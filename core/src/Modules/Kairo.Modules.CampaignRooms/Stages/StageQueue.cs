using Kairo.Contracts.Live;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Stages;

internal sealed class StageQueue(
    CampaignRoomsDbContext db,
    KairoDatabase database,
    IEnumerable<IStageWork> works,
    IAgentProgress progress,
    TimeProvider time)
{
    public IStageWork? Find(string kind) => works.FirstOrDefault(work => work.Kind == kind);

    public async Task<StageRun> EnqueueAsync(Room room, string kind, Guid? requestedBy, CancellationToken cancellationToken, string? input = null)
    {
        var work = Find(kind) ?? throw new InvalidOperationException($"No stage work is registered for {kind}.");
        if (work.Stage != room.State)
        {
            throw new InvalidOperationException($"{kind} runs in {work.Stage}, but the campaign is in {room.State}.");
        }

        var now = time.GetUtcNow();
        var last = await db.StageRuns.Where(existing => existing.RoomId == room.Id).MaxAsync(existing => (DateTimeOffset?)existing.QueuedAt, cancellationToken);
        var run = new StageRun
        {
            Id = Guid.NewGuid(),
            BrandId = room.BrandId,
            RoomId = room.Id,
            Stage = work.Stage,
            Agent = work.Agent,
            Kind = work.Kind,
            Status = StageRunStatuses.Queued,
            NextAttemptAt = now,
            RequestedBy = requestedBy,
            Input = input,
            TraceParent = Kairo.SharedKernel.Observability.KairoTelemetry.CaptureParent(),
            QueuedAt = last is { } previous && previous >= now ? previous.AddTicks(1) : now,
        };
        db.StageRuns.Add(run);
        room.RunStatus = run.Status;
        room.Status = RoomLifecycle.StatusOf(room.State, room.PendingGate, room.RunStatus);
        await database.AfterCommitAsync(
            token => progress.ReportAsync(StageProgress.Of(run, AgentJobStatus.Queued, "Queued"), token),
            cancellationToken);
        return run;
    }

    public Task<StageRun?> LatestAsync(Guid roomId, string stage, CancellationToken cancellationToken) =>
        db.StageRuns
            .Where(run => run.RoomId == roomId && run.Stage == stage && run.Status != StageRunStatuses.Cancelled)
            .OrderByDescending(run => run.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken);
}

internal static class StageProgress
{
    public static AgentProgress Of(StageRun run, AgentJobStatus status, string step, string? message = null) =>
        new(run.Id, AgentOf(run.Agent), status, step)
        {
            Subject = new AgentSubject(AgentSubjectKind.Campaign, run.RoomId),
            Message = message is { Length: > AgentProgress.MaxMessageLength } ? message[..AgentProgress.MaxMessageLength] : message,
        };

    public static AgentName AgentOf(string agent) => agent switch
    {
        StageAgents.Create => AgentName.Create,
        StageAgents.Check => AgentName.Check,
        StageAgents.Prove => AgentName.Prove,
        StageAgents.Learn => AgentName.Learn,
        _ => AgentName.Plan,
    };

    public static string ActorName(string agent) => $"{char.ToUpperInvariant(agent[0])}{agent[1..]} agent";
}
