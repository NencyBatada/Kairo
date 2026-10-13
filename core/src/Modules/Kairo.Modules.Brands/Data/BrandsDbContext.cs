namespace Kairo.Modules.Brands.Data;

internal sealed class BrandsDbContext(DbContextOptions<BrandsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "brands";

    public DbSet<Brand> Brands => Set<Brand>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(brand =>
        {
            brand.ToTable("Brands");
            brand.HasIndex(b => b.Handle).IsUnique();
            brand.Property(b => b.Handle).HasMaxLength(40);
            brand.Property(b => b.Name).HasMaxLength(100);
            brand.Property(b => b.TimeZone).HasMaxLength(64);
            brand.Property(b => b.Currency).HasMaxLength(3).IsFixedLength();
            brand.HasQueryFilter(b => AllBrands || b.Id == CurrentBrandId);
        });
    }
}
