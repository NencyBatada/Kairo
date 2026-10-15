using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Live.Publishing;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

public sealed class AgentProgressTests(SqlServer sqlServer) : IAsyncDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 14, 9, 5, 0, TimeSpan.Zero);

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Agent_progress_reaches_every_member_of_the_workspace()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var listener = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);
        var jobId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var reportedAt = _api.Clock.GetUtcNow();

        await ReportAsync(brandId, new AgentProgress(jobId, AgentName.Create, AgentJobStatus.Running, "Generating video")
        {
            Subject = new AgentSubject(AgentSubjectKind.Campaign, campaignId),
            StepNumber = 2,
            StepCount = 5,
            Message = "Rendering the 15-second cut",
        });

        var message = await listener.NextAsync();
        Assert.Equal("agent.progress", message.Type);
        Assert.Equal(jobId, message.Data.GetProperty("jobId").GetGuid());
        Assert.Equal("create", message.Data.GetProperty("agent").GetString());
        Assert.Equal("running", message.Data.GetProperty("status").GetString());
        Assert.Equal("Generating video", message.Data.GetProperty("step").GetString());
        Assert.Equal(2, message.Data.GetProperty("stepNumber").GetInt32());
        Assert.Equal(5, message.Data.GetProperty("stepCount").GetInt32());
        Assert.Equal("Rendering the 15-second cut", message.Data.GetProperty("message").GetString());
        Assert.Equal("campaign", message.Data.GetProperty("subject").GetProperty("kind").GetString());
        Assert.Equal(campaignId, message.Data.GetProperty("subject").GetProperty("id").GetGuid());
        Assert.Equal(reportedAt, message.Data.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public async Task Agent_progress_stays_inside_its_workspace()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var otherBrandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await ReportAsync(otherBrandId, new AgentProgress(Guid.NewGuid(), AgentName.Sense, AgentJobStatus.Queued, "Waiting to start"));
        await LivePublishing.PublishAsync(_api, brandId, "workspace.changed");

        Assert.Equal("workspace.changed", (await listener.NextAsync()).Type);
    }

    [Fact]
    public void A_finished_job_without_steps_or_subject_sends_nulls()
    {
        var progress = new AgentProgress(Guid.NewGuid(), AgentName.Learn, AgentJobStatus.Succeeded, "  Saved lessons  ") { Message = "   " };

        var payload = Payload(AgentProgressEvents.Progress(progress, At));

        Assert.Equal("learn", payload.GetProperty("agent").GetString());
        Assert.Equal("succeeded", payload.GetProperty("status").GetString());
        Assert.Equal("Saved lessons", payload.GetProperty("step").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("stepNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("stepCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("message").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("subject").ValueKind);
        Assert.Equal(At, payload.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public void Agent_progress_goes_to_everyone_in_the_workspace()
    {
        var progress = new AgentProgress(Guid.NewGuid(), AgentName.Plan, AgentJobStatus.Failed, "Drafting the brief")
        {
            Subject = new AgentSubject(AgentSubjectKind.Opportunity, Guid.NewGuid()),
        };

        var liveEvent = AgentProgressEvents.Progress(progress, At);

        Assert.Equal(LiveAudienceKind.Everyone, liveEvent.Audience.Kind);
        Assert.Equal("opportunity", Payload(liveEvent).GetProperty("subject").GetProperty("kind").GetString());
    }

    public static TheoryData<AgentProgress> InvalidProgress => new(
        new AgentProgress(Guid.Empty, AgentName.Create, AgentJobStatus.Running, "Step"),
        new AgentProgress(Guid.NewGuid(), (AgentName)42, AgentJobStatus.Running, "Step"),
        new AgentProgress(Guid.NewGuid(), AgentName.Create, (AgentJobStatus)42, "Step"),
        new AgentProgress(Guid.NewGuid(), AgentName.Create, AgentJobStatus.Running, " "),
        new AgentProgress(Guid.NewGuid(), AgentName.Create, AgentJobStatus.Running, new string('s', AgentProgress.MaxStepLength + 1)),
        new AgentProgress(Guid.NewGuid(), AgentName.Create, AgentJobStatus.Running, "Step") { Message = new string('m', AgentProgress.MaxMessageLength + 1) },
        new AgentProgress(Guid.NewGuid(), AgentName.Create, AgentJobStatus.Running, "Step") { StepNumber = 1 },
        new AgentProgress(Guid.NewGuid(), AgentName.Create, AgentJobStatus.Running, "Step") { StepCount = 3 },
        new AgentProgress(Guid.NewGuid(), AgentName.Create, AgentJobStatus.Running, "Step") { StepNumber = 0, StepCount = 3 },
        new AgentProgress(Guid.NewGuid(), AgentName.Create, AgentJobStatus.Running, "Step") { StepNumber = 4, StepCount = 3 },
        new AgentProgress(Guid.NewGuid(), AgentName.Create, AgentJobStatus.Running, "Step") { Subject = new AgentSubject(AgentSubjectKind.Campaign, Guid.Empty) },
        new AgentProgress(Guid.NewGuid(), AgentName.Create, AgentJobStatus.Running, "Step") { Subject = new AgentSubject((AgentSubjectKind)42, Guid.NewGuid()) });

    [Theory]
    [MemberData(nameof(InvalidProgress))]
    public void Invalid_progress_is_refused(AgentProgress progress)
    {
        Assert.Throws<ArgumentException>(() => AgentProgressEvents.Progress(progress, At));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static JsonElement Payload(LiveEvent liveEvent) =>
        JsonSerializer.SerializeToElement(liveEvent.Payload, JsonSerializerOptions.Web);

    private async Task ReportAsync(Guid brandId, AgentProgress progress)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        await scope.ServiceProvider.GetRequiredService<IAgentProgress>().ReportAsync(progress, Token);
    }
}
