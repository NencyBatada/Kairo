using Kairo.SharedKernel;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Persistence;

internal sealed class Note : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Text { get; set; } = "";
}

internal sealed class Pin : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }
}

internal sealed class Tag
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
}

internal sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Pin> Pins => Set<Pin>();

    protected override string Schema => "notes";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>().ToTable("Notes");
        modelBuilder.Entity<Pin>().ToTable("Pins");
    }
}

internal sealed class TagsDbContext(DbContextOptions<TagsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public DbSet<Tag> Tags => Set<Tag>();

    protected override string Schema => "tags";

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Tag>().ToTable("Tags");
}

internal sealed class PlumbingDatabase(ServiceProvider services, string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public static async Task<PlumbingDatabase> CreateAsync(SqlServer sqlServer, int? maxPoolSize = null)
    {
        var connectionString = await sqlServer.CreateDatabaseAsync("plumbing");
        if (maxPoolSize is { } size)
        {
            connectionString = new SqlConnectionStringBuilder(connectionString) { MaxPoolSize = size }.ConnectionString;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:kairo"] = connectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKairoCore();
        services.AddDbContext<NotesDbContext>((provider, options) => options.UseKairoDatabase(provider, "notes"));
        services.AddDbContext<TagsDbContext>((provider, options) => options.UseKairoDatabase(provider, "tags"));

        var database = new PlumbingDatabase(services.BuildServiceProvider(), connectionString);
        await using var scope = database.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var tags = scope.ServiceProvider.GetRequiredService<TagsDbContext>();
        await notes.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        await tags.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        await notes.Database.ExecuteSqlRawAsync(BrandSeparation.CreatePredicateSql("notes"));
        await notes.Database.ExecuteSqlRawAsync(BrandSeparation.AddTableSql("notes", "Notes"));
        await notes.Database.ExecuteSqlRawAsync(BrandSeparation.AddTableSql("notes", "Pins"));
        return database;
    }

    public AsyncServiceScope CreateScope() => services.CreateAsyncScope();

    public async Task<Guid> AddNoteAsync(Guid brandId, string text)
    {
        await using var scope = CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        await database.UseBrandAsync(brandId, CancellationToken.None);
        var note = new Note { Text = text };
        notes.Notes.Add(note);
        await notes.SaveChangesAsync();
        return note.Id;
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();
}
