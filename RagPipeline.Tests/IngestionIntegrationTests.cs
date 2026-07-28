using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RagPipeline.Data;
using RagPipeline.Services;
using Xunit;

namespace RagPipeline.Tests;

/// <summary>
/// Full pipeline test against a real SQL Server 2026 instance and real embedding calls.
/// Requires appsettings.test.json (or env vars) with ConnectionStrings:RagDb and
/// GitHubModels:Token filled in. Skips cleanly if not configured, so it won't
/// break `dotnet test` in CI without secrets.
/// </summary>
public class IngestionIntegrationTests : IAsyncLifetime
{
    private IConfigurationRoot _config = default!;
    private RagDbContext _db = default!;

    public async Task InitializeAsync()
    {
        try
        {
            _config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.test.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

            var userSecretsConfig = new ConfigurationBuilder()
                        .AddUserSecrets<IngestionIntegrationTests>()
                        .Build();

            var connStr = userSecretsConfig["ConnectionStrings:RagDb"] ?? throw new InvalidOperationException("ConnectionStrings:RagDb not found in user secrets.");

            //var connStr = _config.GetConnectionString("RagDb");
            if (string.IsNullOrWhiteSpace(connStr)) await Task.CompletedTask;

            var options = new DbContextOptionsBuilder<RagDbContext>()
                .UseSqlServer(connStr)
                .Options;

            _db = new RagDbContext(options);
            // Tests don't require applying EF migrations; skip migration to avoid provider/model issues.
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString()); // or use test output logger
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        //if (_db is null) return;

        //// Clean up whatever this test run inserted so re-runs stay deterministic.
        //await _db.Database.ExecuteSqlRawAsync(
        //    "DELETE FROM DocumentChunks; DELETE FROM SourceDocuments;");
        //await _db.DisposeAsync();
    }

    [Fact]
    public async Task IngestThenAsk_ReturnsAnswerGroundedInTheDocument()
    {
        var userSecretsConfig = new ConfigurationBuilder()
                    .AddUserSecrets<IngestionIntegrationTests>()
                    .Build();

        var connStr = userSecretsConfig["ConnectionStrings:RagDb"] ?? throw new InvalidOperationException("ConnectionStrings:RagDb not found in user secrets.");

        var ghToken = userSecretsConfig["GH_PAT"]
        ?? throw new InvalidOperationException("GH_PAT not found in user secrets.");

        const string samplePdf = "C:\\Users\\Shoukri-EVO2\\Downloads\\IPCC_AR6_WGII_Chapter03.pdf";

        if (string.IsNullOrWhiteSpace(connStr) || string.IsNullOrWhiteSpace(ghToken) || !File.Exists(samplePdf))
        {
            return; // not configured for integration run - see class doc comment
        }

        var ghEndpoint = _config["GitHubModels:Endpoint"] ?? "";
        var embeddingModel = _config["GitHubModels:EmbeddingModel"] ?? "";
        var chatModel = _config["GitHubModels:ChatModel"] ?? "";

        var embedder = new EmbeddingService(ghToken, embeddingModel, ghEndpoint);
        var extractor = new PdfExtractionService();
        var chunker = new ChunkingService();
        var sourceDocuments = new SourceDocumentRepository(_db);
        var documentChunks = new DocumentChunksRepository(_db);
        var ingestion = new IngestionService(extractor, chunker, embedder, sourceDocuments, documentChunks);

        // --- Act: ingest ---
        var doc = await ingestion.IngestAsync(samplePdf);

        // --- Assert: ingestion actually persisted rows ---
        Assert.True(doc.Chunks.Count > 0, "Expected at least one chunk to be created and saved");
        var storedCount = await _db.DocumentChunks.CountAsync(c => c.SourceDocumentId == doc.Id);
        Assert.Equal(doc.Chunks.Count, storedCount);

        // --- Act: query ---
        var search = new VectorSearchService(_db);
        var rag = new RagAnswerService(embedder, search, ghToken, chatModel, ghEndpoint);

        // Replace this with a question you know the sample PDF actually answers.
        var result = await rag.AskAsync("What is this document about?");

        // --- Assert: retrieval found something from the doc we just ingested ---
        Assert.NotEmpty(result.Sources);
        Assert.Contains(result.Sources, s => s.FileName == doc.FileName);
        Assert.False(string.IsNullOrWhiteSpace(result.Answer));
    }
}
