using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Kairo.SharedKernel.Persistence;

public abstract class ModuleDbContext : DbContext
{
    private static readonly MethodInfo ApplyBrandFilterMethod =
        typeof(ModuleDbContext).GetMethod(nameof(ApplyBrandFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    protected ModuleDbContext(DbContextOptions options, KairoDatabase database)
        : base(options)
    {
        KairoDatabase = database;
        database.Enlist(this);
    }

    internal KairoDatabase KairoDatabase { get; }

    protected Guid? CurrentBrandId => KairoDatabase.BrandId;

    protected bool AllBrands => KairoDatabase.AllBrands;

    protected abstract string Schema { get; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampBrand();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampBrand();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);

        var brandOwnedTypes = modelBuilder.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(type => typeof(IBrandOwned).IsAssignableFrom(type))
            .ToList();

        foreach (var type in brandOwnedTypes)
        {
            ApplyBrandFilterMethod.MakeGenericMethod(type).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyBrandFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IBrandOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => AllBrands || entity.BrandId == CurrentBrandId);

    private void StampBrand()
    {
        foreach (var entry in ChangeTracker.Entries<IBrandOwned>().Where(entry => entry.State == EntityState.Added))
        {
            if (entry.Entity.BrandId == Guid.Empty)
            {
                entry.Entity.BrandId = KairoDatabase.BrandId
                    ?? throw new InvalidOperationException("No brand is set for this request.");
            }
        }
    }
}
