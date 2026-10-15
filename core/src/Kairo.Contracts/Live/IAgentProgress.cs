namespace Kairo.Contracts.Live;

public interface IAgentProgress
{
    Task ReportAsync(AgentProgress progress, CancellationToken cancellationToken);
}

public enum AgentName
{
    Sense,
    Plan,
    Create,
    Check,
    Prove,
    Learn,
}

public enum AgentJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

public enum AgentSubjectKind
{
    Campaign,
    Opportunity,
}

public sealed record AgentSubject(AgentSubjectKind Kind, Guid Id);

public sealed record AgentProgress(Guid JobId, AgentName Agent, AgentJobStatus Status, string Step)
{
    public const int MaxStepLength = 100;

    public const int MaxMessageLength = 500;

    public AgentSubject? Subject { get; init; }

    public int? StepNumber { get; init; }

    public int? StepCount { get; init; }

    public string? Message { get; init; }
}
