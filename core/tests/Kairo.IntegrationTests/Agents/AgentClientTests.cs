using Kairo.SharedKernel.Agents;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Agents;

public sealed class AgentClientTests
{
    private const string Audience = "https://kairo-agents-abc123-el.a.run.app";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_generation_limit_keeps_its_code_when_invocation_closure_fails()
    {
        var agents = new FakeAgents { Respond = (_, _) => new(System.Net.HttpStatusCode.TooManyRequests)
            { Content = System.Net.Http.Json.JsonContent.Create(new { error = "generation_limit_exceeded" }) } };
        var invocation = new BrokenClosure();
        var error = await Assert.ThrowsAsync<AgentCallException>(() =>
            Client(agents, invocations: invocation).PostAsync<Echo, Echo>("create/copy", new("Acme", 1), Timeout, Token));
        Assert.Equal(AgentCallException.GenerationLimitExceeded, error.Code);
        Assert.True(invocation.Closed);
        Assert.Single(agents.Requests);
    }

    [Fact]
    public async Task Carries_the_active_trace_and_safe_correlation_ids_to_python()
    {
        var agents = new FakeAgents { Respond = (_, _) => FakeAgents.Json(new { name = "ok", count = 0 }) };
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var brand = Guid.NewGuid();
        var run = Guid.NewGuid();
        activity.SetTag("kairo.brand_id", brand.ToString());
        activity.SetTag("kairo.run_id", run.ToString());
        activity.SetTag("prompt", "private");
        await Client(agents).PostAsync<Echo, Echo>("brand-kit/draft", new Echo("Acme", 1), Timeout, Token);
        var request = Assert.Single(agents.Requests);
        Assert.True(ActivityContext.TryParse(request.TraceParent, null, out var context));
        Assert.Equal(activity.TraceId, context.TraceId);
        Assert.Equal(brand.ToString(), request.BrandId);
        Assert.Equal(run.ToString(), request.RunId);
    }

    [Fact]
    public async Task Posts_camel_case_JSON_to_the_agent_service_and_reads_the_answer()
    {
        var agents = new FakeAgents
        {
            Respond = (_, body) => FakeAgents.Json(new { name = body.GetProperty("name").GetString() + "!", count = 2 }),
        };

        var answer = await Client(agents).PostAsync<Echo, Echo>("brand-kit/draft", new Echo("Acme", 1), Timeout, Token);

        Assert.Equal(new Echo("Acme!", 2), answer);
        var request = Assert.Single(agents.Requests);
        Assert.Equal("http://agents.test/brand-kit/draft", request.Url);
        Assert.Equal(1, request.Body.GetProperty("count").GetInt32());
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task Sends_a_Google_ID_token_for_the_audience_when_one_is_set()
    {
        var agents = new FakeAgents { Respond = (_, _) => FakeAgents.Json(new { name = "ok", count = 0 }) };

        await Client(agents, audience: Audience).PostAsync<Echo, Echo>("brand-kit/draft", new Echo("Acme", 1), Timeout, Token);

        Assert.Equal($"Bearer id-token-for:{Audience}", Assert.Single(agents.Requests).Authorization);
    }

    [Fact]
    public async Task A_failure_to_get_an_identity_token_is_a_failed_call()
    {
        var agents = new FakeAgents { Respond = (_, _) => FakeAgents.Json(new { name = "ok", count = 0 }) };

        var error = await Assert.ThrowsAsync<AgentCallException>(() =>
            Client(agents, audience: Audience, identity: new BrokenIdentity()).PostAsync<Echo, Echo>("brand-kit/draft", new Echo("Acme", 1), Timeout, Token));

        Assert.Equal(AgentCallException.Failed, error.Code);
        Assert.Empty(agents.Requests);
    }

    [Fact]
    public async Task An_error_status_is_a_failed_call()
    {
        var agents = new FakeAgents { Failures = 1 };

        var error = await Assert.ThrowsAsync<AgentCallException>(() =>
            Client(agents).PostAsync<Echo, Echo>("brand-kit/draft", new Echo("Acme", 1), Timeout, Token));

        Assert.Equal(AgentCallException.Failed, error.Code);
        Assert.Contains("502", error.Message);
    }

    [Fact]
    public async Task An_answer_that_is_not_the_expected_JSON_is_a_bad_answer()
    {
        var agents = new FakeAgents { Respond = (_, _) => FakeAgents.Text("not json") };

        var error = await Assert.ThrowsAsync<AgentCallException>(() =>
            Client(agents).PostAsync<Echo, Echo>("brand-kit/draft", new Echo("Acme", 1), Timeout, Token));

        Assert.Equal(AgentCallException.BadAnswer, error.Code);
    }

    [Fact]
    public async Task A_slow_answer_times_out()
    {
        var agents = new FakeAgents { Delay = TimeSpan.FromSeconds(10), Respond = (_, _) => FakeAgents.Json(new { name = "late", count = 0 }) };

        var error = await Assert.ThrowsAsync<AgentCallException>(() =>
            Client(agents).PostAsync<Echo, Echo>("brand-kit/draft", new Echo("Acme", 1), TimeSpan.FromMilliseconds(200), Token));

        Assert.Equal(AgentCallException.Timeout, error.Code);
    }

    [Fact]
    public async Task Without_an_address_nothing_is_sent()
    {
        var agents = new FakeAgents();

        var error = await Assert.ThrowsAsync<AgentCallException>(() =>
            Client(agents, url: null).PostAsync<Echo, Echo>("brand-kit/draft", new Echo("Acme", 1), Timeout, Token));

        Assert.Equal(AgentCallException.NotConfigured, error.Code);
        Assert.Empty(agents.Requests);
    }

    private static AgentClient Client(FakeAgents agents, string? url = "http://agents.test", string? audience = null, IAgentIdentity? identity = null, IAgentInvocations? invocations = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AgentClient.UrlSetting] = url,
                [AgentClient.AudienceSetting] = audience,
            })
            .Build());
        services.AddKairoAgents();
        if (invocations is not null) services.AddSingleton(invocations);
        services.AddSingleton(identity ?? new FakeAgentIdentity());
        services.AddHttpClient(AgentClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => agents);
        return services.BuildServiceProvider().GetRequiredService<AgentClient>();
    }

    private sealed record Echo(string Name, int Count);

    private sealed class BrokenClosure : IAgentInvocations
    {
        public bool Closed { get; private set; }
        public Task<AgentInvocation> OpenAsync(string operation, CancellationToken ct) => Task.FromResult(new AgentInvocation(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64)));
        public Task CloseAsync(Guid id, CancellationToken ct)
        {
            Closed = true;
            throw new InvalidOperationException("database unavailable");
        }
    }

    private sealed class BrokenIdentity : IAgentIdentity
    {
        public Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The metadata server is unavailable.");
    }

    private sealed class FakeAgentIdentity : IAgentIdentity
    {
        public Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken) =>
            Task.FromResult($"id-token-for:{audience}");
    }
}
