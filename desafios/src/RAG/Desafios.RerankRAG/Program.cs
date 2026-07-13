using Desafios.RAG.Shared;
using Desafios.RerankRAG;
using Microsoft.Extensions.AI;
using Npgsql;
using Pgvector;

// Ensina o Dapper a ler/escrever o tipo `vector` do pgvector.
DapperVectorSupport.Register();

var builder = WebApplication.CreateBuilder(args);

// O timeout padrão do HttpClient (100s) é curto demais para indexar o livro inteiro:
// centenas de chunks viram vários requests de embeddings, e o rerank faz uma chamada
// extra ao chat model por pergunta — tudo isso pode demorar mais que isso sem GPU.
builder.Services.ConfigureHttpClientDefaults(http => http.ConfigureHttpClient(
    client => client.Timeout = TimeSpan.FromMinutes(10)));

builder.AddNpgsqlDataSource("rerankrag", configureDataSourceBuilder: dataSourceBuilder =>
{
    dataSourceBuilder.UseVector();
});             // nome igual ao AddDatabase("rerankrag") no AppHost

builder.AddOllamaApiClient("chat").AddChatClient();               // nome igual ao AddModel("chat", ...)
builder.AddOllamaApiClient("embeddings").AddEmbeddingGenerator(); // nome igual ao AddModel("embeddings", ...)

// Classes da pipeline, registradas no container de DI para serem resolvidas uma
// única vez (são stateless, só embrulham dependências que já são singletons).
builder.Services.AddSingleton<FlatChunkStore>();
builder.Services.AddSingleton<RerankRagPipeline>();

var app = builder.Build();

// Passo 1: garante o schema do banco e indexa o livro (só na primeira execução).
// Mesmo formato de índice do Naive RAG — a diferença do Rerank fica toda na consulta.
var chunkStore = app.Services.GetRequiredService<FlatChunkStore>();
var embeddingGenerator = app.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

await chunkStore.EnsureSchemaAsync();
await BookIndexer.IndexIfNeededAsync(
    chunkStore,
    embeddingGenerator,
    BookPathResolver.ResolveRecursoPath("os-sertoes.pdf"),
    chunkSize: 1000,
    overlap: 200);

// Passo 2: expõe a pipeline via web (página + /ask) e via console.
var ragPipeline = app.Services.GetRequiredService<RerankRagPipeline>();

app.MapRagAskEndpoints("Rerank RAG - Os Sertões", question => ragPipeline.AnswerAsync(question));

// Mantém o loop de console funcionando também: se você anexar um terminal de
// verdade (ex: "aspire exec --resource rerankrag-app -- dotnet run"), continua
// dando pra perguntar por ali. Sob o AppHost normal, o ReadLine recebe EOF e
// sai na hora, sem travar o servidor HTTP.
var serverTask = app.RunAsync();
await RagQueryConsole.RunAsync("Os Sertões", question => ragPipeline.AnswerAsync(question));
await serverTask;
