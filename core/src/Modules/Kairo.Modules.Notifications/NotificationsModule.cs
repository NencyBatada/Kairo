using Kairo.Contracts.Notifications;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Delivery;
using Kairo.Modules.Notifications.EmailSettings;
using Kairo.Modules.Notifications.Inbox;
using Kairo.Modules.Notifications.Publishing;
using Kairo.Modules.Notifications.Scheduling;

namespace Kairo.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, NotificationsDbContext.SchemaName));
        services.AddScoped<INotifications, Notifier>();
        services.AddScoped<IInvitationEmails, InvitationEmails>();
        services.AddScoped<IOperationsAlerts, OperationsAlerts>();
        services.AddScoped<BrandDeliveries>();
        services.AddScoped<DueDeliveries>();
        services.AddHttpClient(SenderToken.ClientName, client => client.Timeout = EmailDispatcher.SendTimeout).RemoveAllResilienceHandlers();
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
        jobs.MapDeliveryJobs();
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(cancellationToken);
}
