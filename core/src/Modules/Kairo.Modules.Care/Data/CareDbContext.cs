using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Care.Data;

internal sealed class Recommendation : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid RoomId { get; set; }
    public string Source { get; set; } = "";
    public string Type { get; set; } = "";
    public string CampaignType { get; set; } = "";
    public string State { get; set; } = "";
    public string Title { get; set; } = "";
    public string ExpectedEffect { get; set; } = "";
    public string Reasoning { get; set; } = "";
    public string Change { get; set; } = "{}";
    public string Baseline { get; set; } = "[]";
    public string Evidence { get; set; } = "{}";
    public string? GoogleResource { get; set; }
    public string? Problem { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DismissReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? NotifiedAt { get; set; }
    public int Version { get; set; } = 1;
}

internal sealed class RecommendationRun : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid RoomId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Drafted { get; set; }
    public DateTimeOffset DraftedAt { get; set; }
}

internal sealed class CareDbContext(DbContextOptions<CareDbContext> options, KairoDatabase database) : ModuleDbContext(options, database)
{
    public const string SchemaName = "care";
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<RecommendationRun> Runs => Set<RecommendationRun>();
    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Recommendation>(recommendation =>
        {
            recommendation.HasKey(row => row.Id);
            recommendation.HasIndex(row => new { row.BrandId, row.RoomId, row.CreatedAt });
            recommendation.HasIndex(row => new { row.BrandId, row.GoogleResource }).IsUnique().HasFilter("[GoogleResource] IS NOT NULL");
            recommendation.HasIndex(row => new { row.BrandId, row.State });
            recommendation.Property(row => row.Source).HasMaxLength(20);
            recommendation.Property(row => row.Type).HasMaxLength(80);
            recommendation.Property(row => row.CampaignType).HasMaxLength(20);
            recommendation.Property(row => row.State).HasMaxLength(20);
            recommendation.Property(row => row.Title).HasMaxLength(200);
            recommendation.Property(row => row.ExpectedEffect).HasMaxLength(500);
            recommendation.Property(row => row.Reasoning).HasMaxLength(1000);
            recommendation.Property(row => row.GoogleResource).HasMaxLength(300);
            recommendation.Property(row => row.Problem).HasMaxLength(1000);
            recommendation.Property(row => row.DismissReason).HasMaxLength(500);
            recommendation.Property(row => row.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<RecommendationRun>(run =>
        {
            run.ToTable("RecommendationRuns");
            run.HasKey(row => row.Id);
            run.HasIndex(row => new { row.BrandId, row.RoomId }).IsUnique();
        });
    }
}

internal sealed class CareDbContextFactory : IDesignTimeDbContextFactory<CareDbContext>
{
    public CareDbContext CreateDbContext(string[] args) => KairoDbContextOptions.CreateForDesignTime<CareDbContext>(
        CareDbContext.SchemaName, (options, database) => new CareDbContext(options, database));
}
