using Desafios.RAG.Shared;
using Microsoft.Extensions.AI;
using Pgvector;

namespace Desafios.NaiveRAG;

/// <summary>
/// Naive RAG: a abordagem mais simples de RAG. A pergunta vira embedding, comparamos
/// contra o embedding de cada chunk do documento e pegamos os `topK` mais próximos
/// (menor distância de cosseno) como contexto — sem nenhum passo intermediário de
/// reorganização, hierarquia ou reranqueamento.
/// </summary>
public class NaiveRagPipeline(
    FlatChunkStore store,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChatClient chatClient)
{
    public async Task<string> AnswerAsync(string question, int topK = 5)
    {
        // 1) Pergunta -> embedding, para poder comparar com os embeddings já indexados.
        var questionEmbedding = await embeddingGenerator.GenerateAsync([question]);
        var queryVector = new Vector(questionEmbedding[0].Vector.ToArray());

        // 2) Busca vetorial direta: os topK chunks mais parecidos com a pergunta.
        var retrievedChunks = await store.FindNearestAsync(queryVector, topK);
        var context = string.Join("\n\n---\n\n", retrievedChunks);

        // 3) LLM responde usando só o contexto recuperado.
        var prompt = AnswerPromptBuilder.Build(context, question);
        var response = await chatClient.GetResponseAsync(prompt);

        return response.Text;
    }
}
