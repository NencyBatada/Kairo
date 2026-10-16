using Kairo.Contracts.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;
using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Launch;

public sealed class LaunchModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(LaunchGoogle.ClientName, client => client.Timeout = TimeSpan.FromSeconds(90)).RemoveAllResilienceHandlers().RemoveAllLoggers();
        services.AddDbContext<LaunchDbContext>((provider, options) => options.UseKairoDatabase(provider, LaunchDbContext.SchemaName));
        services.AddScoped<ILaunchQueue, LaunchQueue>();
        services.AddScoped<LaunchGoogle>();
        services.AddScoped<LaunchPreflight>();
        services.AddScoped<YouTubeUpload>();
        services.AddScoped<LaunchRunner>();
        services.AddScoped<LaunchFailureRecorder>();
        services.AddScoped<LaunchDispatcher>();
        services.AddScoped<LaunchReconciler>();
        services.AddScoped<ILaunchRecovery, LaunchRecovery>();
        services.AddScoped<LaunchHealthCheck>();
        services.AddScoped<LaunchHealthWorker>();
        services.AddSingleton<LaunchLoopSwitch>();
        services.AddHostedService<LaunchLoop>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        var campaigns = api.MapBrandApi().MapGroup("/campaigns");
        campaigns.MapGet("/{campaignId:guid}/launch", LaunchEndpoints.GetAsync);
        campaigns.MapPost("/{campaignId:guid}/launch/retry", LaunchEndpoints.RetryAsync).RequireBrandRole(BrandRoles.Marketer);
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.ServiceProvider.GetRequiredService<LaunchLoopSwitch>().Enabled = true;
        jobs.MapPost("/launches/run", async (LaunchDispatcher dispatcher, IHostApplicationLifetime lifetime) =>
            TypedResults.Ok(new { processed = await dispatcher.RunDueAsync(lifetime.ApplicationStopping) }));
        jobs.MapPost("/launches/health", async (LaunchHealthCheck check, IHostApplicationLifetime lifetime) =>
            TypedResults.Ok(new { processed = await check.RunDueAsync(lifetime.ApplicationStopping) }));
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<LaunchDbContext>().Database.MigrateAsync(cancellationToken);
}
