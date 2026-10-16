using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.BrandMemory.Data;

internal sealed class MemoryEntry : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Kind { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Title { get; set; } = "";
    public string Lesson { get; set; } = "";
    public string Evidence { get; set; } = "{}";
    public List<string> Products { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public List<string> Regions { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public Guid? SourceRoomId { get; set; }
    public Guid? SourceReportId { get; set; }
    public string State { get; set; } = MemoryStates.Active;
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? EditedBy { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public Guid? RemovedBy { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public int Version { get; set; } = 1;
}

internal sealed class MemoryUse : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid EntryId { get; set; }
    public string UsedIn { get; set; } = "";
    public Guid TargetId { get; set; }
    public Guid? RoomId { get; set; }
    public DateTimeOffset UsedAt { get; set; }
}

internal sealed class LearnRun : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid RoomId { get; set; }
    public Guid? ReportId { get; set; }
    public string Outcome { get; set; } = "";
    public int Entries { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal static class MemoryStates
{
    public const string Active = "active";
    public const string Removed = "removed";

    public static bool IsKnown(string? state) => state is Active or Removed;
}

internal static class LearnOutcomes
{
    public const string Saved = "saved";
    public const string SkippedInconclusive = "skipped_inconclusive";
    public const string Failed = "failed";
}

internal sealed class BrandMemoryDbContext(DbContextOptions<BrandMemoryDbContext> options, KairoDatabase database) : ModuleDbContext(options, database)
{
    public const string SchemaName = "brand_memory";
    public DbSet<MemoryEntry> Entries => Set<MemoryEntry>();
    public DbSet<MemoryUse> Uses => Set<MemoryUse>();
    public DbSet<LearnRun> LearnRuns => Set<LearnRun>();
    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryEntry>(entry =>
        {
            entry.ToTable("MemoryEntries");
            entry.HasKey(row => row.Id);
            entry.HasIndex(row => new { row.BrandId, row.State, row.CreatedAt });
            entry.HasIndex(row => new { row.BrandId, row.SourceRoomId });
            entry.Property(row => row.Kind).HasMaxLength(20);
            entry.Property(row => row.Outcome).HasMaxLength(20);
            entry.Property(row => row.Title).HasMaxLength(120);
            entry.Property(row => row.Lesson).HasMaxLength(500);
            entry.Property(row => row.State).HasMaxLength(20);
            entry.Property(row => row.CreatedBy).HasMaxLength(40);
            entry.Property(row => row.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<MemoryUse>(use =>
        {
            use.ToTable("MemoryUses");
            use.HasKey(row => row.Id);
            use.HasIndex(row => new { row.BrandId, row.EntryId, row.UsedIn, row.TargetId }).IsUnique();
            use.HasIndex(row => new { row.BrandId, row.RoomId, row.UsedIn });
            use.Property(row => row.UsedIn).HasMaxLength(20);
        });
        modelBuilder.Entity<LearnRun>(run =>
        {
            run.ToTable("LearnRuns");
            run.HasKey(row => row.Id);
            run.HasIndex(row => new { row.BrandId, row.RoomId }).IsUnique();
            run.Property(row => row.Outcome).HasMaxLength(30);
            run.Property(row => row.Error).HasMaxLength(1000);
            run.Property(row => row.Model).HasMaxLength(100);
            run.Property(row => row.PromptVersion).HasMaxLength(100);
        });
    }
}

internal sealed class BrandMemoryDbContextFactory : IDesignTimeDbContextFactory<BrandMemoryDbContext>
{
    public BrandMemoryDbContext CreateDbContext(string[] args) => KairoDbContextOptions.CreateForDesignTime<BrandMemoryDbContext>(
        BrandMemoryDbContext.SchemaName, (options, database) => new BrandMemoryDbContext(options, database));
}
