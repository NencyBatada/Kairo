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
    public int Version { get; set; } = 1;
    public int RetryCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? PendingBuild { get; set; }
}

internal sealed class LaunchStep : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid LaunchId { get; set; }
    public string Name { get; set; } = "";
    public int Attempt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Outcome { get; set; } = "attempting";
}

internal sealed class LaunchHealth : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid LaunchId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public DateTimeOffset? ClaimedUntil { get; set; }
    public Guid? ClaimToken { get; set; }
    public int Attempts { get; set; }
    public string Status { get; set; } = "pending";
    public string Alerts { get; set; } = "[]";
    public string? Cause { get; set; }
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

internal sealed class AdPause : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid RoomId { get; set; }
    public Guid LaunchId { get; set; }
    public string CampaignType { get; set; } = "";
    public string CampaignResource { get; set; } = "";
    public string AdResource { get; set; } = "";
    public string ProductIds { get; set; } = "[]";
    public string State { get; set; } = "paused";
    public Guid? BatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public DateTimeOffset? ResumedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public int Version { get; set; } = 1;
}

internal sealed class StockWatch : IBrandOwned
{
    public Guid BrandId { get; set; }
    public DateTimeOffset NextCheckAt { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public DateTimeOffset? ClaimedUntil { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? FeedUpdatedAt { get; set; }
    public DateTimeOffset? StaleSince { get; set; }
}

internal sealed class LaunchDbContext(DbContextOptions<LaunchDbContext> options, KairoDatabase database) : ModuleDbContext(options, database)
{
    public const string SchemaName = "launch";
    public DbSet<LaunchRecord> Launches => Set<LaunchRecord>();
    public DbSet<ProductAdMap> ProductAds => Set<ProductAdMap>();
    public DbSet<LaunchStep> Steps => Set<LaunchStep>();
    public DbSet<LaunchHealth> Health => Set<LaunchHealth>();
    public DbSet<AdPause> AdPauses => Set<AdPause>();
    public DbSet<StockWatch> StockWatches => Set<StockWatch>();
    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LaunchRecord>(launch =>
        {
            launch.HasKey(row => row.Id);
            launch.HasIndex(row => new { row.BrandId, row.RoomId }).IsUnique().HasFilter("[Status] <> N'superseded'");
            launch.HasIndex(row => new { row.Status, row.ClaimedUntil });
            launch.Property(row => row.Hash).HasMaxLength(64);
            launch.Property(row => row.ClaimToken).IsConcurrencyToken();
            launch.Property(row => row.Status).HasMaxLength(30);
            launch.Property(row => row.Step).HasMaxLength(100);
            launch.Property(row => row.AttemptingStep).HasMaxLength(100);
            launch.Property(row => row.ErrorCode).HasMaxLength(60);
            launch.Property(row => row.ErrorMessage).HasMaxLength(500);
            launch.Property(row => row.SuggestedFix).HasMaxLength(500);
        });
        modelBuilder.Entity<LaunchStep>(step =>
        {
            step.HasKey(row => row.Id);
            step.HasIndex(row => new { row.BrandId, row.LaunchId, row.Name, row.Attempt }).IsUnique();
            step.Property(row => row.Name).HasMaxLength(100);
            step.Property(row => row.Outcome).HasMaxLength(30);
        });
        modelBuilder.Entity<LaunchHealth>(health =>
        {
            health.HasKey(row => row.Id);
            health.HasIndex(row => row.LaunchId).IsUnique();
            health.HasIndex(row => new { row.Status, row.DueAt });
            health.Property(row => row.Status).HasMaxLength(30);
            health.Property(row => row.Cause).HasMaxLength(500);
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
        modelBuilder.Entity<AdPause>(pause =>
        {
            pause.HasKey(row => row.Id);
            pause.HasIndex(row => new { row.BrandId, row.RoomId, row.CreatedAt });
            pause.HasIndex(row => new { row.BrandId, row.AdResource }).IsUnique().HasFilter("[State] IN (N'held', N'paused')");
            pause.HasIndex(row => new { row.BrandId, row.BatchId });
            pause.Property(row => row.CampaignType).HasMaxLength(20);
            pause.Property(row => row.CampaignResource).HasMaxLength(200);
            pause.Property(row => row.AdResource).HasMaxLength(200);
            pause.Property(row => row.State).HasMaxLength(20);
        });
        modelBuilder.Entity<StockWatch>(watch =>
        {
            watch.HasKey(row => row.BrandId);
            watch.HasIndex(row => row.NextCheckAt);
            watch.Property(row => row.ClaimToken).IsConcurrencyToken();
        });
    }
}

internal sealed class LaunchDbContextFactory : IDesignTimeDbContextFactory<LaunchDbContext>
{
    public LaunchDbContext CreateDbContext(string[] args) => KairoDbContextOptions.CreateForDesignTime<LaunchDbContext>(
        LaunchDbContext.SchemaName, (options, database) => new LaunchDbContext(options, database));
}
