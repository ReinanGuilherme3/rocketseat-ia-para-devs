using Dapper;
using Npgsql;
using Pgvector;

namespace Desafios.ParentRAG;

/// <summary>
/// Acesso aos dois níveis de chunk do Parent RAG:
///  - parent_chunks: pedaços grandes do texto, sem embedding — é o que vira contexto
///    para o LLM.
///  - child_chunks: pedaços pequenos de cada parent, cada um com seu próprio
///    embedding — é contra eles que a busca vetorial roda, porque textos pequenos
///    "casam" melhor semanticamente com uma pergunta pontual do que um bloco grande.
/// </summary>
public class ParentChildChunkStore(NpgsqlDataSource dataSource)
{
    public async Task EnsureSchemaAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        const string schemaSql = """
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS parent_chunks (
                id SERIAL PRIMARY KEY,
                content TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS child_chunks (
                id SERIAL PRIMARY KEY,
                parent_id INTEGER NOT NULL REFERENCES parent_chunks(id) ON DELETE CASCADE,
                content TEXT NOT NULL,
                embedding vector(768)
            );

            CREATE INDEX IF NOT EXISTS child_chunks_embedding_idx
                ON child_chunks USING hnsw (embedding vector_cosine_ops);
            """;

        await connection.ExecuteAsync(schemaSql);

        // Numa base nova, a extensão `vector` acabou de ser criada acima. O Npgsql
        // carrega o catálogo de tipos uma vez, no primeiro open, e o cacheia — então
        // ele ainda não conhece o OID do tipo `vector` e, ao gravar um Pgvector.Vector,
        // lança "Writing values of 'Pgvector.Vector' is not supported...". Recarregar
        // os tipos aqui ensina o data source a mapear o tipo recém-criado.
        await connection.ReloadTypesAsync();
    }

    public async Task<long> CountChildChunksAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM child_chunks");
    }

    public async Task<int> InsertParentAsync(string content)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO parent_chunks (content) VALUES (@Content) RETURNING id",
            new { Content = content });
    }

    public async Task InsertChildAsync(int parentId, string content, Vector embedding)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO child_chunks (parent_id, content, embedding) VALUES (@ParentId, @Content, @Embedding)",
            new { ParentId = parentId, Content = content, Embedding = embedding });
    }

    /// <summary>
    /// Busca os child chunks mais próximos da pergunta, mas devolve o conteúdo do
    /// parent de cada um — agrupando por parent_id e usando a menor distância entre
    /// os filhos daquele grupo como critério de ordenação (o filho mais parecido
    /// "vota" pelo pai inteiro). É o coração do padrão Parent Document Retriever.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindNearestParentsAsync(Vector queryEmbedding, int topParents)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        const string retrievalSql = """
            SELECT pc.content
            FROM child_chunks cc
            JOIN parent_chunks pc ON pc.id = cc.parent_id
            GROUP BY pc.id, pc.content
            ORDER BY MIN(cc.embedding <=> @Embedding)
            LIMIT @TopParents
            """;

        var parents = await connection.QueryAsync<string>(
            retrievalSql, new { Embedding = queryEmbedding, TopParents = topParents });

        return parents.AsList();
    }
}
