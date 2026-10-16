using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Launch.Data;

internal sealed class LaunchRecord : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid RoomId { get; set; }
    public string Plan { get; set; } = "{}";
    public string Hash { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string Step { get; set; } = "queued";
    public string? AttemptingStep { get; set; }
    public string Resources { get; set; } = "{}";
    public string Uploads { get; set; } = "{}";
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? ClaimedUntil { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuggestedFix { get; set; }
    public bool Uncertain { get; set; }
}

internal sealed class ProductAdMap : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid RoomId { get; set; }
    public string ProductId { get; set; } = "";
    public string CampaignType { get; set; } = "";
    public string CampaignResource { get; set; } = "";
    public string AdResource { get; set; } = "";
}

internal sealed class LaunchDbContext(DbContextOptions<LaunchDbContext> options, KairoDatabase database) : ModuleDbContext(options, database)
{
    public const string SchemaName = "launch";
    public DbSet<LaunchRecord> Launches => Set<LaunchRecord>();
    public DbSet<ProductAdMap> ProductAds => Set<ProductAdMap>();
    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LaunchRecord>(launch =>
        {
            launch.HasKey(row => row.Id);
            launch.HasIndex(row => new { row.BrandId, row.RoomId }).IsUnique();
            launch.HasIndex(row => new { row.Status, row.ClaimedUntil });
            launch.Property(row => row.Hash).HasMaxLength(64);
            launch.Property(row => row.Status).HasMaxLength(30);
            launch.Property(row => row.Step).HasMaxLength(100);
            launch.Property(row => row.AttemptingStep).HasMaxLength(100);
            launch.Property(row => row.ErrorCode).HasMaxLength(60);
            launch.Property(row => row.ErrorMessage).HasMaxLength(500);
            launch.Property(row => row.SuggestedFix).HasMaxLength(500);
        });
        modelBuilder.Entity<ProductAdMap>(map =>
        {
            map.HasKey(row => row.Id);
            map.HasIndex(row => new { row.BrandId, row.RoomId, row.ProductId, row.AdResource }).IsUnique();
            map.Property(row => row.ProductId).HasMaxLength(200);
            map.Property(row => row.CampaignType).HasMaxLength(20);
            map.Property(row => row.CampaignResource).HasMaxLength(200);
            map.Property(row => row.AdResource).HasMaxLength(200);
        });
    }
}

internal sealed class LaunchDbContextFactory : IDesignTimeDbContextFactory<LaunchDbContext>
{
    public LaunchDbContext CreateDbContext(string[] args) => KairoDbContextOptions.CreateForDesignTime<LaunchDbContext>(
        LaunchDbContext.SchemaName, (options, database) => new LaunchDbContext(options, database));
}
