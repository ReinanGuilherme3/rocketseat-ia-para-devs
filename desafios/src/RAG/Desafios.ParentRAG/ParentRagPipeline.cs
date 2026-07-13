using Desafios.RAG.Shared;
using Microsoft.Extensions.AI;
using Pgvector;

namespace Desafios.ParentRAG;

/// <summary>
/// Parent RAG: a busca vetorial roda nos chunks pequenos (mais fáceis de "casar"
/// semanticamente com a pergunta), mas o contexto enviado ao LLM é o chunk pai
/// inteiro — mais texto ao redor do trecho relevante, o que costuma ajudar o modelo
/// a responder perguntas que dependem de contexto mais amplo do que uma frase isolada.
/// </summary>
public class ParentRagPipeline(
    ParentChildChunkStore store,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChatClient chatClient)
{
    // topParents = 8: as distâncias neste corpus (um livro só) são muito próximas
    // umas das outras, e o aparato editorial do PDF (índice, ensaios de Araripe/Darcy
    // Ribeiro, histórico das crônicas) casa bem com perguntas "meta" e domina os
    // primeiros lugares. Com topParents=4 a narrativa real da guerra — que fica nos
    // ranks ~8-12 — nunca chega ao LLM. Trazer mais pais garante o contexto de fato.
    public async Task<string> AnswerAsync(string question, int topParents = 8)
    {
        // 1) Pergunta -> embedding.
        var questionEmbedding = await embeddingGenerator.GenerateAsync([question]);
        var queryVector = new Vector(questionEmbedding[0].Vector.ToArray());

        // 2) Busca pelos filhos mais próximos, mas recupera o texto do pai de cada um.
        var retrievedParents = await store.FindNearestParentsAsync(queryVector, topParents);
        var context = string.Join("\n\n---\n\n", retrievedParents);

        // 3) LLM responde usando o contexto (mais amplo) dos chunks pai.
        var prompt = AnswerPromptBuilder.Build(context, question);
        var response = await chatClient.GetResponseAsync(prompt);

        return response.Text;
    }
}
