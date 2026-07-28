using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RagPipeline.Data;

namespace RagPipeline.Services;

public record RetrievedChunk(int ChunkId, int SourceDocumentId, string FileName, int PageNumber, string Content, double Distance);

public class VectorSearchService
{
    private readonly RagDbContext _db;

    public VectorSearchService(RagDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Cosine-distance nearest-neighbor search using SQL Server 2026's native
    /// VECTOR_DISTANCE function. EF Core's LINQ provider doesn't translate this
    /// yet, so we drop to raw SQL - still fully parameterized against injection.
    /// </summary>
    public async Task<List<RetrievedChunk>> SearchAsync(float[] queryEmbedding, int topK = 5, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP (@topK)
                c.Id            AS ChunkId,
                c.SourceDocumentId,
                d.FileName,
                c.PageNumber,
                c.Content,
                VECTOR_DISTANCE('cosine', c.Embedding, CAST(@query AS VECTOR(1536))) AS Distance
            FROM RagDocumentChunks c
            INNER JOIN RagSourceDocuments d ON d.Id = c.SourceDocumentId
            ORDER BY Distance ASC
            """;

        var queryParam = new SqlParameter("@query", SqlDbType.NVarChar)
        {
            // SQL Server 2026 accepts a JSON array literal cast to VECTOR(n)
            Value = System.Text.Json.JsonSerializer.Serialize(queryEmbedding)
        };
        var topKParam = new SqlParameter("@topK", SqlDbType.Int) { Value = topK };

        var results = new List<RetrievedChunk>();

        await using var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(queryParam);
        cmd.Parameters.Add(topKParam);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RetrievedChunk(
                ChunkId: reader.GetInt32(0),
                SourceDocumentId: reader.GetInt32(1),
                FileName: reader.GetString(2),
                PageNumber: reader.GetInt32(3),
                Content: reader.GetString(4),
                Distance: reader.GetDouble(5)));
        }

        return results;
    }
}
