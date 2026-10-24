using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Operations.Data;

internal sealed class Invocation : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? RunId { get; set; }
    public string Operation { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Closed { get; set; }
}

internal sealed class ProviderCall : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid InvocationId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? RunId { get; set; }
    public Guid RequestId { get; set; }
    public string Model { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Region { get; set; } = "";
    public string Reservation { get; set; } = "";
    public int TextCalls { get; set; }
    public int Images { get; set; }
    public int VideoClips { get; set; }
    public string Outcome { get; set; } = "uncertain";
    public string? Report { get; set; }
    public string? ResolvedModel { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? ThoughtTokens { get; set; }
    public long? CachedTokens { get; set; }
    public int? GeneratedImages { get; set; }
    public decimal? VideoSeconds { get; set; }
    public long? SpeechCharacters { get; set; }
    public decimal? SpeechSeconds { get; set; }
    public decimal? EstimatedUsd { get; set; }
    public string? RateVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReportedAt { get; set; }
}

internal sealed class OperationsDbContext(DbContextOptions<OperationsDbContext> options, KairoDatabase database) : ModuleDbContext(options, database)
{
    public const string SchemaName = "operations";
    protected override string Schema => SchemaName;
    public DbSet<Invocation> Invocations => Set<Invocation>();
    public DbSet<ProviderCall> Calls => Set<ProviderCall>();

    protected override void ConfigureModel(ModelBuilder builder)
    {
        builder.Entity<Invocation>(row =>
        {
            row.HasKey(value => value.Id);
            row.Property(value => value.Operation).HasMaxLength(100);
            row.Property(value => value.TokenHash).HasMaxLength(64);
        });
        builder.Entity<ProviderCall>(row =>
        {
            row.ToTable("ProviderCalls");
            row.HasKey(value => value.Id);
            row.HasIndex(value => new { value.BrandId, value.InvocationId, value.RequestId }).IsUnique();
            row.HasIndex(value => new { value.BrandId, value.CampaignId });
            row.Property(value => value.Model).HasMaxLength(100);
            row.Property(value => value.ResolvedModel).HasMaxLength(100);
            row.Property(value => value.Operation).HasMaxLength(40);
            row.Property(value => value.Region).HasMaxLength(40);
            row.Property(value => value.Outcome).HasMaxLength(20);
            row.Property(value => value.RateVersion).HasMaxLength(100);
            row.Property(value => value.VideoSeconds).HasPrecision(18, 4);
            row.Property(value => value.SpeechSeconds).HasPrecision(18, 4);
            row.Property(value => value.EstimatedUsd).HasPrecision(28, 12);
        });
    }
}

internal sealed class OperationsDbContextFactory : IDesignTimeDbContextFactory<OperationsDbContext>
{
    public OperationsDbContext CreateDbContext(string[] args) => KairoDbContextOptions.CreateForDesignTime<OperationsDbContext>(
        OperationsDbContext.SchemaName, (options, database) => new OperationsDbContext(options, database));
}
