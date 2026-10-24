using System.Net;
using System.Net.Http.Json;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Operations;
using Kairo.Modules.Operations.Data;
using Kairo.SharedKernel.Agents;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Operations;

public sealed class UsageLedgerTests(SqlServer sql)
{
    [Fact]
    public async Task Concurrent_calls_never_exceed_campaign_capacity_and_duplicate_callbacks_do_not_consume_again()
    {
        using var api = new KairoApiFactory(sql.KairoConnectionString, settings: new Dictionary<string, string> { ["Kairo:Operations:Limits:TextCalls"] = "5" });
        var brand = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        var invocation = await OpenAsync(api, brand, campaign);
        var request = new ReserveCall(Guid.NewGuid(), "gemini-3.8-flash", "text", "global", TextCalls: 1);
        var duplicates = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ReserveAsync(api, invocation, request)));
        Assert.All(duplicates, value => Assert.Equal(HttpStatusCode.OK, value.StatusCode));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => ReserveAsync(api, invocation, request with { RequestId = Guid.NewGuid() })));
        Assert.Equal(4, attempts.Count(value => value.StatusCode == HttpStatusCode.OK));
        Assert.Equal(8, attempts.Count(value => value.StatusCode == HttpStatusCode.TooManyRequests));
        var conflict = await ReserveAsync(api, invocation, request with { Model = "other-model" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand, CancellationToken.None);
        var usage = await scope.ServiceProvider.GetRequiredService<UsageLedger>().ReadAsync(campaign, CancellationToken.None);
        Assert.Equal(5, usage.ProviderCalls);
        Assert.Equal(0, usage.Remaining.TextCalls);
        Assert.False(usage.CostComplete);
        Assert.Equal(5, usage.UnknownCostCalls);
    }

    [Fact]
    public async Task Reports_are_idempotent_bound_to_invocation_and_cannot_change_consumed_capacity()
    {
        using var api = new KairoApiFactory(sql.KairoConnectionString);
        var invocation = await OpenAsync(api, Guid.NewGuid(), Guid.NewGuid());
        var request = new ReserveCall(Guid.NewGuid(), "gemini-3.8-flash", "text", "global", TextCalls: 1);
        Assert.Equal(HttpStatusCode.OK, (await ReserveAsync(api, invocation, request)).StatusCode);
        var report = new ReportCall("succeeded", InputTokens: 100, OutputTokens: 20, ThoughtTokens: 10, CachedTokens: 0);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(api, invocation, request.RequestId, report)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReportAsync(api, invocation, request.RequestId, report)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ReportAsync(api, invocation, request.RequestId, report with { OutputTokens = 30 })).StatusCode);
        var other = await OpenAsync(api, invocation.BrandId, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await ReportAsync(api, other, request.RequestId, report)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReserveAsync(api, invocation with { Token = (invocation.Token[0] == 'b' ? "c" : "b") + invocation.Token[1..] }, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReserveAsync(api, invocation with { BrandId = Guid.NewGuid() }, request)).StatusCode);
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(invocation.BrandId, CancellationToken.None);
        var call = Assert.Single(await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().Calls.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, call.TextCalls);
        Assert.Equal(0.0001875m, call.EstimatedUsd);
        Assert.Equal(UsageRates.Version, call.RateVersion);
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().Calls.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Expired_or_closed_invocations_cannot_reserve_and_campaigns_have_separate_capacity()
    {
        using var api = new KairoApiFactory(sql.KairoConnectionString, settings: new Dictionary<string, string> { ["Kairo:Operations:Limits:Images"] = "1" });
        var brand = Guid.NewGuid();
        var first = await OpenAsync(api, brand, Guid.NewGuid(), "/create/images");
        var second = await OpenAsync(api, brand, Guid.NewGuid(), "/create/images");
        var request = new ReserveCall(Guid.NewGuid(), "gemini-2.5-flash-image", "image", "global", Images: 1);
        Assert.Equal(HttpStatusCode.OK, (await ReserveAsync(api, first, request)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ReserveAsync(api, first, request with { RequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReserveAsync(api, second, request)).StatusCode);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().Invocations.Where(row => row.Id == first.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Closed, true), TestContext.Current.CancellationToken);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await ReserveAsync(api, first, request with { RequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReserveAsync(api, first, request)).StatusCode);
        api.Clock.Advance(TimeSpan.FromMinutes(26));
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReserveAsync(api, second, request)).StatusCode);
    }

    private static async Task<AgentInvocation> OpenAsync(KairoApiFactory api, Guid brand, Guid campaign, string operation = "/create/copy")
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand, CancellationToken.None);
        return await scope.ServiceProvider.GetRequiredService<UsageLedger>().OpenAsync(new(brand, campaign), operation, CancellationToken.None);
    }

    private static HttpClient CallbackClient(KairoApiFactory api, AgentInvocation invocation)
    {
        var client = api.CreateBrowser();
        client.DefaultRequestHeaders.Add("X-Kairo-Usage-Token", invocation.Token);
        return client;
    }

    private static async Task<HttpResponseMessage> ReserveAsync(KairoApiFactory api, AgentInvocation invocation, ReserveCall request)
    {
        using var client = CallbackClient(api, invocation);
        return await client.PostAsJsonAsync($"/api/brands/{invocation.BrandId}/operations/invocations/{invocation.Id}/reserve", request);
    }

    private static async Task<HttpResponseMessage> ReportAsync(KairoApiFactory api, AgentInvocation invocation, Guid requestId, ReportCall request)
    {
        using var client = CallbackClient(api, invocation);
        return await client.PostAsJsonAsync($"/api/brands/{invocation.BrandId}/operations/invocations/{invocation.Id}/calls/{requestId}/report", request);
    }
}
