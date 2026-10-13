using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

public sealed class OutboxStorageTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_published_event_is_stored_for_its_workspace_with_a_camel_case_payload()
    {
        var brandId = Guid.NewGuid();

        await LivePublishing.PublishAsync(_api, brandId, "test.happened", new { UserId = brandId, Removed = true });

        var row = Assert.Single(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Equal("test.happened", row.Type);
        Assert.Equal($$"""{"userId":"{{brandId}}","removed":true}""", row.Payload);
        Assert.Equal("Everyone", row.AudienceKind);
    }

    [Fact]
    public async Task The_audience_is_stored_with_the_event()
    {
        var brandId = Guid.NewGuid();
        var priya = Guid.NewGuid();
        var arjun = Guid.NewGuid();

        await LivePublishing.PublishAsync(_api, brandId, "test.for_admins", audience: LiveAudience.Roles(BrandRoles.Admin | BrandRoles.Approver));
        await LivePublishing.PublishAsync(_api, brandId, "test.for_people", audience: LiveAudience.Users(priya, arjun));

        var rows = await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId);
        Assert.Equal(("Roles", 5), (rows[0].AudienceKind, rows[0].AudienceRoles));
        Assert.Equal("Users", rows[1].AudienceKind);
        Assert.Equal([priya, arjun], JsonSerializer.Deserialize<Guid[]>(rows[1].AudienceUsers!)!);
    }

    [Fact]
    public async Task An_event_published_in_a_rolled_back_transaction_is_not_stored()
    {
        var brandId = Guid.NewGuid();
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            var live = scope.ServiceProvider.GetRequiredService<ILiveUpdates>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            await live.PublishAsync(new LiveEvent("test.discarded", new { }, LiveAudience.Everyone), Token);
            await database.RollbackAsync(Token);
        }

        Assert.Empty(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId));
    }

    [Fact]
    public async Task Publishing_needs_a_workspace()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var live = scope.ServiceProvider.GetRequiredService<ILiveUpdates>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            live.PublishAsync(new LiveEvent("test.happened", new { }, LiveAudience.Everyone), Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("team")]
    [InlineData("Team.Changed")]
    [InlineData("team changed")]
    [InlineData("team.")]
    public async Task Event_types_are_lowercase_and_dotted(string type)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => LivePublishing.PublishAsync(_api, Guid.NewGuid(), type));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
