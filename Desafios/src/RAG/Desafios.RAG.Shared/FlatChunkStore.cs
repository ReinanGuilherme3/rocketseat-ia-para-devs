using Dapper;
using Npgsql;
using Pgvector;

namespace Desafios.RAG.Shared;

/// <summary>
/// Acesso a um índice vetorial "plano": todos os chunks do documento vivem em uma
/// única tabela (document_chunks), cada um com seu próprio embedding. É a estrutura
/// usada tanto pelo Naive RAG quanto pelo Rerank RAG — a diferença entre as duas
/// abordagens está em como o resultado desta classe é usado para montar a resposta,
/// não em como os dados são guardados.
/// </summary>
public class FlatChunkStore(NpgsqlDataSource dataSource)
{
    public async Task EnsureSchemaAsync()
    {
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

        // Numa base nova, a extensão `vector` acabou de ser criada acima. O Npgsql
        // carrega o catálogo de tipos uma vez, no primeiro open, e o cacheia — então
        // ele ainda não conhece o OID do tipo `vector` e, ao gravar um Pgvector.Vector,
        // lança "Writing values of 'Pgvector.Vector' is not supported...". Recarregar
        // os tipos aqui ensina o data source a mapear o tipo recém-criado.
        await connection.ReloadTypesAsync();
    }

    public async Task<long> CountChunksAsync()
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM document_chunks");
    }

    public async Task InsertChunkAsync(string content, Vector embedding)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO document_chunks (content, embedding) VALUES (@Content, @Embedding)",
            new { Content = content, Embedding = embedding });
    }

    /// <summary>
    /// Busca os `count` chunks cujo embedding está mais próximo (menor distância de
    /// cosseno) do embedding informado. Usada tanto para a busca final do Naive RAG
    /// (count = topK) quanto para o passo de recall do Rerank RAG, onde count é bem
    /// maior que o topK final — o rerank é quem reduz a lista depois.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindNearestAsync(Vector queryEmbedding, int count)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        var chunks = await connection.QueryAsync<string>(
            "SELECT content FROM document_chunks ORDER BY embedding <=> @Embedding LIMIT @Count",
            new { Embedding = queryEmbedding, Count = count });

        return chunks.AsList();
    }
}
