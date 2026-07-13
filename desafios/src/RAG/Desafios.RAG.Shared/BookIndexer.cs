using Microsoft.Extensions.AI;
using Pgvector;

namespace Desafios.RAG.Shared;

/// <summary>
/// Passo único de "carga" do livro num índice vetorial plano: extrai o texto do PDF,
/// divide em chunks e calcula/persiste o embedding de cada um. Só roda de fato se a
/// tabela ainda estiver vazia, para não reprocessar o PDF a cada reinício da app.
/// Usado pelo Naive RAG e pelo Rerank RAG, que compartilham o mesmo formato de índice
/// (veja <see cref="FlatChunkStore"/>). O Parent RAG tem seu próprio indexador porque
/// o formato de dados dele é diferente (dois níveis de chunk).
/// </summary>
public static class BookIndexer
{
    public static async Task IndexIfNeededAsync(
        FlatChunkStore store,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        string pdfPath,
        int chunkSize,
        int overlap)
    {
        var existingChunks = await store.CountChunksAsync();
        if (existingChunks > 0)
        {
            Console.WriteLine($"Documento já indexado ({existingChunks} chunks). Pulando indexação.");
            return;
        }

        Console.WriteLine($"Extraindo texto de '{pdfPath}'...");
        var rawText = PdfTextExtractor.ExtractText(pdfPath);
        // Descarta o aparato editorial (sumário, ensaios críticos, colofão) para não
        // poluir o índice vetorial com texto "meta" sobre o livro. Veja BookContentTrimmer.
        var text = BookContentTrimmer.TrimToBookContent(rawText);

        var chunks = TextChunker.Chunk(text, chunkSize, overlap).ToList();
        Console.WriteLine($"{chunks.Count} chunks gerados. Calculando embeddings...");

        var embeddings = await embeddingGenerator.GenerateAsync(chunks);

        for (var i = 0; i < chunks.Count; i++)
        {
            var vector = new Vector(embeddings[i].Vector.ToArray());
            await store.InsertChunkAsync(chunks[i], vector);
        }

        Console.WriteLine("Indexação concluída.");
    }
}
