using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Desafios.RAG.Shared;

/// <summary>
/// Expõe a mesma interface web (página HTML + endpoint POST /ask) para qualquer uma
/// das variantes de RAG. Cada app só precisa fornecer o título da página e a função
/// que sabe responder a uma pergunta — o "como" responder fica isolado na pipeline
/// de cada projeto (NaiveRagPipeline, ParentRagPipeline, RerankRagPipeline).
/// </summary>
public static class RagWebEndpoints
{
    public static void MapRagAskEndpoints(this WebApplication app, string pageTitle, Func<string, Task<string>> answerQuestion)
    {
        var askPageHtml = RagWebUi.BuildAskPage(pageTitle);

        app.MapGet("/", () => Results.Content(askPageHtml, "text/html"));

        app.MapPost("/ask", async (AskRequest request) =>
        {
            var answer = await answerQuestion(request.Question);
            return Results.Ok(new AskResponse(answer));
        });
    }
}
