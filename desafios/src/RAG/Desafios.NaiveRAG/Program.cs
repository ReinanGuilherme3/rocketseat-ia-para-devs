using Desafios.NaiveRAG;
using Desafios.RAG.Shared;
using Microsoft.Extensions.AI;
using Npgsql;
using Pgvector;

// Ensina o Dapper a ler/escrever o tipo `vector` do pgvector.
DapperVectorSupport.Register();

var builder = WebApplication.CreateBuilder(args);

// O timeout padrão do HttpClient (100s) é curto demais para indexar o livro inteiro:
// centenas de chunks viram um único request de embeddings, e respostas do chat model
// também podem demorar mais que isso rodando sem GPU.
builder.Services.ConfigureHttpClientDefaults(http => http.ConfigureHttpClient(
    client => client.Timeout = TimeSpan.FromMinutes(10)));

builder.AddNpgsqlDataSource("naiverag", configureDataSourceBuilder: dataSourceBuilder =>
{
    dataSourceBuilder.UseVector();
});             // nome igual ao AddDatabase("naiverag") no AppHost

builder.AddOllamaApiClient("chat").AddChatClient();               // nome igual ao AddModel("chat", ...)
builder.AddOllamaApiClient("embeddings").AddEmbeddingGenerator(); // nome igual ao AddModel("embeddings", ...)

// Classes da pipeline, registradas no container de DI para serem resolvidas uma
// única vez (são stateless, só embrulham dependências que já são singletons).
builder.Services.AddSingleton<FlatChunkStore>();
builder.Services.AddSingleton<NaiveRagPipeline>();

var app = builder.Build();

// Passo 1: garante o schema do banco e indexa o livro (só na primeira execução).
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
var ragPipeline = app.Services.GetRequiredService<NaiveRagPipeline>();

app.MapRagAskEndpoints("Naive RAG - Os Sertões", question => ragPipeline.AnswerAsync(question));

// Mantém o loop de console funcionando também: se você anexar um terminal de
// verdade (ex: "aspire exec --resource naiverag-app -- dotnet run"), continua
// dando pra perguntar por ali. Sob o AppHost normal, o ReadLine recebe EOF e
// sai na hora, sem travar o servidor HTTP.
var serverTask = app.RunAsync();
await RagQueryConsole.RunAsync("Os Sertões", question => ragPipeline.AnswerAsync(question));
await serverTask;
