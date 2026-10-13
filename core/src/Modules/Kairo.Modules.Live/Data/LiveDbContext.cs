namespace Kairo.Modules.Live.Data;

internal sealed class LiveDbContext(DbContextOptions<LiveDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "live";

    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();

    protected override string Schema => SchemaName;

    public IQueryable<OutboxEvent> Unlocked() =>
        Outbox.FromSql($"SELECT * FROM [live].[Outbox] WITH (READCOMMITTEDLOCK, READPAST)").AsNoTracking();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxEvent>(row =>
        {
            row.ToTable("Outbox");
            row.HasIndex(e => new { e.BrandId, e.Id });
            row.HasIndex(e => e.CreatedAt);
            row.Property(e => e.Type).HasMaxLength(100);
            row.Property(e => e.AudienceKind).HasConversion<string>().HasMaxLength(20);
        });
    }
}
