using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Approvals.Data;

internal sealed class ApprovalEntry : IBrandOwned
{
    public long Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid RoomId { get; set; }
    public string Gate { get; set; } = "";
    public Guid ApproverId { get; set; }
    public DateTimeOffset ApprovedAt { get; set; }
    public string Hash { get; set; } = "";
    public string Snapshot { get; set; } = "{}";
    public bool Invalidated { get; set; }
}

internal sealed class ApprovalsDbContext(DbContextOptions<ApprovalsDbContext> options, KairoDatabase database) : ModuleDbContext(options, database)
{
    public const string SchemaName = "approvals";
    public DbSet<ApprovalEntry> Entries => Set<ApprovalEntry>();
    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApprovalEntry>(entry =>
        {
            entry.ToTable("Entries", table => table.HasTrigger("Entries_PreventChanges"));
            entry.HasKey(row => row.Id);
            entry.HasIndex(row => new { row.BrandId, row.RoomId, row.Gate, row.Id });
            entry.Property(row => row.Gate).HasMaxLength(20);
            entry.Property(row => row.Hash).HasMaxLength(64);
        });
    }
}

internal sealed class ApprovalsDbContextFactory : IDesignTimeDbContextFactory<ApprovalsDbContext>
{
    public ApprovalsDbContext CreateDbContext(string[] args) => KairoDbContextOptions.CreateForDesignTime<ApprovalsDbContext>(
        ApprovalsDbContext.SchemaName, (options, database) => new ApprovalsDbContext(options, database));
}
