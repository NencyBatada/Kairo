using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Approvals;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Approvals.Data;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class ApprovalHashTests
{
    [Fact]
    public void Object_order_is_canonical_but_content_and_array_order_are_significant()
    {
        Assert.Equal(ApprovalHash.Of("{\"a\":1,\"b\":{\"c\":2,\"d\":3}}"), ApprovalHash.Of("{\"b\":{\"d\":3,\"c\":2},\"a\":1}"));
        Assert.NotEqual(ApprovalHash.Of("{\"budget\":100}"), ApprovalHash.Of("{\"budget\":101}"));
        Assert.NotEqual(ApprovalHash.Of("[1,2]"), ApprovalHash.Of("[2,1]"));
    }
}

public sealed class ApprovalLockTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);
    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task Brief_approval_is_persisted_with_its_exact_versions_and_cannot_be_changed_or_deleted()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        var db = scope.ServiceProvider.GetRequiredService<ApprovalsDbContext>();
        var entry = await db.Entries.SingleAsync(row => row.RoomId == roomId && row.Gate == "brief" && !row.Invalidated, Token);
        Assert.Equal(_world.Approver.UserId, entry.ApproverId);
        Assert.Equal(ApprovalHash.Of(entry.Snapshot), entry.Hash);
        Assert.Equal(1, JsonDocument.Parse(entry.Snapshot).RootElement.GetProperty("briefVersion").GetInt32());
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE approvals.Entries SET Hash = N'changed' WHERE Id = {entry.Id}", Token));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM approvals.Entries WHERE Id = {entry.Id}", Token));
    }

    [Fact]
    public async Task Invalidating_an_approval_does_not_rewrite_history_or_allow_old_content_to_be_reused()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        var db = scope.ServiceProvider.GetRequiredService<ApprovalsDbContext>();
        var original = await db.Entries.SingleAsync(row => row.RoomId == roomId && row.Gate == "brief" && !row.Invalidated, Token);
        var locks = scope.ServiceProvider.GetRequiredService<IApprovalLocks>();
        Assert.True(await locks.MatchesAsync(roomId, "brief", original.Snapshot, Token));
        await locks.InvalidateAsync(roomId, ["brief"], Token);
        Assert.False(await locks.MatchesAsync(roomId, "brief", original.Snapshot, Token));
        Assert.Equal(2, await db.Entries.CountAsync(row => row.RoomId == roomId && row.Gate == "brief", Token));
    }

    [Fact]
    public async Task Approval_evidence_is_hidden_from_other_brands()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(roomId);
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApprovalsDbContext>().Entries.ToListAsync(Token));
    }

    [Fact]
    public async Task A_snapshot_mismatch_prevents_approval_even_when_the_room_version_has_not_changed()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var requested = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var pending = await requested.Content.ReadFromJsonAsync<JsonElement>(Token);
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key=N'kairo.all_brands', @value=1; UPDATE campaign_rooms.Rooms SET GateHash = REPLICATE('0',64) WHERE Id=@room";
        command.Parameters.AddWithValue("@room", roomId);
        await command.ExecuteNonQueryAsync(Token);
        using var refused = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "approve", new { gate = "brief", version = Campaigns.Version(pending) });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }
}
