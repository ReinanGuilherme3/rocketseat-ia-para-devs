namespace Desafios.RAG.Shared;

public static class RagWebUi
{
    // Página HTML mínima, compartilhada pelas três variantes de RAG. Só o título muda.
    public static string BuildAskPage(string title) => $$"""
        <!DOCTYPE html>
        <html lang="pt-br">
        <head>
            <meta charset="utf-8" />
            <title>{{title}}</title>
            <style>
                body { font-family: sans-serif; max-width: 700px; margin: 2rem auto; padding: 0 1rem; }
                textarea { width: 100%; height: 4rem; font-size: 1rem; }
                button { margin-top: 0.5rem; padding: 0.5rem 1.5rem; font-size: 1rem; }
                #answer { margin-top: 1.5rem; white-space: pre-wrap; border-top: 1px solid #ccc; padding-top: 1rem; }
            </style>
        </head>
        <body>
            <h1>{{title}}</h1>
            <textarea id="question" placeholder="Faça uma pergunta sobre o livro..."></textarea>
            <br />
            <button id="ask">Perguntar</button>
            <div id="answer"></div>
            <script>
                document.getElementById('ask').addEventListener('click', async () => {
                    const question = document.getElementById('question').value;
                    const answerEl = document.getElementById('answer');
                    answerEl.textContent = 'Pensando...';

                    const response = await fetch('/ask', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ question })
                    });

                    const data = await response.json();
                    answerEl.textContent = data.answer;
                });
            </script>
        </body>
        </html>
        """;
}

public record AskRequest(string Question);

public record AskResponse(string Answer);
