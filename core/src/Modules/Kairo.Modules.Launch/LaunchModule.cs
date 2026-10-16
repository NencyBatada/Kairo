using Kairo.Contracts.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;
using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Launch;

public sealed class LaunchModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(LaunchGoogle.ClientName, client => client.Timeout = TimeSpan.FromSeconds(90)).RemoveAllResilienceHandlers();
        services.AddDbContext<LaunchDbContext>((provider, options) => options.UseKairoDatabase(provider, LaunchDbContext.SchemaName));
        services.AddScoped<ILaunchQueue, LaunchQueue>();
        services.AddScoped<LaunchGoogle>();
        services.AddScoped<LaunchPreflight>();
        services.AddScoped<YouTubeUpload>();
        services.AddScoped<LaunchRunner>();
        services.AddScoped<LaunchFailureRecorder>();
        services.AddScoped<LaunchDispatcher>();
        services.AddSingleton<LaunchLoopSwitch>();
        services.AddHostedService<LaunchLoop>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.ServiceProvider.GetRequiredService<LaunchLoopSwitch>().Enabled = true;
        jobs.MapPost("/launches/run", async (LaunchDispatcher dispatcher, IHostApplicationLifetime lifetime) =>
            TypedResults.Ok(new { processed = await dispatcher.RunDueAsync(lifetime.ApplicationStopping) }));
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<LaunchDbContext>().Database.MigrateAsync(cancellationToken);
}
