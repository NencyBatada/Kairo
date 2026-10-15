using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.ProofPlans;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms;

internal static class CampaignRoomsSetup
{
    public static void AddCampaignRooms(this IServiceCollection services)
    {
        services.AddScoped<RoomWorkflow>();
        services.AddScoped<CampaignReader>();
        services.AddScoped<StageQueue>();
        services.AddScoped<StageRunner>();
        services.AddScoped<StageDispatcher>();
        services.AddScoped<IStageWork, StarterBriefWork>();
        services.AddScoped<IStageWork, ProofPlanWork>();
        services.AddScoped<ICampaignRooms, RoomOpener>();
        services.AddScoped<ICampaignLifecycle, CampaignLifecycle>();
        services.AddSingleton<StageLoopSwitch>();
        services.AddHostedService<StageLoop>();
    }

    public static void MapCampaignRooms(this IEndpointRouteBuilder api)
    {
        api.ServiceProvider.GetRequiredService<StageLoopSwitch>().TurnOn();
        var campaigns = api.MapBrandApi().MapGroup("/campaigns");
        campaigns.MapGet("", CampaignEndpoints.ListAsync);
        campaigns.MapGet("/{campaignId:guid}", CampaignEndpoints.GetAsync);
        campaigns.MapGet("/{campaignId:guid}/activity", CampaignEndpoints.ActivityAsync);
        campaigns.MapPost("/{campaignId:guid}/approval-requests", CampaignEndpoints.RequestApprovalAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/approve", CampaignEndpoints.ApproveAsync).RequireBrandRole(BrandRoles.Approver);
        campaigns.MapPost("/{campaignId:guid}/send-back", CampaignEndpoints.SendBackAsync).RequireBrandRole(BrandRoles.Approver);
        campaigns.MapPost("/{campaignId:guid}/discard", CampaignEndpoints.DiscardAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapPost("/{campaignId:guid}/stage-runs/{runId:guid}/retry", CampaignEndpoints.RetryAsync).RequireBrandRole(BrandRoles.Marketer);
        campaigns.MapGet("/{campaignId:guid}/proof-plan", ProofPlanEndpoints.GetAsync);
        campaigns.MapPost("/{campaignId:guid}/proof-plan/holdouts", ProofPlanEndpoints.ChooseHoldoutsAsync).RequireBrandRole(BrandRoles.Marketer);
    }
}
