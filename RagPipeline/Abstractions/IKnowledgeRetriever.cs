namespace RagPipeline.Abstractions;

public record KnowledgeAnswer(string Answer, IReadOnlyList<KnowledgeSource> Sources);

public record KnowledgeSource(string FileName, int PageNumber, double Relevance);

/// <summary>
/// The only thing the chatbot project should reference. Keeps PdfPig, the
/// embedding client, and vector-search SQL entirely inside the RAG project.
/// </summary>
public interface IKnowledgeRetriever
{
    Task<KnowledgeAnswer> AskAsync(string question, CancellationToken ct = default);
}

/// <summary>Separate from querying - typically only called from an ingestion/admin path, not the chat turn.</summary>
public interface IKnowledgeIngestor
{
    Task<int> IngestPdfAsync(string filePath, CancellationToken ct = default);
}
