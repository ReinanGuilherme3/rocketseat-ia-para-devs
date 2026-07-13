namespace Desafios.RAG.Shared;

public static class TextChunker
{
    // Divide o texto em pedaços de tamanho fixo com sobreposição (overlap) entre eles,
    // usada por todas as variantes de RAG (Naive, Parent e Rerank) para gerar chunks
    // a partir do texto bruto extraído do PDF.
    public static IEnumerable<string> Chunk(string text, int chunkSize, int overlap)
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
}
