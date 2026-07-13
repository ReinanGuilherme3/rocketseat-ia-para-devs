using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Desafios.RerankRAG;

/// <summary>
/// Passo de rerank: recebe um conjunto de candidatos (já filtrados por similaridade
/// de embedding) e usa o próprio chat model para dar uma nota de relevância a cada
/// um, reordenando a lista. É o que diferencia o Rerank RAG do Naive RAG — a busca
/// vetorial vira só um filtro grosseiro de recall; quem decide a ordem final de
/// precisão é um modelo lendo a pergunta e cada candidato lado a lado.
/// </summary>
public static class ChunkReranker
{
    public static async Task<IReadOnlyList<string>> RerankAsync(
        IChatClient chatClient, string question, IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var rerankResponse = await chatClient.GetResponseAsync(BuildRerankPrompt(question, candidates));
        var scores = ParseScores(rerankResponse.Text, candidates.Count);

        return candidates
            .Select((content, index) => (content, score: scores[index], index))
            .OrderByDescending(c => c.score)
            .ThenBy(c => c.index) // desempate: mantém a ordem original (por distância) quando a nota empata
            .Select(c => c.content)
            .ToList();
    }

    private static string BuildRerankPrompt(string question, IReadOnlyList<string> candidates)
    {
        var numberedCandidates = string.Join(
            "\n\n", candidates.Select((chunk, index) => $"[{index + 1}]\n{chunk}"));

        return $"""
            Você é um avaliador de relevância. Para a pergunta abaixo, dê uma nota de 0 a 10
            para cada trecho numerado, indicando o quão útil ele é para responder a pergunta.

            Responda APENAS com uma linha por trecho, no formato "numero: nota", sem texto extra.
            Exemplo de resposta para 3 trechos:
            1: 7
            2: 2
            3: 9

            Pergunta: {question}

            Trechos:
            {numberedCandidates}
            """;
    }

    // Extrai pares "numero: nota" da resposta do modelo. Se ele não seguir o formato
    // pedido para algum item, esse item fica com nota 0 (vai pro fim da lista, mas
    // não derruba a aplicação).
    private static double[] ParseScores(string rerankResponseText, int candidateCount)
    {
        var scoreLinePattern = new Regex(@"(\d+)\s*[:\-]\s*(\d+(?:[.,]\d+)?)");
        var scores = new double[candidateCount];

        foreach (Match match in scoreLinePattern.Matches(rerankResponseText))
        {
            var index = int.Parse(match.Groups[1].Value) - 1;
            var score = double.Parse(match.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);

            if (index >= 0 && index < candidateCount)
            {
                scores[index] = score;
            }
        }

        return scores;
    }
}
