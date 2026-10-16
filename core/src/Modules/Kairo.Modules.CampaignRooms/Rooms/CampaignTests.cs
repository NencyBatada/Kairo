using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.ProofPlans;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed class CampaignTests(CampaignRoomsDbContext db) : ICampaignTests
{
    private static readonly string[] Running = [RoomStates.Live, RoomStates.ResultsReady];

    public async Task<IReadOnlyList<CampaignTest>> ListRunningAsync(CancellationToken cancellationToken)
    {
        var rooms = await db.Rooms.AsNoTracking().Where(room => Running.Contains(room.State)).OrderBy(room => room.CreatedAt).ToListAsync(cancellationToken);
        var tests = new List<CampaignTest>(rooms.Count);
        foreach (var room in rooms)
        {
            tests.Add(await ViewAsync(room, cancellationToken));
        }

        return tests;
    }

    public async Task<CampaignTest?> GetAsync(Guid roomId, CancellationToken cancellationToken) =>
        await db.Rooms.AsNoTracking().SingleOrDefaultAsync(room => room.Id == roomId, cancellationToken) is { } room
            ? await ViewAsync(room, cancellationToken)
            : null;

    private async Task<CampaignTest> ViewAsync(Room room, CancellationToken cancellationToken)
    {
        var plan = (await ProofPlanReader.LatestAsync(db, room.Id, cancellationToken))?.Read();
        var ended = RoomStates.Order(room.State) >= RoomStates.Order(RoomStates.ResultsReady) ? room.StateEnteredAt : (DateTimeOffset?)null;
        return new CampaignTest(room.Id, room.Title, room.State, plan?.Kpi ?? "orders", plan?.StartDate, plan?.EndDate, ended, room.TestEndedEarly);
    }
}

internal sealed record TestEndSweep(int Brands, int Ended);

internal sealed class TestEnds(CampaignRoomsDbContext db, ICampaignLifecycle lifecycle, TimeProvider time)
{
    public static readonly AuditActor Actor = new(AuditActorType.System, "Test window", "test-window");

    public async Task<int> EndDueAsync(BrandSummary brand, CancellationToken cancellationToken)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(brand.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        var live = await db.Rooms.AsNoTracking().Where(room => room.State == RoomStates.Live).Select(room => room.Id).ToListAsync(cancellationToken);
        var ended = 0;
        foreach (var roomId in live)
        {
            var plan = (await ProofPlanReader.LatestAsync(db, roomId, cancellationToken))?.Read();
            if (plan?.EndDate is not { } end || end >= today)
            {
                continue;
            }

            if (await lifecycle.AdvanceAsync(roomId, CampaignSignals.TestEnded, Actor, cancellationToken) == CampaignAdvance.Moved)
            {
                ended++;
            }
        }

        return ended;
    }
}

internal static class TestEndJobs
{
    public static void MapTestEndJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/campaigns/test-ends", SweepAsync);

    private static async Task<Ok<TestEndSweep>> SweepAsync(IBrandDirectory brands, IServiceScopeFactory scopes, ILogger<TestEnds> logger, IHostApplicationLifetime lifetime)
    {
        var stopping = lifetime.ApplicationStopping;
        var all = await brands.ListAllAsync(stopping);
        var ended = 0;
        foreach (var brand in all)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand.Id, stopping);
                ended += await scope.ServiceProvider.GetRequiredService<TestEnds>().EndDueAsync(brand, stopping);
            }
            catch (Exception exception) when (!stopping.IsCancellationRequested)
            {
                logger.LogError(exception, "Ending finished tests failed for brand {BrandId}.", brand.Id);
            }
        }

        return TypedResults.Ok(new TestEndSweep(all.Count, ended));
    }
}
