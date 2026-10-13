using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Persistence;

public sealed class BrandSeparationTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queries_see_only_the_current_brand()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var (brandA, brandB) = (Guid.NewGuid(), Guid.NewGuid());
        await plumbing.AddNoteAsync(brandA, "a");
        await plumbing.AddNoteAsync(brandB, "b");

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandA, Token);
        var texts = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.Select(note => note.Text).ToListAsync(Token);

        Assert.Equal(["a"], texts);
    }

    [Fact]
    public async Task Row_level_security_hides_other_brands_without_the_query_filter()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var (brandA, brandB) = (Guid.NewGuid(), Guid.NewGuid());
        await plumbing.AddNoteAsync(brandA, "a");
        await plumbing.AddNoteAsync(brandB, "b");

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandA, Token);
        var texts = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes
            .IgnoreQueryFilters()
            .Select(note => note.Text)
            .ToListAsync(Token);

        Assert.Equal(["a"], texts);
    }

    [Fact]
    public async Task Raw_sql_sees_nothing_when_no_brand_is_set()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        await plumbing.AddNoteAsync(Guid.NewGuid(), "a");

        await using var scope = plumbing.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var count = await notes.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [notes].[Notes]").SingleAsync(Token);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Rows_cannot_be_written_for_another_brand()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        notes.Notes.Add(new Note { BrandId = Guid.NewGuid(), Text = "elsewhere" });

        await Assert.ThrowsAsync<DbUpdateException>(() => notes.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task New_rows_take_the_current_brand()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        var noteId = await plumbing.AddNoteAsync(brandId, "a");

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        var note = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.SingleAsync(Token);
        Assert.Equal((noteId, brandId), (note.Id, note.BrandId));
    }

    [Fact]
    public async Task Saving_a_brand_owned_row_without_a_brand_fails()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);

        await using var scope = plumbing.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        notes.Notes.Add(new Note { Text = "orphan" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => notes.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task The_system_scope_sees_every_brand_until_it_ends()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        await plumbing.AddNoteAsync(Guid.NewGuid(), "a");
        await plumbing.AddNoteAsync(Guid.NewGuid(), "b");

        await using var scope = plumbing.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();

        int inside;
        await using (await database.EnterSystemScopeAsync(Token))
        {
            inside = await notes.Notes.CountAsync(Token);
        }

        var after = await notes.Notes.IgnoreQueryFilters().CountAsync(Token);
        Assert.Equal((2, 0), (inside, after));
    }

    [Fact]
    public async Task A_pooled_connection_does_not_keep_an_earlier_brand()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer, maxPoolSize: 1);
        var brandId = Guid.NewGuid();
        await plumbing.AddNoteAsync(brandId, "a");

        await using (var first = plumbing.CreateScope())
        {
            await first.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
            Assert.Equal(1, await first.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.CountAsync(Token));
        }

        await using var second = plumbing.CreateScope();
        var count = await second.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.IgnoreQueryFilters().CountAsync(Token);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task A_second_table_joins_the_existing_policy()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var (brandA, brandB) = (Guid.NewGuid(), Guid.NewGuid());
        foreach (var brand in new[] { brandA, brandB })
        {
            await using var writer = plumbing.CreateScope();
            await writer.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand, Token);
            var context = writer.ServiceProvider.GetRequiredService<NotesDbContext>();
            context.Pins.Add(new Pin());
            await context.SaveChangesAsync(Token);
        }

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandA, Token);
        var pins = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Pins.IgnoreQueryFilters().ToListAsync(Token);

        Assert.Equal(brandA, Assert.Single(pins).BrandId);
    }

    [Fact]
    public async Task Removing_a_table_from_the_policy_stops_filtering_it()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        await using (var writer = plumbing.CreateScope())
        {
            await writer.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
            var context = writer.ServiceProvider.GetRequiredService<NotesDbContext>();
            context.Pins.Add(new Pin());
            await context.SaveChangesAsync(Token);
        }

        await using var scope = plumbing.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        await notes.Database.ExecuteSqlRawAsync(BrandSeparation.RemoveTableSql("notes", "Pins"), Token);

        Assert.Equal(1, await notes.Pins.IgnoreQueryFilters().CountAsync(Token));
    }

    [Fact]
    public async Task Switching_brand_inside_a_transaction_applies_immediately()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var (brandA, brandB) = (Guid.NewGuid(), Guid.NewGuid());
        await plumbing.AddNoteAsync(brandA, "a");
        await plumbing.AddNoteAsync(brandB, "b");

        await using var scope = plumbing.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        await database.UseBrandAsync(brandA, Token);
        await database.BeginTransactionAsync(Token);
        var first = await notes.Notes.IgnoreQueryFilters().Select(note => note.Text).SingleAsync(Token);
        await database.UseBrandAsync(brandB, Token);
        var second = await notes.Notes.IgnoreQueryFilters().Select(note => note.Text).SingleAsync(Token);
        await database.RollbackAsync(Token);

        Assert.Equal(("a", "b"), (first, second));
    }
}
