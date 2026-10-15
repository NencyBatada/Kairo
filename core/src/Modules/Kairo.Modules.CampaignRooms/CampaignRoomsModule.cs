using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms;

public sealed class CampaignRoomsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CampaignRoomsDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, CampaignRoomsDbContext.SchemaName));
        services.AddScoped<ICampaignRooms, RoomOpener>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CampaignRoomsDbContext>().Database.MigrateAsync(cancellationToken);
}
