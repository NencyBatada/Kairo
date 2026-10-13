using Kairo.Modules.Brands.Connections;

namespace Kairo.Modules.Brands.Data;

internal sealed class BrandsDbContext(DbContextOptions<BrandsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "brands";

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Connection> Connections => Set<Connection>();

    public DbSet<ConsentNonce> ConsentNonces => Set<ConsentNonce>();

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

        modelBuilder.Entity<Connection>(connection =>
        {
            connection.ToTable("Connections");
            connection.HasIndex(c => new { c.BrandId, c.Product }).IsUnique();
            connection.HasIndex(c => c.GoogleSubject);
            connection.Property(c => c.Product).HasConversion<string>().HasMaxLength(20);
            connection.Property(c => c.State).HasConversion<string>().HasMaxLength(20);
            connection.Property(c => c.ExternalId).HasMaxLength(100);
            connection.Property(c => c.DisplayName).HasMaxLength(200);
            connection.Property(c => c.Scopes).HasMaxLength(1000);
            connection.Property(c => c.GoogleSubject).HasMaxLength(255);
            connection.Property(c => c.RefreshToken).HasMaxLength(4096);
            connection.Property(c => c.KeyVersion).HasMaxLength(300);
            connection.Property(c => c.ConnectedByName).HasMaxLength(200);
            connection.Property(c => c.ReconnectReason).HasMaxLength(40);
        });

        modelBuilder.Entity<ConsentNonce>(nonce =>
        {
            nonce.ToTable("ConsentNonces");
            nonce.HasKey(n => n.Nonce);
            nonce.Property(n => n.Nonce).HasMaxLength(32);
            nonce.HasIndex(n => n.ExpiresAt);
        });
    }
}
