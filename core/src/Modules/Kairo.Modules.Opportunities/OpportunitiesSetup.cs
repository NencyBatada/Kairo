using Kairo.Contracts.Opportunities;
using Kairo.Modules.Opportunities.Radar;
using Kairo.Modules.Opportunities.Refresh;
using Kairo.Modules.Opportunities.Sense;
using Kairo.Modules.Opportunities.Topics;
using Kairo.Modules.Opportunities.Triggered;

namespace Kairo.Modules.Opportunities;

internal static class OpportunitiesSetup
{
    public static void AddOpportunities(this IServiceCollection services)
    {
        services.AddHttpClient(AdsHttp.ClientName, client => client.Timeout = AdsHttp.Timeout).RemoveAllResilienceHandlers();
        services.AddSingleton<OpportunityOptions>();
        services.AddSingleton<KeywordPlanner>();
        services.AddSingleton<SenseAgent>();
        services.AddSingleton<RefreshRunner>();
        services.AddScoped<BrandRefresher>();
        services.AddScoped<OpportunityReader>();
        services.AddScoped<IOpportunities, TriggeredOpportunities>();
    }

    public static void MapOpportunities(this IEndpointRouteBuilder api)
    {
        var opportunities = api.MapBrandApi().MapGroup("/opportunities");
        opportunities.MapGet("", OpportunityEndpoints.ListAsync);

        var topics = opportunities.MapGroup("/topics");
        topics.MapGet("", TopicEndpoints.ListAsync);
        topics.MapPost("", TopicEndpoints.AddAsync).RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer);
        topics.MapDelete("/{topicId:guid}", TopicEndpoints.RemoveAsync).RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer);

        opportunities.MapGet("/{opportunityId:guid}", OpportunityEndpoints.GetAsync);
        var decisions = opportunities.MapGroup("/{opportunityId:guid}").RequireBrandRole(BrandRoles.Marketer);
        decisions.MapPost("/accept", OpportunityEndpoints.AcceptAsync);
        decisions.MapPost("/save", OpportunityEndpoints.SaveAsync);
        decisions.MapPost("/unsave", OpportunityEndpoints.UnsaveAsync);
        decisions.MapPost("/dismiss", OpportunityEndpoints.DismissAsync);
    }
}
