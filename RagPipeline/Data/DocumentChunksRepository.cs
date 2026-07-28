using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RagPipeline.Interfaces;
using RagPipeline.Models;

namespace RagPipeline.Data;

public class DocumentChunksRepository : IDocumentChunksRepository
{
    private readonly RagDbContext _db;
    public DocumentChunksRepository(RagDbContext db) => _db = db;

    public async Task AddWithEmbeddingAsync(RagDocumentChunk chunk, float[] embedding, CancellationToken ct)
    {
        var vectorJson = JsonSerializer.Serialize(embedding);
        var conn = (SqlConnection)_db.Database.GetDbConnection();

        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(@"
            INSERT INTO RagDocumentChunks
                (SourceDocumentId, ChunkIndex, PageNumber, Type, Content, Embedding, TokenCount)
            OUTPUT INSERTED.Id
            VALUES
                (@SourceDocumentId, @ChunkIndex, @PageNumber, @Type, @Content, CAST(@Embedding AS VECTOR(1536)), @TokenCount)",
            conn);

        cmd.Parameters.AddWithValue("@SourceDocumentId", chunk.SourceDocumentId);
        cmd.Parameters.AddWithValue("@ChunkIndex", chunk.ChunkIndex);
        cmd.Parameters.AddWithValue("@PageNumber", chunk.PageNumber);
        cmd.Parameters.AddWithValue("@Type", (int)chunk.Type);
        cmd.Parameters.AddWithValue("@Content", chunk.Content);
        cmd.Parameters.AddWithValue("@Embedding", vectorJson);
        cmd.Parameters.AddWithValue("@TokenCount", chunk.TokenCount);

        chunk.Id = (int)await cmd.ExecuteScalarAsync(ct);
    }

    public Task<List<RagDocumentChunk>> GetBySourceDocumentIdAsync(int sourceDocumentId, CancellationToken ct) =>
        _db.DocumentChunks
            .Where(c => c.SourceDocumentId == sourceDocumentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);
}
