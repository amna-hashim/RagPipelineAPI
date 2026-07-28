using RagPipeline.Models;

namespace RagPipeline.Interfaces;

public interface IDocumentChunksRepository
{
    Task AddWithEmbeddingAsync(RagDocumentChunk chunk, float[] embedding, CancellationToken ct);
    Task<List<RagDocumentChunk>> GetBySourceDocumentIdAsync(int sourceDocumentId, CancellationToken ct);
}
