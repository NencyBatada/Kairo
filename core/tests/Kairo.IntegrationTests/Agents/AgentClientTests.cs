using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Agents;

public sealed class AgentClientTests
{
    private const string Audience = "https://kairo-agents-abc123-el.a.run.app";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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

    private static AgentClient Client(FakeAgents agents, string? url = "http://agents.test", string? audience = null, IAgentIdentity? identity = null)
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
        services.AddSingleton(identity ?? new FakeAgentIdentity());
        services.AddHttpClient(AgentClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => agents);
        return services.BuildServiceProvider().GetRequiredService<AgentClient>();
    }

    private sealed record Echo(string Name, int Count);

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
