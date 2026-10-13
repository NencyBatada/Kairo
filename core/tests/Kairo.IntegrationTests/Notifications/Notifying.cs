using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Notifications;

internal static class Notifying
{
    public static Notification Sample(LiveAudience audience, string kind = "connection.problem") =>
        new(kind, audience, "Google Ads needs reconnecting", "Reconnect it so refreshes and changes can continue.", "/settings/connections");

    public static async Task NotifyAsync(KairoApiFactory api, Guid brandId, Notification notification)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<INotifications>().NotifyAsync(notification, TestContext.Current.CancellationToken);
    }
}
