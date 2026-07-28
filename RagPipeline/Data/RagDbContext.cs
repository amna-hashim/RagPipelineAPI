using Microsoft.EntityFrameworkCore;
using RagPipeline.Models;

namespace RagPipeline.Data;

public class RagDbContext : DbContext
{
    // Keep this in sync with your embedding model's output size.
    public const int EmbeddingDimensions = 1536;

    public RagDbContext(DbContextOptions<RagDbContext> options) : base(options) { }

    public DbSet<RagSourceDocument> SourceDocuments => Set<RagSourceDocument>();
    public DbSet<RagDocumentChunk> DocumentChunks => Set<RagDocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        modelBuilder.Entity<RagSourceDocument>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(400).IsRequired();
            e.Property(x => x.Title).HasMaxLength(400);
        });

        modelBuilder.Entity<RagDocumentChunk>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).HasColumnType("nvarchar(max)").IsRequired();
            e.Ignore(m => m.Embedding); // handled outside EF
            e.HasOne(x => x.SourceDocument)
                .WithMany(x => x.Chunks)
                .HasForeignKey(x => x.SourceDocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.SourceDocumentId, x.ChunkIndex });
        });
    }
}
