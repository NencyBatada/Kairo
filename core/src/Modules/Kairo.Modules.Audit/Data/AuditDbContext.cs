namespace Kairo.Modules.Audit.Data;

internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "audit";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.ToTable("Entries", table => table.HasTrigger("Entries_PreventChanges"));
            entry.HasIndex(e => new { e.BrandId, e.Id });
            entry.Property(e => e.ActorType).HasMaxLength(20);
            entry.Property(e => e.ActorId).HasMaxLength(64);
            entry.Property(e => e.ActorName).HasMaxLength(200);
            entry.Property(e => e.ActorEmail).HasMaxLength(320);
            entry.Property(e => e.Action).HasMaxLength(100);
            entry.Property(e => e.TargetType).HasMaxLength(50);
            entry.Property(e => e.TargetId).HasMaxLength(64);
            entry.Property(e => e.TraceId).HasMaxLength(32);
        });
    }
}
