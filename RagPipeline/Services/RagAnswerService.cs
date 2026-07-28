using System.ClientModel;
using System.Text;
using OpenAI;
using OpenAI.Chat;

namespace RagPipeline.Services;

public record RagAnswer(string Answer, List<RetrievedChunk> Sources);

public class RagAnswerService
{
    private readonly EmbeddingService _embedder;
    private readonly VectorSearchService _search;
    private readonly ChatClient _chatClient;

    /// <summary>
    /// apiKey: your OpenAI key, or a GitHub PAT if endpoint targets GitHub Models.
    /// endpoint: null = api.openai.com. For GitHub Models pass
    /// "https://models.github.ai/inference".
    /// </summary>
    public RagAnswerService(
        EmbeddingService embedder,
        VectorSearchService search,
        string apiKey,
        string chatModel = "gpt-4o-mini",
        string? endpoint = null)
    {
        _embedder = embedder;
        _search = search;

        if (endpoint is null)
        {
            _chatClient = new ChatClient(chatModel, apiKey);
        }
        else
        {
            var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
            _chatClient = new ChatClient(chatModel, new ApiKeyCredential(apiKey), options);
        }
    }

    public async Task<RagAnswer> AskAsync(string question, int topK = 5, CancellationToken ct = default)
    {
        var queryEmbedding = await _embedder.EmbedAsync(question, ct);
        var chunks = await _search.SearchAsync(queryEmbedding, topK, ct);

        if (chunks.Count == 0)
        {
            return new RagAnswer("I couldn't find anything relevant in the ingested documents.", chunks);
        }

        var context = BuildContext(chunks);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are a helpful assistant that answers strictly using the provided context. " +
                "Context includes both prose and tables (rendered as markdown). " +
                "If the answer isn't in the context, say you don't know. " +
                "Cite the source file and page number for each fact you use, e.g. (source.pdf, p.3)."),
            new UserChatMessage($"Context:\n{context}\n\nQuestion: {question}")
        };

        var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: ct);
        var answer = response.Value.Content.Count > 0 ? response.Value.Content[0].Text : string.Empty;

        return new RagAnswer(answer, chunks);
    }

    private static string BuildContext(List<RetrievedChunk> chunks)
    {
        var sb = new StringBuilder();
        foreach (var c in chunks)
        {
            sb.AppendLine($"[Source: {c.FileName}, page {c.PageNumber}]");
            sb.AppendLine(c.Content);
            sb.AppendLine("---");
        }
        return sb.ToString();
    }
}
