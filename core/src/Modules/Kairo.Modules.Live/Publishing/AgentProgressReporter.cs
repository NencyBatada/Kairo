using Kairo.Contracts.Live;

namespace Kairo.Modules.Live.Publishing;

internal sealed class AgentProgressReporter(ILiveUpdates live, TimeProvider time) : IAgentProgress
{
    public Task ReportAsync(AgentProgress progress, CancellationToken cancellationToken) =>
        live.PublishAsync(AgentProgressEvents.Progress(progress, time.GetUtcNow()), cancellationToken);
}
