var builder = DistributedApplication.CreateBuilder(args);

#region Ollama
var ollama = builder.AddOllama("ollama")
    .WithGPUSupport()
    .WithDataVolume();   // evita rebaixar os modelos toda vez que você reinicia

var chatModel = ollama.AddModel("chat", "llama3.2");
var embedModel = ollama.AddModel("embeddings", "nomic-embed-text");
#endregion

#region Postgres + PGVector
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg17")          // Postgres 17 + pgvector já instalado
    .WithDataVolume()
    .WithPgAdmin();                 // opcional, UI web pra inspecionar as tabelas

var naiveRagDb = postgres.AddDatabase("naiverag");
var parentRagDb = postgres.AddDatabase("parentrag");
var rerankRagDb = postgres.AddDatabase("rerankrag");
#endregion

var naiveRAG = builder.AddProject<Projects.Desafios_NaiveRAG>("naiverag-app")
    .WithReference(chatModel)
    .WithReference(embedModel)
    .WithReference(naiveRagDb)
    .WaitFor(postgres)
    .WaitFor(ollama)
    .WaitFor(chatModel)
    .WaitFor(embedModel);

var parentRAG = builder.AddProject<Projects.Desafios_ParentRAG>("parentrag-app")
    .WithReference(chatModel)
    .WithReference(embedModel)
    .WithReference(parentRagDb)
    .WaitFor(postgres)
    .WaitFor(ollama)
    .WaitFor(chatModel)
    .WaitFor(embedModel);

builder.AddProject<Projects.Desafios_RerankRAG>("rerankrag-app")
    .WithReference(chatModel)
    .WithReference(embedModel)
    .WithReference(rerankRagDb)
    .WaitFor(postgres)
    .WaitFor(ollama)
    .WaitFor(chatModel)
    .WaitFor(embedModel);

builder.Build().Run();