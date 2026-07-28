using System.ComponentModel.DataAnnotations.Schema;

namespace RagPipeline.Models;

public class RagSourceDocument
{
    public int Id { get; set; }
    public string FileName { get; set; } = default!;
    public string? Title { get; set; }
    public DateTime IngestedAtUtc { get; set; } = DateTime.UtcNow;
    public int PageCount { get; set; }

    public List<RagDocumentChunk> Chunks { get; set; } = new();
}

public enum ChunkType
{
    Text = 0,
    Table = 1
}

public class RagDocumentChunk
{
    public int Id { get; set; }

    public int SourceDocumentId { get; set; }
    public RagSourceDocument SourceDocument { get; set; } = default!;

    public int ChunkIndex { get; set; }
    public int PageNumber { get; set; }
    public ChunkType Type { get; set; }

    /// <summary>Raw chunk text. For tables this is a markdown-rendered version of the table.</summary>
    public string Content { get; set; } = default!;

    [NotMapped]
    public float[] Embedding { get; set; }

    public int TokenCount { get; set; }
}
