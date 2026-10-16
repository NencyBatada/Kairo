using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Checks;
using Kairo.Modules.CampaignRooms.ProofPlans;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Data;

internal sealed class CampaignRoomsDbContext(DbContextOptions<CampaignRoomsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "campaign_rooms";

    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<StageRun> StageRuns => Set<StageRun>();

    public DbSet<Brief> Briefs => Set<Brief>();

    public DbSet<ProofPlan> ProofPlans => Set<ProofPlan>();
    public DbSet<BriefProposal> BriefProposals => Set<BriefProposal>();

    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<AssetVersion> AssetVersions => Set<AssetVersion>();

    public DbSet<CheckSettings> CheckSettings => Set<CheckSettings>();

    public DbSet<CheckResult> CheckResults => Set<CheckResult>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>(room =>
        {
            room.ToTable("Rooms");
            room.HasKey(r => r.Id).IsClustered(false);
            room.HasIndex(r => new { r.BrandId, r.CreatedAt }).IsClustered();
            room.HasIndex(r => new { r.BrandId, r.OpportunityId }).IsUnique();
            room.HasIndex(r => new { r.BrandId, r.State });
            room.HasIndex(r => new { r.BrandId, r.Status });
            room.Property(r => r.Title).HasMaxLength(200);
            room.Property(r => r.State).HasMaxLength(20);
            room.Property(r => r.Status).HasMaxLength(20);
            room.Property(r => r.PendingGate).HasMaxLength(20);
            room.Property(r => r.GateRoute).HasMaxLength(20);
            room.Property(r => r.RunStatus).HasMaxLength(20);
            room.Property(r => r.DiscardedFrom).HasMaxLength(20);
            room.Property(r => r.DiscardReason).HasMaxLength(RoomLimits.MaxReasonLength);
            room.OwnsMany(r => r.Products, product => product.ToJson());
            room.OwnsOne(r => r.LastSendBack, sendBack => sendBack.ToJson());
        });

        modelBuilder.Entity<StageRun>(run =>
        {
            run.ToTable("StageRuns");
            run.HasKey(r => r.Id).IsClustered(false);
            run.HasIndex(r => new { r.BrandId, r.RoomId, r.QueuedAt }).IsClustered();
            run.HasIndex(r => new { r.Status, r.NextAttemptAt });
            run.Property(r => r.Stage).HasMaxLength(20);
            run.Property(r => r.Agent).HasMaxLength(20);
            run.Property(r => r.Kind).HasMaxLength(40);
            run.Property(r => r.Status).HasMaxLength(20);
            run.Property(r => r.Model).HasMaxLength(100);
            run.Property(r => r.PromptVersion).HasMaxLength(100);
            run.Property(r => r.ErrorCode).HasMaxLength(40);
            run.Property(r => r.ErrorMessage).HasMaxLength(500);
        });

        modelBuilder.Entity<Brief>(brief =>
        {
            brief.ToTable("Briefs");
            brief.HasKey(b => b.Id).IsClustered(false);
            brief.HasIndex(b => new { b.BrandId, b.RoomId, b.Version }).IsUnique().IsClustered();
            brief.Property(b => b.Origin).HasMaxLength(20);
            brief.Property(b => b.Model).HasMaxLength(100);
            brief.Property(b => b.PromptVersion).HasMaxLength(100);
            brief.Property(b => b.Instruction).HasMaxLength(BriefLimits.Instruction);
            brief.OwnsOne(b => b.Content, Content);
            brief.Navigation(b => b.Content).IsRequired();
        });

        modelBuilder.Entity<ProofPlan>(plan =>
        {
            plan.ToTable("ProofPlans");
            plan.HasKey(p => p.Id).IsClustered(false);
            plan.HasIndex(p => new { p.BrandId, p.RoomId, p.Version }).IsUnique().IsClustered();
            plan.Property(p => p.Method).HasMaxLength(100);
            plan.Property(p => p.ChangeReason).HasMaxLength(RoomLimits.MaxReasonLength);
        });
        modelBuilder.Entity<BriefProposal>(proposal =>
        {
            proposal.ToTable("BriefProposals");
            proposal.HasKey(p => p.Id).IsClustered(false);
            proposal.HasIndex(p => new { p.BrandId, p.RoomId, p.CreatedAt }).IsClustered();
            proposal.Property(p => p.Instruction).HasMaxLength(BriefLimits.Instruction);
            proposal.Property(p => p.Summary).HasMaxLength(500);
            proposal.Property(p => p.Model).HasMaxLength(100);
            proposal.Property(p => p.PromptVersion).HasMaxLength(100);
            proposal.OwnsOne(p => p.Content, Content);
            proposal.Navigation(p => p.Content).IsRequired();
        });

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.ToTable("Assets");
            asset.HasKey(a => a.Id).IsClustered(false);
            asset.HasIndex(a => new { a.BrandId, a.RoomId, a.Key }).IsUnique().IsClustered();
            asset.Property(a => a.Key).HasMaxLength(AssetLimits.Key);
            asset.Property(a => a.Kind).HasMaxLength(40);
            asset.Property(a => a.CampaignType).HasMaxLength(20);
            asset.Property(a => a.Language).HasMaxLength(10);
            asset.Property(a => a.AdGroup).HasMaxLength(100);
            asset.Property(a => a.Format).HasMaxLength(20);
            asset.Property(a => a.CheckState).HasMaxLength(20);
            asset.Property(a => a.Status).HasMaxLength(20);
            asset.Property(a => a.DurationSeconds).HasPrecision(9, 3);
        });

        modelBuilder.Entity<AssetVersion>(version =>
        {
            version.ToTable("AssetVersions");
            version.HasKey(v => v.Id).IsClustered(false);
            version.HasIndex(v => new { v.BrandId, v.RoomId, v.AssetId, v.Version }).IsUnique().IsClustered();
            version.Property(v => v.Origin).HasMaxLength(20);
            version.Property(v => v.Instruction).HasMaxLength(AssetLimits.Instruction);
            version.Property(v => v.Model).HasMaxLength(100);
            version.Property(v => v.PromptVersion).HasMaxLength(100);
        });

        modelBuilder.Entity<CheckSettings>(settings =>
        {
            settings.ToTable("CheckSettings");
            settings.HasKey(s => s.BrandId);
            settings.OwnsMany(s => s.Rules, rule => rule.ToJson());
            settings.OwnsMany(s => s.CustomRules, rule => rule.ToJson());
        });

        modelBuilder.Entity<CheckResult>(result =>
        {
            result.ToTable("CheckResults");
            result.HasKey(r => r.Id).IsClustered(false);
            result.HasIndex(r => new { r.BrandId, r.RoomId, r.AssetId, r.AssetVersion, r.CreatedAt }).IsClustered();
            result.Property(r => r.Model).HasMaxLength(100);
            result.Property(r => r.PromptVersion).HasMaxLength(100);
            result.Property(r => r.OverrideReason).HasMaxLength(CheckResultLimits.MaxReason);
        });
    }

    private static void Content<TOwner>(OwnedNavigationBuilder<TOwner, BriefContent> content)
        where TOwner : class
    {
        content.ToJson();
        content.OwnsOne(c => c.Budget);
        content.OwnsMany(c => c.Reasons, reason => reason.OwnsMany(r => r.Evidence));
    }
}
