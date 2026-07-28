using RagPipeline.Models;
using RagPipeline.Services;

namespace RagPipeline.Services;

public class ChunkingOptions
{
    public int MaxTokens { get; set; } = 400;
    public int OverlapTokens { get; set; } = 60;
}

public record PendingChunk(int PageNumber, ChunkType Type, string Content, int TokenCount);

public class ChunkingService
{
    private readonly ChunkingOptions _options;

    public ChunkingService(ChunkingOptions? options = null)
    {
        _options = options ?? new ChunkingOptions();
    }

    public List<PendingChunk> Chunk(IEnumerable<ExtractedBlock> blocks)
    {
        var result = new List<PendingChunk>();

        foreach (var block in blocks)
        {
            if (block.IsTable)
            {
                // Tables stay intact as a single chunk (splitting rows loses column context).
                // If a table is huge, you'd split by row-groups instead - omitted here for clarity.
                result.Add(new PendingChunk(block.PageNumber, ChunkType.Table, block.Content, EstimateTokens(block.Content)));
                continue;
            }

            foreach (var chunkText in SplitTextWithOverlap(block.Content))
            {
                result.Add(new PendingChunk(block.PageNumber, ChunkType.Text, chunkText, EstimateTokens(chunkText)));
            }
        }

        return result;
    }

    private IEnumerable<string> SplitTextWithOverlap(string text)
    {
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) yield break;

        // Rough heuristic: ~0.75 tokens per word for English.
        int wordsPerChunk = (int)(_options.MaxTokens / 0.75);
        int overlapWords = (int)(_options.OverlapTokens / 0.75);

        int start = 0;
        while (start < words.Length)
        {
            int len = Math.Min(wordsPerChunk, words.Length - start);
            yield return string.Join(' ', words.Skip(start).Take(len));

            if (start + len >= words.Length) break;
            start += Math.Max(1, wordsPerChunk - overlapWords);
        }
    }

    private static int EstimateTokens(string text) => (int)(text.Split(' ').Length * 1.3);
}
