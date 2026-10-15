using Kairo.SharedKernel.Persistence;

namespace Kairo.IntegrationTests.Persistence;

public sealed class AppLockTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_free_lock_is_taken_at_once()
    {
        await using var database = new KairoDatabase(sqlServer.KairoConnectionString);
        await database.BeginTransactionAsync(Token);

        Assert.True(await database.TryLockAsync(NewResource(), Token));
    }

    [Fact]
    public async Task A_lock_another_transaction_holds_is_not_taken()
    {
        var resource = NewResource();
        await using var holder = new KairoDatabase(sqlServer.KairoConnectionString);
        await holder.BeginTransactionAsync(Token);
        await holder.LockAsync(resource, Token);
        await using var other = new KairoDatabase(sqlServer.KairoConnectionString);
        await other.BeginTransactionAsync(Token);

        Assert.False(await other.TryLockAsync(resource, Token));
    }

    [Fact]
    public async Task The_lock_is_free_again_once_the_holder_commits()
    {
        var resource = NewResource();
        await using var holder = new KairoDatabase(sqlServer.KairoConnectionString);
        await holder.BeginTransactionAsync(Token);
        Assert.True(await holder.TryLockAsync(resource, Token));
        await holder.CommitAsync(Token);
        await using var other = new KairoDatabase(sqlServer.KairoConnectionString);
        await other.BeginTransactionAsync(Token);

        Assert.True(await other.TryLockAsync(resource, Token));
    }

    [Fact]
    public async Task Trying_a_lock_needs_a_transaction()
    {
        await using var database = new KairoDatabase(sqlServer.KairoConnectionString);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.TryLockAsync(NewResource(), Token));
    }

    private static string NewResource() => $"test:{Guid.NewGuid():N}";
}
