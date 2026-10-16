using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.Impact;
using Kairo.Modules.Measurement.Lift;

namespace Kairo.Modules.Measurement.Data;

internal sealed class MeasurementDbContext(DbContextOptions<MeasurementDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "measurement";

    public DbSet<FeedRun> FeedRuns => Set<FeedRun>();

    public DbSet<SalesUpload> SalesUploads => Set<SalesUpload>();

    public DbSet<SalesSource> SalesSources => Set<SalesSource>();

    public DbSet<LiftReading> LiftReadings => Set<LiftReading>();

    public DbSet<ImpactReport> ImpactReports => Set<ImpactReport>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeedRun>(run =>
        {
            run.ToTable("FeedRuns");
            run.HasIndex(r => new { r.BrandId, r.Feed, r.StartedAt });
            run.HasIndex(r => r.StartedAt);
            run.Property(r => r.Feed).HasMaxLength(20);
            run.Property(r => r.ExternalId).HasMaxLength(300);
            run.Property(r => r.Status).HasMaxLength(20);
            run.Property(r => r.Reason).HasMaxLength(40);
            run.Property(r => r.Error).HasMaxLength(1000);
            run.Property(r => r.Tables).HasMaxLength(300);
        });

        modelBuilder.Entity<SalesUpload>(upload =>
        {
            upload.ToTable("SalesUploads");
            upload.HasIndex(u => new { u.BrandId, u.UploadedAt });
            upload.Property(u => u.FileName).HasMaxLength(200);
        });

        modelBuilder.Entity<SalesSource>(source =>
        {
            source.ToTable("SalesSources");
            source.HasKey(s => s.BrandId);
            source.Property(s => s.TableRef).HasMaxLength(300);
            source.Property(s => s.DateColumn).HasMaxLength(300);
            source.Property(s => s.DateColumnType).HasMaxLength(20).HasDefaultValue("DATE");
            source.Property(s => s.RegionColumn).HasMaxLength(300);
            source.Property(s => s.OrdersColumn).HasMaxLength(300);
            source.Property(s => s.RevenueColumn).HasMaxLength(300);
            source.Property(s => s.Location).HasMaxLength(64);
        });

        modelBuilder.Entity<LiftReading>(reading =>
        {
            reading.ToTable("LiftReadings");
            reading.HasIndex(r => new { r.BrandId, r.RoomId, r.Early, r.ThroughDate }).IsUnique();
            reading.Property(r => r.Kpi).HasMaxLength(20);
            reading.Property(r => r.Unit).HasMaxLength(20);
            reading.Property(r => r.Status).HasMaxLength(20);
            reading.Property(r => r.Reason).HasMaxLength(40);
            reading.Property(r => r.Method).HasMaxLength(40);
        });

        modelBuilder.Entity<ImpactReport>(report =>
        {
            report.ToTable("ImpactReports");
            report.HasIndex(r => new { r.BrandId, r.RoomId }).IsUnique();
            report.HasIndex(r => new { r.BrandId, r.Status, r.ReadyAt });
            report.Property(r => r.Status).HasMaxLength(20);
            report.Property(r => r.Problem).HasMaxLength(1000);
            report.Property(r => r.Title).HasMaxLength(200);
            report.Property(r => r.Kpi).HasMaxLength(20);
            report.Property(r => r.Currency).HasMaxLength(3);
            report.Property(r => r.Spend).HasPrecision(19, 4);
            report.Property(r => r.Results).HasPrecision(19, 4);
            report.Property(r => r.CostPerResult).HasPrecision(19, 4);
            report.Property(r => r.CostPerExtra).HasPrecision(19, 4);
            report.Property(r => r.CostPerExtraLow).HasPrecision(19, 4);
            report.Property(r => r.CostPerExtraHigh).HasPrecision(19, 4);
            report.Property(r => r.TargetCost).HasPrecision(19, 4);
            report.Property(r => r.Outcome).HasMaxLength(20);
            report.Property(r => r.NextStep).HasMaxLength(20);
            report.Property(r => r.NextTest).HasMaxLength(1000);
            report.Property(r => r.Summary).HasMaxLength(1200);
            report.Property(r => r.NextStepReasoning).HasMaxLength(1000);
            report.Property(r => r.InconclusiveReason).HasMaxLength(800);
            report.Property(r => r.NextTestAdvice).HasMaxLength(800);
            report.Property(r => r.Model).HasMaxLength(80);
            report.Property(r => r.PromptVersion).HasMaxLength(80);
            report.Property(r => r.Version).IsConcurrencyToken();
        });
    }
}
