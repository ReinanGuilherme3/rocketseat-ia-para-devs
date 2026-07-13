using Dapper;
using Pgvector.Dapper;

namespace Desafios.RAG.Shared;

public static class DapperVectorSupport
{
    // Registra o type handler que ensina o Dapper a ler/escrever o tipo `vector`
    // do pgvector. Precisa ser chamado uma única vez, no início de cada app.
    public static void Register() => SqlMapper.AddTypeHandler(new VectorTypeHandler());
}
