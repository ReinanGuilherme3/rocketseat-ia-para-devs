using Dapper;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Pgvector;
using Pgvector.Dapper;
using System.Text;
using UglyToad.PdfPig;

SqlMapper.AddTypeHandler(new VectorTypeHandler());

var builder = WebApplication.CreateBuilder(args);

builder.AddNpgsqlDataSource("naiverag", configureDataSourceBuilder: dataSourceBuilder =>
{
    dataSourceBuilder.UseVector();
});             // nome igual ao AddDatabase("naiverag")

builder.AddOllamaApiClient("chat").AddChatClient();       // nome igual ao AddModel("chat", ...)
builder.AddOllamaApiClient("embeddings").AddEmbeddingGenerator(); // nome igual ao AddModel("embeddings", ...)

var app = builder.Build();

await EnsureDatabaseSchemaAsync(app.Services);
await IndexDocumentIfNeededAsync(app.Services);

// Interface web (Passo 7): como o Aspire não encaminha o teclado pro console
// de um recurso orquestrado, a pergunta é feita por HTTP em vez de stdin.
const string askPageHtml = """
    <!DOCTYPE html>
    <html lang="pt-br">
    <head>
        <meta charset="utf-8" />
        <title>Naive RAG - Os Sertões</title>
        <style>
            body { font-family: sans-serif; max-width: 700px; margin: 2rem auto; padding: 0 1rem; }
            textarea { width: 100%; height: 4rem; font-size: 1rem; }
            button { margin-top: 0.5rem; padding: 0.5rem 1.5rem; font-size: 1rem; }
            #answer { margin-top: 1.5rem; white-space: pre-wrap; border-top: 1px solid #ccc; padding-top: 1rem; }
        </style>
    </head>
    <body>
        <h1>Naive RAG - Os Sertões</h1>
        <textarea id="question" placeholder="Faça uma pergunta sobre o livro..."></textarea>
        <br />
        <button id="ask">Perguntar</button>
        <div id="answer"></div>
        <script>
            document.getElementById('ask').addEventListener('click', async () => {
                const question = document.getElementById('question').value;
                const answerEl = document.getElementById('answer');
                answerEl.textContent = 'Pensando...';

                const response = await fetch('/ask', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ question })
                });

                const data = await response.json();
                answerEl.textContent = data.answer;
            });
        </script>
    </body>
    </html>
    """;

app.MapGet("/", () => Results.Content(askPageHtml, "text/html"));

app.MapPost("/ask", async (AskRequest request, IServiceProvider services) =>
{
    var answer = await AnswerQuestionAsync(services, request.Question);
    return Results.Ok(new AskResponse(answer));
});

// Mantém o loop de console funcionando também: se você anexar um terminal de
// verdade (ex: "aspire exec --resource naiverag-app -- dotnet run"), continua
// dando pra perguntar por ali. Sob o AppHost normal, o ReadLine recebe EOF e
// sai na hora, sem travar o servidor HTTP.
var serverTask = app.RunAsync();
await RunQueryLoopAsync(app.Services);
await serverTask;

static async Task EnsureDatabaseSchemaAsync(IServiceProvider services)
{
    var dataSource = services.GetRequiredService<NpgsqlDataSource>();
    await using var connection = await dataSource.OpenConnectionAsync();

    const string schemaSql = """
        CREATE EXTENSION IF NOT EXISTS vector;

        CREATE TABLE IF NOT EXISTS document_chunks (
            id SERIAL PRIMARY KEY,
            content TEXT NOT NULL,
            embedding vector(768)
        );

        CREATE INDEX IF NOT EXISTS document_chunks_embedding_idx
            ON document_chunks USING hnsw (embedding vector_cosine_ops);
        """;

    await connection.ExecuteAsync(schemaSql);
}

// Pipeline de indexação (Passo 6): extrai o texto do PDF, divide em chunks,
// gera o embedding de cada chunk e persiste tudo em document_chunks.
// Só roda se a tabela ainda estiver vazia, pra não reprocessar o livro a cada start.
static async Task IndexDocumentIfNeededAsync(IServiceProvider services)
{
    var dataSource = services.GetRequiredService<NpgsqlDataSource>();

    await using (var checkConnection = await dataSource.OpenConnectionAsync())
    {
        var existingChunks = await checkConnection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM document_chunks");

        if (existingChunks > 0)
        {
            Console.WriteLine($"Documento já indexado ({existingChunks} chunks). Pulando indexação.");
            return;
        }
    }

    var pdfPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "Recursos", "os-sertoes.pdf"));

    Console.WriteLine($"Extraindo texto de '{pdfPath}'...");
    var text = ExtractText(pdfPath);

    var chunks = ChunkText(text, chunkSize: 1000, overlap: 200).ToList();
    Console.WriteLine($"{chunks.Count} chunks gerados. Calculando embeddings...");

    var embeddingGenerator = services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    var embeddings = await embeddingGenerator.GenerateAsync(chunks);

    await using var connection = await dataSource.OpenConnectionAsync();
    for (var i = 0; i < chunks.Count; i++)
    {
        var vector = new Vector(embeddings[i].Vector.ToArray());
        await connection.ExecuteAsync(
            "INSERT INTO document_chunks (content, embedding) VALUES (@Content, @Embedding)",
            new { Content = chunks[i], Embedding = vector });
    }

    Console.WriteLine("Indexação concluída.");
}

static string ExtractText(string pdfPath)
{
    using var document = PdfDocument.Open(pdfPath);
    var text = new StringBuilder();

    foreach (var page in document.GetPages())
    {
        text.AppendLine(page.Text);
    }

    return text.ToString();
}

static IEnumerable<string> ChunkText(string text, int chunkSize, int overlap)
{
    var normalized = text.Replace("\r\n", "\n");
    var position = 0;

    while (position < normalized.Length)
    {
        var length = Math.Min(chunkSize, normalized.Length - position);
        var chunk = normalized.Substring(position, length).Trim();

        if (chunk.Length > 0)
        {
            yield return chunk;
        }

        if (position + length >= normalized.Length)
        {
            break;
        }

        position += chunkSize - overlap;
    }
}

// Pipeline de consulta (Passo 7): embedding da pergunta, busca dos chunks mais
// próximos por distância de cosseno e resposta do LLM usando esses chunks como contexto.
static async Task RunQueryLoopAsync(IServiceProvider services)
{
    Console.WriteLine();
    Console.WriteLine("Pergunte algo sobre 'Os Sertões' (digite 'sair' para encerrar):");

    while (true)
    {
        Console.Write("> ");
        var question = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(question) || question.Trim().Equals("sair", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        var answer = await AnswerQuestionAsync(services, question);
        Console.WriteLine();
        Console.WriteLine(answer);
        Console.WriteLine();
    }
}

static async Task<string> AnswerQuestionAsync(IServiceProvider services, string question, int topK = 5)
{
    var embeddingGenerator = services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    var questionEmbedding = await embeddingGenerator.GenerateAsync([question]);
    var queryVector = new Vector(questionEmbedding[0].Vector.ToArray());

    var dataSource = services.GetRequiredService<NpgsqlDataSource>();
    await using var connection = await dataSource.OpenConnectionAsync();

    var retrievedChunks = await connection.QueryAsync<string>(
        "SELECT content FROM document_chunks ORDER BY embedding <=> @Embedding LIMIT @TopK",
        new { Embedding = queryVector, TopK = topK });

    var context = string.Join("\n\n---\n\n", retrievedChunks);

    var prompt = $"""
        Responda à pergunta usando apenas o contexto abaixo, extraído do livro "Os Sertões".
        Se a resposta não estiver no contexto, diga que não encontrou.

        Contexto:
        {context}

        Pergunta: {question}
        """;

    var chatClient = services.GetRequiredService<IChatClient>();
    var response = await chatClient.GetResponseAsync(prompt);

    return response.Text;
}

record AskRequest(string Question);

record AskResponse(string Answer);