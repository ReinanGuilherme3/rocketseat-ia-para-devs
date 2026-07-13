namespace Desafios.RAG.Shared;

/// <summary>
/// Monta o prompt final enviado ao chat model: contexto recuperado + pergunta do
/// usuário, com instrução explícita para não inventar resposta fora do contexto
/// (mitiga alucinação). É igual nas três abordagens de RAG — o que muda entre elas
/// é só como o contexto foi selecionado antes de chegar aqui.
/// </summary>
public static class AnswerPromptBuilder
{
    public static string Build(string context, string question, string sourceDescription = "do livro \"Os Sertões\"")
        => $"""
            Responda à pergunta usando apenas o contexto abaixo, extraído {sourceDescription}.
            Se a resposta não estiver no contexto, diga que não encontrou.

            Contexto:
            {context}

            Pergunta: {question}
            """;
}
