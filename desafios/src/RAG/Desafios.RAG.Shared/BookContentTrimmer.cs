using System.Text.RegularExpressions;

namespace Desafios.RAG.Shared;

/// <summary>
/// Recorta o texto extraído do PDF de "Os Sertões" para conter apenas o livro que
/// Euclides da Cunha escreveu, descartando o aparato editorial desta edição: no início
/// a capa, os patrocinadores, o sumário e os ensaios críticos (Araripe Júnior, Darcy
/// Ribeiro, Paulo Roberto Pereira); no fim, o colofão e a ficha catalográfica.
///
/// Por que isso importa para o RAG: esse aparato é "meta" — fala SOBRE o livro, a guerra
/// e Euclides — e por isso casa muito bem com perguntas também "meta" (ex.: "qual foi o
/// contexto histórico e político que levou à Guerra de Canudos?"). Como todo o corpus é
/// um único livro, as distâncias vetoriais ficam muito próximas umas das outras e esses
/// trechos editoriais acabam dominando o topo do retrieval, empurrando a narrativa real
/// para fora do contexto enviado ao LLM — que então responde "não encontrei". Remover o
/// aparato na indexação melhora a precisão dos três RAGs (Naive, Parent e Rerank).
/// </summary>
public static class BookContentTrimmer
{
    // Primeira frase da "Nota preliminar", escrita pelo próprio Euclides — é o começo
    // real do livro. Mantemos a Nota Preliminar de propósito: é nela que ele enquadra o
    // sentido histórico/político da Campanha de Canudos.
    private const string StartMarker = "Escrito nos raros intervalos de folga";

    // Começo do colofão/créditos editoriais, logo após as notas finais de Euclides.
    private const string EndMarker = "Patrocínio:Realização";

    /// <summary>
    /// Normaliza os espaços em branco (o extrator do PDF espalha quebras de linha por
    /// toda parte) e devolve só o trecho entre os marcadores de início e fim. Se algum
    /// marcador não for encontrado (PDF diferente do esperado), faz o fallback seguro de
    /// devolver o texto todo em vez de quebrar a indexação.
    /// </summary>
    public static string TrimToBookContent(string rawText)
    {
        var text = Regex.Replace(rawText, @"\s+", " ");

        var start = text.IndexOf(StartMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return text.Trim();
        }

        var end = text.IndexOf(EndMarker, start, StringComparison.OrdinalIgnoreCase);
        var book = end < 0 ? text[start..] : text[start..end];

        return book.Trim();
    }
}
