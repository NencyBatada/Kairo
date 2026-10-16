using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms;

public sealed class CampaignRoomsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CampaignRoomsDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, CampaignRoomsDbContext.SchemaName));
        services.AddCampaignRooms();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapCampaignRooms();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.MapStageJobs();
        jobs.MapTestEndJobs();
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CampaignRoomsDbContext>().Database.MigrateAsync(cancellationToken);
}
