using Desafios.RAG.Shared;
using Microsoft.Extensions.AI;
using Pgvector;

namespace Desafios.ParentRAG;

/// <summary>
/// Indexação em dois níveis: o texto do PDF vira chunks "pai" grandes; cada pai é
/// dividido em chunks "filho" menores, e só os filhos recebem embedding. Só roda de
/// fato se a tabela de filhos ainda estiver vazia, para não reprocessar o PDF a cada
/// reinício da app.
/// </summary>
public static class ParentDocumentIndexer
{
    public static async Task IndexIfNeededAsync(
        ParentChildChunkStore store,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        string pdfPath,
        int parentChunkSize,
        int parentOverlap,
        int childChunkSize,
        int childOverlap)
    {
        var existingChildren = await store.CountChildChunksAsync();
        if (existingChildren > 0)
        {
            Console.WriteLine($"Documento já indexado ({existingChildren} chunks filhos). Pulando indexação.");
            return;
        }

        Console.WriteLine($"Extraindo texto de '{pdfPath}'...");
        var rawText = PdfTextExtractor.ExtractText(pdfPath);
        // Descarta o aparato editorial (sumário, ensaios críticos, colofão) para não
        // poluir o índice vetorial com texto "meta" sobre o livro. Veja BookContentTrimmer.
        var text = BookContentTrimmer.TrimToBookContent(rawText);

        var parentChunks = TextChunker.Chunk(text, parentChunkSize, parentOverlap).ToList();
        Console.WriteLine($"{parentChunks.Count} chunks pai gerados. Gerando chunks filhos e embeddings...");

        var totalChildren = 0;
        foreach (var parentContent in parentChunks)
        {
            var parentId = await store.InsertParentAsync(parentContent);

            var childChunks = TextChunker.Chunk(parentContent, childChunkSize, childOverlap).ToList();
            var childEmbeddings = await embeddingGenerator.GenerateAsync(childChunks);

            for (var i = 0; i < childChunks.Count; i++)
            {
                var vector = new Vector(childEmbeddings[i].Vector.ToArray());
                await store.InsertChildAsync(parentId, childChunks[i], vector);
            }

            totalChildren += childChunks.Count;
        }

        Console.WriteLine($"Indexação concluída: {parentChunks.Count} chunks pai, {totalChildren} chunks filhos.");
    }
}
