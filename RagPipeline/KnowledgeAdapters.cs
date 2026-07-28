using RagPipeline.Abstractions;
using RagPipeline.Services;

namespace RagPipeline;

public class KnowledgeRetriever : IKnowledgeRetriever
{
    private readonly RagAnswerService _rag;

    public KnowledgeRetriever(RagAnswerService rag) => _rag = rag;

    public async Task<KnowledgeAnswer> AskAsync(string question, CancellationToken ct = default)
    {
        var result = await _rag.AskAsync(question, ct: ct);
        var sources = result.Sources
            .Select(s => new KnowledgeSource(s.FileName, s.PageNumber, 1 - s.Distance)) // cosine distance -> rough similarity
            .ToList();

        return new KnowledgeAnswer(result.Answer, sources);
    }
}

public class KnowledgeIngestor : IKnowledgeIngestor
{
    private readonly IngestionService _ingestion;

    public KnowledgeIngestor(IngestionService ingestion) => _ingestion = ingestion;

    public async Task<int> IngestPdfAsync(string filePath, CancellationToken ct = default)
    {
        var doc = await _ingestion.IngestAsync(filePath, ct);
        return doc.Chunks.Count;
    }
}
