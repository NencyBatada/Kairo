using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.SharedKernel.Persistence;

public static class KairoDbContextOptions
{
    public const string DesignTimeConnectionString = "Server=localhost;Database=kairo;Integrated Security=true;TrustServerCertificate=true";

    public static DbContextOptionsBuilder UseKairoDatabase(
        this DbContextOptionsBuilder options,
        IServiceProvider services,
        string schema) =>
        options.UseKairoDatabase(services.GetRequiredService<KairoDatabase>(), schema);

    public static DbContextOptionsBuilder UseKairoDatabase(
        this DbContextOptionsBuilder options,
        KairoDatabase database,
        string schema) =>
        options
            .UseSqlServer(database.Connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .AddInterceptors(SessionContextInterceptor.Instance);

    public static TContext CreateForDesignTime<TContext>(
        string schema,
        Func<DbContextOptions<TContext>, KairoDatabase, TContext> create)
        where TContext : DbContext
    {
        var database = new KairoDatabase(DesignTimeConnectionString);
        var options = new DbContextOptionsBuilder<TContext>();
        options.UseKairoDatabase(database, schema);
        return create(options.Options, database);
    }
}
