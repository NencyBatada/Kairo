using Kairo.Modules.Opportunities.Radar;
using Kairo.Modules.Opportunities.Refresh;
using Kairo.Modules.Opportunities.Topics;

namespace Kairo.Modules.Opportunities.Data;

internal sealed class OpportunitiesDbContext(DbContextOptions<OpportunitiesDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "opportunities";

    public DbSet<Opportunity> Opportunities => Set<Opportunity>();

    public DbSet<WatchTopic> WatchTopics => Set<WatchTopic>();

    public DbSet<RefreshRun> RefreshRuns => Set<RefreshRun>();

    public Guid BrandId => CurrentBrandId ?? throw new InvalidOperationException("No brand is set for this request.");

    public IQueryable<Opportunity> BrandOpportunities => Opportunities.Where(opportunity => opportunity.BrandId == BrandId);

    public IQueryable<WatchTopic> BrandTopics => WatchTopics.Where(topic => topic.BrandId == BrandId);

    public IQueryable<RefreshRun> BrandRuns => RefreshRuns.Where(run => run.BrandId == BrandId);

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Opportunity>(opportunity =>
        {
            opportunity.ToTable("Opportunities");
            opportunity.HasKey(o => o.Id).IsClustered(false);
            opportunity.HasIndex(o => new { o.BrandId, o.FirstSeenAt }).IsClustered();
            opportunity.HasIndex(o => new { o.BrandId, o.Status, o.Score });
            opportunity.Property(o => o.Topic).HasMaxLength(200);
            opportunity.Property(o => o.TopicKey).HasMaxLength(200);
            opportunity.Property(o => o.Status).HasMaxLength(20);
            opportunity.Property(o => o.Source).HasMaxLength(20);
            opportunity.Property(o => o.FitReason).HasMaxLength(500);
            opportunity.Property(o => o.ReasonsStatus).HasMaxLength(20);
            opportunity.Property(o => o.Model).HasMaxLength(100);
            opportunity.Property(o => o.PromptVersion).HasMaxLength(100);
            opportunity.Property(o => o.DismissReason).HasMaxLength(20);
            opportunity.Property(o => o.DismissNote).HasMaxLength(500);
            opportunity.OwnsOne(o => o.Breakdown, breakdown => breakdown.ToJson());
            opportunity.Navigation(o => o.Breakdown).IsRequired();
            opportunity.OwnsMany(o => o.Trend, trend => trend.ToJson());
            opportunity.OwnsMany(o => o.Regions, regions => regions.ToJson());
            opportunity.OwnsOne(o => o.Bids, bids => bids.ToJson());
            opportunity.OwnsMany(o => o.Reasons, reasons =>
            {
                reasons.ToJson();
                reasons.OwnsMany(r => r.Sources);
            });
            opportunity.OwnsMany(o => o.Triggers, triggers =>
            {
                triggers.ToJson();
                triggers.OwnsMany(t => t.Regions);
            });
            opportunity.OwnsOne(o => o.Brief, brief => brief.ToJson());
            opportunity.Property(o => o.BriefStatus).HasMaxLength(20);
        });

        modelBuilder.Entity<WatchTopic>(topic =>
        {
            topic.ToTable("WatchTopics");
            topic.HasKey(t => t.Id).IsClustered(false);
            topic.HasIndex(t => new { t.BrandId, t.Phrase }).IsUnique().IsClustered();
            topic.Property(t => t.Phrase).HasMaxLength(100);
            topic.Property(t => t.Kind).HasMaxLength(10);
        });

        modelBuilder.Entity<RefreshRun>(run =>
        {
            run.ToTable("RefreshRuns");
            run.HasKey(r => r.Id).IsClustered(false);
            run.HasIndex(r => new { r.BrandId, r.StartedAt }).IsClustered();
            run.Property(r => r.Outcome).HasMaxLength(20);
            run.Property(r => r.Reason).HasMaxLength(50);
            run.Property(r => r.Error).HasMaxLength(1000);
        });
    }
}
