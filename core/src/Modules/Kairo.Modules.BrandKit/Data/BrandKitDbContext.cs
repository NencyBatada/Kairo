using Kairo.Modules.BrandKit.Files;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.Modules.BrandKit.Data;

internal sealed class BrandKitDbContext(DbContextOptions<BrandKitDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "brand_kit";

    public DbSet<KitVersion> Versions => Set<KitVersion>();

    public DbSet<KitFile> Files => Set<KitFile>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KitVersion>(version =>
        {
            version.ToTable("KitVersions");
            version.HasIndex(v => new { v.BrandId, v.Number }).IsUnique();
            version.HasIndex(v => v.BrandId, "IX_KitVersions_OneDraft").IsUnique().HasFilter("[Status] = N'Draft'");
            version.HasIndex(v => v.BrandId, "IX_KitVersions_OneApproved").IsUnique().HasFilter("[Status] = N'Approved'");
            version.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
            version.Property(v => v.Origin).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<KitFile>(file =>
        {
            file.ToTable("KitFiles");
            file.HasIndex(f => new { f.BrandId, f.UploadedAt });
            file.Property(f => f.Kind).HasConversion<string>().HasMaxLength(20);
            file.Property(f => f.FileName).HasMaxLength(200);
            file.Property(f => f.ContentType).HasMaxLength(100);
            file.Property(f => f.StorageKey).HasMaxLength(200);
        });
    }
}
