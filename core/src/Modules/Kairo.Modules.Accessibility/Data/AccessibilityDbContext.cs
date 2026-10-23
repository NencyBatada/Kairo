using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Accessibility.Data;

internal sealed class AccessibilityDbContext(DbContextOptions<AccessibilityDbContext> options, KairoDatabase database) : ModuleDbContext(options, database)
{
    public const string SchemaName = "accessibility";
    protected override string Schema => SchemaName;
    public DbSet<MediaAlternative> Alternatives => Set<MediaAlternative>();
    protected override void ConfigureModel(ModelBuilder builder)
    {
        builder.Entity<MediaAlternative>(row =>
        {
            row.ToTable("MediaAlternatives");
            row.HasKey(value => value.Id);
            row.HasIndex(value => new { value.BrandId, value.TargetKey }).IsUnique();
            row.Property(value => value.TargetKey).HasMaxLength(64);
            row.Property(value => value.SourceHash).HasMaxLength(64);
            row.Property(value => value.DescribedFileKey).HasMaxLength(500);
            row.Property(value => value.Revision).IsConcurrencyToken();
        });
    }
}

internal sealed class AccessibilityDbContextFactory : IDesignTimeDbContextFactory<AccessibilityDbContext>
{
    public AccessibilityDbContext CreateDbContext(string[] args) => KairoDbContextOptions.CreateForDesignTime<AccessibilityDbContext>(
        AccessibilityDbContext.SchemaName, (options, database) => new AccessibilityDbContext(options, database));
}
