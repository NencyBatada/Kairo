namespace Kairo.SharedKernel.Agents;

public sealed record AgentUsageScope(Guid BrandId, Guid? CampaignId = null, Guid? RunId = null);
public sealed record AgentInvocation(Guid Id, Guid BrandId, string Token);

public interface IAgentInvocations
{
    Task<AgentInvocation> OpenAsync(string operation, CancellationToken ct);
    Task CloseAsync(Guid id, CancellationToken ct);
}

public sealed class AgentUsageContext
{
    public AgentUsageScope? Current { get; private set; }

    public IDisposable Enter(AgentUsageScope value)
    {
        var previous = Current;
        Current = value;
        return new Restore(() => Current = previous);
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
