using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Persistence;

public sealed class TransactionTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Changes_in_two_contexts_commit_together()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        await using (var scope = plumbing.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            notes.Notes.Add(new Note { Text = "first" });
            await notes.SaveChangesAsync(Token);
            var tags = scope.ServiceProvider.GetRequiredService<TagsDbContext>();
            tags.Tags.Add(new Tag { Name = "first" });
            await tags.SaveChangesAsync(Token);
            await database.CommitAsync(Token);
        }

        Assert.Equal((1, 1), await CountAsync(plumbing, brandId));
    }

    [Fact]
    public async Task Rolling_back_discards_changes_in_every_context()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        await using (var scope = plumbing.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            notes.Notes.Add(new Note { Text = "first" });
            await notes.SaveChangesAsync(Token);
            var tags = scope.ServiceProvider.GetRequiredService<TagsDbContext>();
            tags.Tags.Add(new Tag { Name = "first" });
            await tags.SaveChangesAsync(Token);
            await database.RollbackAsync(Token);
        }

        Assert.Equal((0, 0), await CountAsync(plumbing, brandId));
    }

    [Fact]
    public async Task A_context_created_during_the_transaction_joins_it()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        await using (var scope = plumbing.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            var tags = scope.ServiceProvider.GetRequiredService<TagsDbContext>();
            tags.Tags.Add(new Tag { Name = "late" });
            await tags.SaveChangesAsync(Token);
            await database.RollbackAsync(Token);
        }

        Assert.Equal((0, 0), await CountAsync(plumbing, brandId));
    }

    [Fact]
    public async Task A_scope_can_run_a_second_transaction_after_the_first_ends()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        await using (var scope = plumbing.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            await database.UseBrandAsync(brandId, Token);

            await database.BeginTransactionAsync(Token);
            notes.Notes.Add(new Note { Text = "discarded" });
            await notes.SaveChangesAsync(Token);
            await database.RollbackAsync(Token);
            notes.ChangeTracker.Clear();

            await database.BeginTransactionAsync(Token);
            notes.Notes.Add(new Note { Text = "kept" });
            await notes.SaveChangesAsync(Token);
            await database.CommitAsync(Token);
        }

        Assert.Equal((1, 0), await CountAsync(plumbing, brandId));
    }

    private static async Task<(int Notes, int Tags)> CountAsync(PlumbingDatabase plumbing, Guid brandId)
    {
        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        var notes = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.CountAsync(Token);
        var tags = await scope.ServiceProvider.GetRequiredService<TagsDbContext>().Tags.CountAsync(Token);
        return (notes, tags);
    }
}
