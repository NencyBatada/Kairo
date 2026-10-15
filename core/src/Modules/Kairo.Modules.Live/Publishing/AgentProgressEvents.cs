using Kairo.Contracts.Live;

namespace Kairo.Modules.Live.Publishing;

internal static class AgentProgressEvents
{
    public static LiveEvent Progress(AgentProgress progress, DateTimeOffset at)
    {
        Validate(progress);
        return new("agent.progress", new
        {
            jobId = progress.JobId,
            agent = Name(progress.Agent),
            status = Name(progress.Status),
            step = progress.Step.Trim(),
            stepNumber = progress.StepNumber,
            stepCount = progress.StepCount,
            message = string.IsNullOrWhiteSpace(progress.Message) ? null : progress.Message.Trim(),
            subject = progress.Subject is { } subject ? new { kind = Name(subject.Kind), id = subject.Id } : null,
            at,
        }, LiveAudience.Everyone);
    }

    private static void Validate(AgentProgress progress)
    {
        if (progress.JobId == Guid.Empty)
        {
            throw new ArgumentException("An agent job needs an ID.", nameof(progress));
        }

        if (!Enum.IsDefined(progress.Agent) || !Enum.IsDefined(progress.Status))
        {
            throw new ArgumentException("The agent or status isn't one Kairo knows.", nameof(progress));
        }

        if (string.IsNullOrWhiteSpace(progress.Step) || progress.Step.Trim().Length > AgentProgress.MaxStepLength)
        {
            throw new ArgumentException($"A step label needs 1 to {AgentProgress.MaxStepLength} characters.", nameof(progress));
        }

        if (progress.Message?.Trim().Length > AgentProgress.MaxMessageLength)
        {
            throw new ArgumentException($"A progress message can't be longer than {AgentProgress.MaxMessageLength} characters.", nameof(progress));
        }

        if (progress.StepNumber is null != progress.StepCount is null)
        {
            throw new ArgumentException("A step number and a step count go together.", nameof(progress));
        }

        if (progress.StepNumber is { } number && progress.StepCount is { } count && (count < 1 || number < 1 || number > count))
        {
            throw new ArgumentException("The step number must be between 1 and the step count.", nameof(progress));
        }

        if (progress.Subject is { } subject && (subject.Id == Guid.Empty || !Enum.IsDefined(subject.Kind)))
        {
            throw new ArgumentException("The campaign or opportunity reference isn't valid.", nameof(progress));
        }
    }

    private static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();
}
