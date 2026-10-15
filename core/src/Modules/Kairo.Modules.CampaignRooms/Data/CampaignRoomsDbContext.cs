using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Data;

internal sealed class CampaignRoomsDbContext(DbContextOptions<CampaignRoomsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "campaign_rooms";

    public DbSet<Room> Rooms => Set<Room>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>(room =>
        {
            room.ToTable("Rooms");
            room.HasKey(r => r.Id).IsClustered(false);
            room.HasIndex(r => new { r.BrandId, r.CreatedAt }).IsClustered();
            room.HasIndex(r => new { r.BrandId, r.OpportunityId }).IsUnique();
            room.Property(r => r.Title).HasMaxLength(200);
            room.Property(r => r.State).HasMaxLength(20);
        });
    }
}
