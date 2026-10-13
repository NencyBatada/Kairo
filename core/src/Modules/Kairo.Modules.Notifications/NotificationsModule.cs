using Kairo.Contracts.Notifications;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Publishing;

namespace Kairo.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, NotificationsDbContext.SchemaName));
        services.AddScoped<INotifications, Notifier>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(cancellationToken);
}
