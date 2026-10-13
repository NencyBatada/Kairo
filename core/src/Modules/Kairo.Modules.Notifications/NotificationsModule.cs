using Kairo.Contracts.Notifications;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Delivery;
using Kairo.Modules.Notifications.EmailSettings;
using Kairo.Modules.Notifications.Inbox;
using Kairo.Modules.Notifications.Publishing;

namespace Kairo.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, NotificationsDbContext.SchemaName));
        services.AddScoped<INotifications, Notifier>();
        services.AddHttpClient(SenderToken.ClientName).RemoveAllResilienceHandlers();
        services.AddSingleton<SenderToken>();
        services.AddSingleton<GmailSender>();
        services.AddSingleton<LogEmailSender>();
        services.AddSingleton<IEmailSender>(provider =>
            NotificationSettings.HasSender(provider.GetRequiredService<IConfiguration>())
                ? provider.GetRequiredService<GmailSender>()
                : provider.GetRequiredService<LogEmailSender>());
        services.AddScoped<EmailDispatcher>();
        services.AddSingleton<EmailDeliverySwitch>();
        services.AddHostedService<EmailDelivery>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.ServiceProvider.GetRequiredService<EmailDeliverySwitch>().TurnOn();
        api.MapInbox();
        api.MapEmailSettings();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(cancellationToken);
}
