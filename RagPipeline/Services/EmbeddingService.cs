using System.ClientModel;
using OpenAI;
using OpenAI.Embeddings;

namespace RagPipeline.Services;

public class EmbeddingService
{
    private readonly EmbeddingClient _client;

    /// <summary>
    /// apiKey: your OpenAI key, or a GitHub PAT (with "models: read" permission) if endpoint
    /// points at GitHub Models.
    /// endpoint: null = api.openai.com. For GitHub Models pass
    /// "https://models.github.ai/inference".
    /// </summary>
    public EmbeddingService(string apiKey, string model = "text-embedding-3-small", string? endpoint = null)
    {
        if (endpoint is null)
        {
            _client = new EmbeddingClient(model, apiKey);
        }
        else
        {
            var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
            _client = new EmbeddingClient(model, new ApiKeyCredential(apiKey), options);
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }

    /// <summary>Batch embedding is much cheaper/faster than one call per chunk.</summary>
    public async Task<List<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var result = await _client.GenerateEmbeddingsAsync(texts.ToList(), cancellationToken: ct);
        return result.Value.Select(e => e.ToFloats().ToArray()).ToList();
    }
}
