using RagPipeline.Interfaces;
using RagPipeline.Models;

namespace RagPipeline.Services;

public class IngestionService
{
    private readonly PdfExtractionService _extractor;
    private readonly ChunkingService _chunker;
    private readonly EmbeddingService _embedder;
    private readonly ISourceDocumentRepository _sourceDocuments;
    private readonly IDocumentChunksRepository _documentChunks;

    public IngestionService(
        PdfExtractionService extractor,
        ChunkingService chunker,
        EmbeddingService embedder,
        ISourceDocumentRepository sourceDocuments,
        IDocumentChunksRepository documentChunks)
    {
        _extractor = extractor;
        _chunker = chunker;
        _embedder = embedder;
        _sourceDocuments = sourceDocuments;
        _documentChunks = documentChunks;
    }

    public async Task<RagSourceDocument> IngestAsync(string pdfPath, CancellationToken ct = default)
    {
        var (pageCount, blocks) = _extractor.Extract(pdfPath);
        var pending = _chunker.Chunk(blocks);

        var sourceDoc = new RagSourceDocument
        {
            FileName = Path.GetFileName(pdfPath),
            Title = Path.GetFileNameWithoutExtension(pdfPath),
            PageCount = pageCount
        };
        await _sourceDocuments.AddAsync(sourceDoc, ct); // populates sourceDoc.Id

        // Embed in batches to keep request sizes reasonable.
        const int batchSize = 64;
        int chunkIndex = 0;

        for (int i = 0; i < pending.Count; i += batchSize)
        {
            var batch = pending.Skip(i).Take(batchSize).ToList();
            var embeddings = await _embedder.EmbedBatchAsync(batch.Select(b => b.Content), ct);

            for (int j = 0; j < batch.Count; j++)
            {
                var chunk = new RagDocumentChunk
                {
                    SourceDocumentId = sourceDoc.Id,
                    ChunkIndex = chunkIndex++,
                    PageNumber = batch[j].PageNumber,
                    Type = batch[j].Type,
                    Content = batch[j].Content,
                    TokenCount = batch[j].TokenCount
                };

                // Embedding is written via raw SQL (CAST ... AS VECTOR(1536)) since EF Core
                // can't map SQL Server's VECTOR column type directly.
                await _documentChunks.AddWithEmbeddingAsync(chunk, embeddings[j], ct);
                sourceDoc.Chunks.Add(chunk);
            }
        }

        return sourceDoc;
    }
}
