using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.CampaignRooms.Data;

internal sealed class CampaignRoomsDbContextFactory : IDesignTimeDbContextFactory<CampaignRoomsDbContext>
{
    public CampaignRoomsDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<CampaignRoomsDbContext>(
            CampaignRoomsDbContext.SchemaName,
            (options, database) => new CampaignRoomsDbContext(options, database));
}
