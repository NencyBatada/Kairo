using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

internal static class LivePublishing
{
    public static async Task PublishAsync(KairoApiFactory api, Guid brandId, string type, object? payload = null, LiveAudience? audience = null)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<ILiveUpdates>().PublishAsync(
            new LiveEvent(type, payload ?? new { }, audience ?? LiveAudience.Everyone),
            TestContext.Current.CancellationToken);
    }
}
