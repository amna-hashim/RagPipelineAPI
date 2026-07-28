using RagPipeline.Models;

namespace RagPipeline.Interfaces;

public interface ISourceDocumentRepository
{
    Task<RagSourceDocument?> GetByIdAsync(int id, CancellationToken ct);
    Task<RagSourceDocument?> GetWithChunksAsync(int id, CancellationToken ct);

    Task AddAsync(RagSourceDocument document, CancellationToken ct);
}
