using Kairo.Modules.Catalogue.Imports;
using Kairo.Modules.Catalogue.Products;

namespace Kairo.Modules.Catalogue.Data;

internal sealed class CatalogueDbContext(DbContextOptions<CatalogueDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "catalogue";

    public DbSet<Product> Products => Set<Product>();

    public DbSet<CatalogueSource> Sources => Set<CatalogueSource>();

    public DbSet<CatalogueImport> Imports => Set<CatalogueImport>();

    public DbSet<StockEvent> StockEvents => Set<StockEvent>();

    public Guid BrandId => CurrentBrandId ?? throw new InvalidOperationException("No brand is set for this request.");

    public IQueryable<Product> BrandProducts => Products.Where(product => product.BrandId == BrandId);

    public IQueryable<CatalogueSource> BrandSources => Sources.Where(source => source.BrandId == BrandId);

    public IQueryable<CatalogueImport> BrandImports => Imports.Where(import => import.BrandId == BrandId);

    public IQueryable<StockEvent> BrandStockEvents => StockEvents.Where(stock => stock.BrandId == BrandId);

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("Products");
            product.HasKey(p => p.Id).IsClustered(false);
            product.HasIndex(p => new { p.BrandId, p.OfferId }).IsUnique().IsClustered();
            product.HasIndex(p => new { p.BrandId, p.Status, p.Missing });
            product.Property(p => p.OfferId).HasMaxLength(50);
            product.Property(p => p.Title).HasMaxLength(150);
            product.Property(p => p.Link).HasMaxLength(2000);
            product.Property(p => p.ImageLink).HasMaxLength(2000);
            product.Property(p => p.Price).HasPrecision(19, 4);
            product.Property(p => p.SalePrice).HasPrecision(19, 4);
            product.Property(p => p.Currency).HasMaxLength(3);
            product.Property(p => p.Availability).HasMaxLength(20);
            product.Property(p => p.Category).HasMaxLength(750);
            product.Property(p => p.GoogleCategory).HasMaxLength(750);
            product.Property(p => p.Brand).HasMaxLength(70);
            product.Property(p => p.Gtin).HasMaxLength(14);
            product.Property(p => p.ItemGroupId).HasMaxLength(50);
            product.Property(p => p.Status).HasMaxLength(20);
            product.OwnsOne(p => p.Fixes, fixes =>
            {
                fixes.ToJson();
                fixes.Property(f => f.Title).HasJsonPropertyName("title");
                fixes.Property(f => f.Price).HasJsonPropertyName("price");
                fixes.Property(f => f.Currency).HasJsonPropertyName("currency");
                fixes.Property(f => f.Link).HasJsonPropertyName("link");
                fixes.Property(f => f.ImageLink).HasJsonPropertyName("imageLink");
                fixes.Property(f => f.Category).HasJsonPropertyName("category");
            });
            product.Navigation(p => p.Fixes).IsRequired();
        });

        modelBuilder.Entity<CatalogueSource>(source =>
        {
            source.ToTable("Sources");
            source.HasKey(s => s.BrandId);
            source.Property(s => s.BrandId).ValueGeneratedNever();
            source.Property(s => s.Kind).HasMaxLength(10);
            source.Property(s => s.FeedUrl).HasMaxLength(2000);
        });

        modelBuilder.Entity<CatalogueImport>(import =>
        {
            import.ToTable("Imports");
            import.HasKey(i => i.Id).IsClustered(false);
            import.HasIndex(i => new { i.BrandId, i.StartedAt }).IsClustered();
            import.Property(i => i.Number).UseIdentityColumn();
            import.Property(i => i.Kind).HasMaxLength(10);
            import.Property(i => i.FileName).HasMaxLength(200);
            import.Property(i => i.FeedUrl).HasMaxLength(2000);
            import.Property(i => i.Status).HasMaxLength(20);
            import.Property(i => i.ErrorCode).HasMaxLength(50);
            import.Property(i => i.ErrorMessage).HasMaxLength(500);
            import.Property(i => i.ContentHash).HasMaxLength(64);
            import.Property(i => i.StorageKey).HasMaxLength(200);
        });

        modelBuilder.Entity<StockEvent>(stock =>
        {
            stock.ToTable("StockEvents");
            stock.HasKey(e => e.Id).IsClustered(false);
            stock.HasIndex(e => new { e.BrandId, e.ProductId, e.At }).IsClustered();
            stock.Property(e => e.From).HasMaxLength(20);
            stock.Property(e => e.To).HasMaxLength(20);
        });
    }
}
