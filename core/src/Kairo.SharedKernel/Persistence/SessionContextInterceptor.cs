using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kairo.SharedKernel.Persistence;

internal sealed class SessionContextInterceptor : DbConnectionInterceptor
{
    public static readonly SessionContextInterceptor Instance = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (eventData.Context is ModuleDbContext context)
        {
            context.KairoDatabase.ApplySessionContext(connection);
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is ModuleDbContext context)
        {
            await context.KairoDatabase.ApplySessionContextAsync(connection, cancellationToken);
        }
    }
}
