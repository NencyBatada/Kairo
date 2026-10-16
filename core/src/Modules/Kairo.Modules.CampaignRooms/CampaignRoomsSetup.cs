using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Checks;
using Kairo.Modules.CampaignRooms.Media;
using Kairo.Modules.CampaignRooms.ProofPlans;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.Modules.CampaignRooms.LaunchPlans;
using Kairo.Contracts.Launch;

namespace Kairo.Modules.CampaignRooms;

internal static class CampaignRoomsSetup
{
    public static void AddCampaignRooms(this IServiceCollection services)
    {
        services.AddScoped<ApprovalSnapshots>();
        services.AddScoped<LaunchPlanDesk>();
        services.AddScoped<ILaunchPlans>(provider => provider.GetRequiredService<LaunchPlanDesk>());
        services.AddScoped<RoomWorkflow>();
        services.AddScoped<CampaignReader>();
        services.AddScoped<StageQueue>();
        services.AddScoped<StageRunner>();
        services.AddScoped<StageDispatcher>();
        services.AddScoped<IStageWork, ProofPlanWork>();
        services.AddScoped<BriefAgent>();
        services.AddScoped<MediaAgent>();
        services.AddScoped<CataloguePhotos>();
        services.AddScoped<MediaDesk>();
        services.AddScoped<VideoMaker>();
        services.AddScoped<BriefDesk>();
        services.AddScoped<BriefEditor>();
        services.AddScoped<BriefGate>();
        services.AddScoped<BriefWork>();
        services.AddScoped<IStageWork>(provider => provider.GetRequiredService<BriefWork>());
        services.AddScoped<IStageWork, StarterBriefAlias>();
        services.AddScoped<CopyAgent>();
        services.AddScoped<CopyDesk>();
        services.AddScoped<ProvenanceTrail>();
        services.AddScoped<AssetStore>();
        services.AddScoped<AssetEditor>();
        services.AddScoped<AssetApproval>();
        services.AddScoped<AssetHistory>();
        services.AddScoped<CheckSettingsStore>();
        services.AddScoped<AssetReworker>();
        services.AddScoped<CheckAgent>();
        services.AddScoped<CheckLedger>();
        services.AddScoped<CheckViews>();
        services.AddScoped<CheckReview>();
        services.AddScoped<IStageWork, CheckWork>();
        services.AddScoped<IStageWork, CheckFixWork>();
        services.AddScoped<IStageWork, CopyWork>();
        services.AddScoped<IStageWork, AssetRegenerateWork>();
        services.AddScoped<IAssetRegenerator, TextRegenerator>();
        services.AddScoped<IStageWork, ImagesWork>();
        services.AddScoped<IStageWork, VideoWork>();
        services.AddScoped<IAssetRegenerator, ImageRegenerator>();
        services.AddScoped<IAssetRegenerator, VideoRegenerator>();
        services.AddScoped<ICampaignRooms, RoomOpener>();
        services.AddScoped<ICampaignLifecycle, CampaignLifecycle>();
        services.AddScoped<ICampaignTests, CampaignTests>();
        services.AddScoped<ICampaignBriefs, CampaignBriefs>();
        services.AddScoped<TestEnds>();
        services.AddScoped<IProofPlans, ProofPlanDirectory>();
        services.AddScoped<IAssets, AssetDirectory>();
        services.AddSingleton<StageLoopSwitch>();
        services.AddHostedService<StageLoop>();
    }

    public static void MapCampaignRooms(this IEndpointRouteBuilder api)
    {
        api.ServiceProvider.GetRequiredService<StageLoopSwitch>().TurnOn();
        api.MapCheckSettings();
        var campaigns = api.MapBrandApi().MapGroup("/campaigns");
        campaigns.MapGet("/{campaignId:guid}/launch-plan", LaunchPlanEndpoints.GetAsync);
        campaigns.MapPut("/{campaignId:guid}/launch-plan", LaunchPlanEndpoints.UpdateAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapGet("", CampaignEndpoints.ListAsync);
        campaigns.MapGet("/{campaignId:guid}", CampaignEndpoints.GetAsync);
        campaigns.MapGet("/{campaignId:guid}/activity", CampaignEndpoints.ActivityAsync);
        campaigns.MapPost("/{campaignId:guid}/approval-requests", CampaignEndpoints.RequestApprovalAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/approve", CampaignEndpoints.ApproveAsync).RequireBrandRole(BrandRoles.Approver);
        campaigns.MapPost("/{campaignId:guid}/send-back", CampaignEndpoints.SendBackAsync).RequireBrandRole(BrandRoles.Approver);
        campaigns.MapPost("/{campaignId:guid}/discard", CampaignEndpoints.DiscardAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/end-test", CampaignEndpoints.EndTestAsync).RequireBrandRole(BrandRoles.Marketer | BrandRoles.Approver);
        campaigns.MapPost("/{campaignId:guid}/stage-runs/{runId:guid}/retry", CampaignEndpoints.RetryAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapGet("/{campaignId:guid}/proof-plan", ProofPlanEndpoints.GetAsync);
        campaigns.MapPost("/{campaignId:guid}/proof-plan/holdouts", ProofPlanEndpoints.ChooseHoldoutsAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/proof-plan/changes", ProofPlanEndpoints.ChangeAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapGet("/{campaignId:guid}/brief", BriefEndpoints.GetAsync);
        campaigns.MapPatch("/{campaignId:guid}/brief", BriefEndpoints.EditAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/brief/instructions", BriefEndpoints.ProposeAsync).RequireBrandRole(BrandRoles.Marketer).WithoutRequestTransaction();
        campaigns.MapPost("/{campaignId:guid}/brief/proposals/{proposalId:guid}/apply", BriefEndpoints.ApplyAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapGet("/{campaignId:guid}/assets", AssetEndpoints.ListAsync);
        campaigns.MapPatch("/{campaignId:guid}/assets/{assetId:guid}", AssetEndpoints.EditAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/assets/{assetId:guid}/lock", AssetEndpoints.LockAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/assets/{assetId:guid}/unlock", AssetEndpoints.UnlockAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/assets/{assetId:guid}/regenerate", AssetEndpoints.RegenerateAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapGet("/{campaignId:guid}/assets/{assetId:guid}/file", MediaEndpoints.FileAsync);
        campaigns.MapGet("/{campaignId:guid}/assets/{assetId:guid}/history", AssetEndpoints.HistoryAsync);
        campaigns.MapPost("/{campaignId:guid}/assets/skip-video", MediaEndpoints.SkipVideoAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/assets/send-back", CheckEndpoints.SendBackAsync).RequireBrandRole(BrandRoles.Approver);
        campaigns.MapPost("/{campaignId:guid}/assets/check", CheckEndpoints.RequestCheckAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/assets/{assetId:guid}/override", CheckEndpoints.OverrideAsync).RequireBrandRole(BrandRoles.Approver);
    }
}
