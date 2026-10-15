using Kairo.Contracts.Triggers;
using Kairo.Modules.Triggers.Checks;
using Kairo.Modules.Triggers.Festivals;
using Kairo.Modules.Triggers.Regions;
using Kairo.Modules.Triggers.Rules;
using Kairo.Modules.Triggers.Weather;

namespace Kairo.Modules.Triggers;

internal static class TriggersSetup
{
    public static void AddTriggers(this IServiceCollection services)
    {
        services.AddHttpClient(WeatherClient.ClientName, client => client.Timeout = WeatherClient.Timeout).RemoveAllResilienceHandlers();
        services.AddSingleton<TriggerSettings>();
        services.AddSingleton<IWeatherToken, ApplicationDefaultWeatherToken>();
        services.AddSingleton<WeatherClient>();
        services.AddSingleton<CheckRunner>();
        services.AddScoped<ForecastCache>();
        services.AddScoped<FestivalCalendar>();
        services.AddScoped<BrandChecker>();
        services.AddScoped<ITriggerSuggestions, TriggerSuggestions>();
        services.AddSingleton<IRegionPlaces, RegionPlaces>();
    }

    public static void MapTriggers(this IEndpointRouteBuilder api)
    {
        var triggers = api.MapBrandApi().MapGroup("/triggers");
        triggers.MapGet("/options", RuleEndpoints.OptionsAsync);

        var rules = triggers.MapGroup("/rules");
        rules.MapGet("", RuleEndpoints.ListAsync);
        rules.MapPost("", RuleEndpoints.CreateAsync).RequireBrandRole(BrandRoles.Admin);
        rules.MapPost("/preview", RuleEndpoints.PreviewAsync).RequireBrandRole(BrandRoles.Admin);
        rules.MapGet("/{ruleId:guid}", RuleEndpoints.GetAsync);
        rules.MapPut("/{ruleId:guid}", RuleEndpoints.UpdateAsync).RequireBrandRole(BrandRoles.Admin);
        rules.MapDelete("/{ruleId:guid}", RuleEndpoints.DeleteAsync).RequireBrandRole(BrandRoles.Admin);
        rules.MapPost("/{ruleId:guid}/pause", RuleEndpoints.PauseAsync).RequireBrandRole(BrandRoles.Admin);
        rules.MapPost("/{ruleId:guid}/resume", RuleEndpoints.ResumeAsync).RequireBrandRole(BrandRoles.Admin);
        rules.MapPost("/{ruleId:guid}/activate", RuleEndpoints.ActivateAsync).RequireBrandRole(BrandRoles.Admin);

        var festivals = triggers.MapGroup("/festivals");
        festivals.MapGet("", FestivalEndpoints.ListAsync);
        festivals.MapPost("", FestivalEndpoints.AddAsync).RequireBrandRole(BrandRoles.Admin);
        festivals.MapPut("/{festivalId:guid}", FestivalEndpoints.UpdateAsync).RequireBrandRole(BrandRoles.Admin);
        festivals.MapDelete("/{festivalId:guid}", FestivalEndpoints.RemoveAsync).RequireBrandRole(BrandRoles.Admin);
    }
}
