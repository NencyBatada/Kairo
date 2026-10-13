using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Notifications.Data;

internal sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<NotificationsDbContext>(
            NotificationsDbContext.SchemaName,
            (options, database) => new NotificationsDbContext(options, database));
}
