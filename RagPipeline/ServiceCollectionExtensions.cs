using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RagPipeline.Abstractions;
using RagPipeline.Data;
using RagPipeline.Interfaces;
using RagPipeline.Services;

namespace RagPipeline;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything RAG needs. Call once from the chatbot host's Program.cs / Startup.
    /// If the chatbot already has its own DbContext against the same database, you can skip
    /// AddDbContext here and instead add SourceDocuments/DocumentChunks to that existing context -
    /// see RagDbContext.OnModelCreating for the config to port over.
    /// </summary>
    public static IServiceCollection AddRagPipeline(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("RagDb")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:RagDb");

        var ghEndpoint = config["GitHubModels:Endpoint"];
        var ghToken = config["GH_PAT"];
        var ghEmbeddingModel = config["GitHubModels:EmbeddingModel"] ?? "text-embedding-3-small";
        var ghChatModel = config["GitHubModels:ChatModel"] ?? "openai/gpt-4o-mini";

        var openAiKey = config["OpenAI:ApiKey"];

        // Prefer GitHub Models if configured, else fall back to a direct OpenAI key.
        var apiKey = ghToken ?? openAiKey
            ?? throw new InvalidOperationException("Configure either GitHubModels:Token or OpenAI:ApiKey");
        var endpoint = ghToken is not null ? ghEndpoint : null;

        services.AddDbContext<RagDbContext>(opt => opt.UseSqlServer(connectionString));

        services.AddScoped<ISourceDocumentRepository, SourceDocumentRepository>();
        services.AddScoped<IDocumentChunksRepository, DocumentChunksRepository>();

        services.AddSingleton(new EmbeddingService(apiKey, ghEmbeddingModel, endpoint));
        services.AddScoped<PdfExtractionService>();
        services.AddScoped<ChunkingService>();
        services.AddScoped<IngestionService>();
        services.AddScoped<VectorSearchService>();
        services.AddScoped(sp => new RagAnswerService(
            sp.GetRequiredService<EmbeddingService>(),
            sp.GetRequiredService<VectorSearchService>(),
            apiKey,
            ghChatModel,
            endpoint));

        services.AddScoped<IKnowledgeRetriever, KnowledgeRetriever>();
        services.AddScoped<IKnowledgeIngestor, KnowledgeIngestor>();

        return services;
    }
}
