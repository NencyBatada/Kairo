using System.Net;
using System.Net.Http.Json;
using Kairo.Contracts.Operations;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.Modules.Operations.Data;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Operations;

public sealed class CampaignUsageTests(SqlServer sql)
{
    [Fact]
    public async Task Usage_reads_reuse_campaign_access_and_background_invocations_keep_authoritative_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var world = await CampaignWorld.CreateAsync(sql);
        var campaign = await world.OpenWithBriefAsync();
        var path = $"/api/brands/{world.BrandId}/campaigns/{campaign}/usage";
        using var response = await world.Marketer.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var usage = await response.Content.ReadFromJsonAsync<CampaignUsage>(ct);
        Assert.Equal(campaign, usage!.CampaignId);
        Assert.Equal(new GenerationCapacity(100, 50, 10), usage.Remaining);
        Assert.True(usage.CostComplete);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
        using var approver = await world.Approver.Browser.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.OK, approver.StatusCode);
        using var anonymous = await world.Api.CreateBrowser().GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var missing = await world.Marketer.GetAsync($"/api/brands/{world.BrandId}/campaigns/{Guid.NewGuid()}/usage", ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await using var scope = world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(world.BrandId, ct);
        var invocations = await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().Invocations.AsNoTracking().Where(row => row.CampaignId == campaign).ToListAsync(ct);
        Assert.NotEmpty(invocations);
        Assert.All(invocations, invocation =>
        {
            Assert.Equal(world.BrandId, invocation.BrandId);
            Assert.NotNull(invocation.RunId);
            Assert.True(invocation.Closed);
        });
    }
}
