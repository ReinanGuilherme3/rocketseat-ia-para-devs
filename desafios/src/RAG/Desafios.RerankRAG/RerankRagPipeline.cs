using Desafios.RAG.Shared;
using Microsoft.Extensions.AI;
using Pgvector;

namespace Desafios.RerankRAG;

/// <summary>
/// Rerank RAG: dois estágios. (1) recall amplo — busca vetorial trazendo bem mais
/// candidatos do que o necessário; (2) rerank de precisão — o <see cref="ChunkReranker"/>
/// reordena esses candidatos por relevância real à pergunta. Só os melhores depois
/// do rerank viram contexto do prompt final.
/// </summary>
public class RerankRagPipeline(
    FlatChunkStore store,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChatClient chatClient)
{
    public async Task<string> AnswerAsync(string question, int candidateCount = 15, int topK = 5)
    {
        // 1) Pergunta -> embedding.
        var questionEmbedding = await embeddingGenerator.GenerateAsync([question]);
        var queryVector = new Vector(questionEmbedding[0].Vector.ToArray());

        // 2) Recall: traz candidateCount candidatos (bem mais que o topK final).
        var candidates = await store.FindNearestAsync(queryVector, candidateCount);

        // 3) Rerank: o chat model reordena os candidatos por relevância real à pergunta.
        var rerankedChunks = await ChunkReranker.RerankAsync(chatClient, question, candidates);
        var context = string.Join("\n\n---\n\n", rerankedChunks.Take(topK));

        // 4) LLM responde usando só os topK chunks depois do rerank.
        var prompt = AnswerPromptBuilder.Build(context, question);
        var response = await chatClient.GetResponseAsync(prompt);

        return response.Text;
    }
}
