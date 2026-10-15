using Kairo.Modules.Triggers.Checks;
using Kairo.Modules.Triggers.Festivals;
using Kairo.Modules.Triggers.Rules;
using Kairo.Modules.Triggers.Weather;

namespace Kairo.Modules.Triggers.Data;

internal sealed class TriggersDbContext(DbContextOptions<TriggersDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "triggers";

    public DbSet<TriggerRule> Rules => Set<TriggerRule>();

    public DbSet<TriggerFiring> Firings => Set<TriggerFiring>();

    public DbSet<FestivalEvent> Festivals => Set<FestivalEvent>();

    public DbSet<BrandTriggerState> BrandStates => Set<BrandTriggerState>();

    public DbSet<WeatherForecast> Forecasts => Set<WeatherForecast>();

    public Guid BrandId => CurrentBrandId ?? throw new InvalidOperationException("No brand is set for this request.");

    public IQueryable<TriggerRule> BrandRules => Rules.Where(rule => rule.BrandId == BrandId && rule.Status != RuleStatuses.Deleted);

    public IQueryable<TriggerFiring> BrandFirings => Firings.Where(firing => firing.BrandId == BrandId);

    public IQueryable<FestivalEvent> BrandFestivals => Festivals.Where(festival => festival.BrandId == BrandId);

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TriggerRule>(rule =>
        {
            rule.ToTable("TriggerRules");
            rule.HasKey(r => r.Id).IsClustered(false);
            rule.HasIndex(r => new { r.BrandId, r.CreatedAt }).IsClustered();
            rule.HasIndex(r => new { r.BrandId, r.Status });
            rule.Property(r => r.Name).HasMaxLength(RuleLimits.MaxNameLength);
            rule.Property(r => r.Source).HasMaxLength(20);
            rule.Property(r => r.Category).HasMaxLength(RuleLimits.MaxCategoryLength);
            rule.Property(r => r.Status).HasMaxLength(20);
            rule.Property(r => r.Origin).HasMaxLength(20);
            rule.Property(r => r.Evidence).HasMaxLength(RuleLimits.MaxEvidenceLength);
            rule.Property(r => r.LastCheckOutcome).HasMaxLength(30);
            rule.OwnsOne(r => r.Condition, condition =>
            {
                condition.ToJson();
                condition.Property(c => c.Kind).HasMaxLength(20);
            });
            rule.Navigation(r => r.Condition).IsRequired();
        });

        modelBuilder.Entity<TriggerFiring>(firing =>
        {
            firing.ToTable("TriggerFirings");
            firing.HasKey(f => f.Id).IsClustered(false);
            firing.HasIndex(f => new { f.BrandId, f.FiredAt }).IsClustered();
            firing.HasIndex(f => new { f.BrandId, f.RuleId, f.FiredAt });
            firing.Property(f => f.Summary).HasMaxLength(500);
            firing.Property(f => f.Outcome).HasMaxLength(20);
        });

        modelBuilder.Entity<FestivalEvent>(festival =>
        {
            festival.ToTable("FestivalEvents");
            festival.HasKey(f => f.Id).IsClustered(false);
            festival.HasIndex(f => new { f.BrandId, f.StartsOn }).IsClustered();
            festival.Property(f => f.Name).HasMaxLength(80);
            festival.Property(f => f.Origin).HasMaxLength(10);
        });

        modelBuilder.Entity<BrandTriggerState>(state =>
        {
            state.ToTable("BrandStates");
            state.HasKey(s => s.BrandId);
            state.Property(s => s.BrandId).ValueGeneratedNever();
        });

        modelBuilder.Entity<WeatherForecast>(forecast =>
        {
            forecast.ToTable("WeatherForecasts");
            forecast.HasKey(f => f.Id).IsClustered(false);
            forecast.HasIndex(f => new { f.RegionId, f.Hour }).IsUnique().IsClustered();
            forecast.Property(f => f.RegionId).HasMaxLength(60);
            forecast.OwnsMany(f => f.Days, days => days.ToJson());
        });
    }
}
