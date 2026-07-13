namespace Desafios.RAG.Shared;

/// <summary>
/// Loop de console simples para testar a pipeline de RAG sem precisar do navegador.
/// Sob o AppHost normal (sem terminal de verdade anexado ao processo), o ReadLine
/// recebe EOF na primeira iteração e o loop sai na hora, sem travar o servidor HTTP
/// que já está rodando em paralelo.
/// </summary>
public static class RagQueryConsole
{
    public static async Task RunAsync(string bookLabel, Func<string, Task<string>> answerQuestion)
    {
        Console.WriteLine();
        Console.WriteLine($"Pergunte algo sobre '{bookLabel}' (digite 'sair' para encerrar):");

        while (true)
        {
            Console.Write("> ");
            var question = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(question) || question.Trim().Equals("sair", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var answer = await answerQuestion(question);
            Console.WriteLine();
            Console.WriteLine(answer);
            Console.WriteLine();
        }
    }
}
