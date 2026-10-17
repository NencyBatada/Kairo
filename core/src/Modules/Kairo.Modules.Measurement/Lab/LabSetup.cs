using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.Lab.Sources;
using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Measurement.Lab;

internal sealed record LabSweep(int Ran);

internal static class LabSetup
{
    public const int MostRounds = 10;

    public static void AddLab(this IServiceCollection services)
    {
        services.AddScoped<ILabSource, NightlySource>();
        services.AddScoped<ILabSource, Ga4ItemSource>();
        services.AddScoped<ILabSource, AdsProductSource>();
        services.AddScoped<ILabSource, BrandTableSource>();
        services.AddScoped<ILabSource, FileSource>();
        services.AddScoped<LabWork>();
        services.AddScoped<LabRunner>();
        services.AddScoped<LabDispatcher>();
        services.AddSingleton<LabLoopSwitch>();
        services.AddHostedService<LabLoop>();
    }

    public static void MapLab(this IEndpointRouteBuilder api)
    {
        api.ServiceProvider.GetRequiredService<LabLoopSwitch>().TurnOn();
        var lab = api.MapBrandApi().MapGroup("/impact-lab/analyses");
        var editors = BrandRoles.Admin | BrandRoles.Marketer;
        lab.MapGet("", LabEndpoints.ListAsync);
        lab.MapGet("/{analysisId:guid}", LabEndpoints.GetAsync);
        lab.MapPost("", LabEndpoints.CreateAsync).RequireBrandRole(editors);
        lab.MapPut("/{analysisId:guid}", LabEndpoints.UpdateAsync).RequireBrandRole(editors);
        lab.MapPost("/{analysisId:guid}/file", LabEndpoints.UploadAsync)
            .RequireBrandRole(editors)
            .WithMetadata(new UploadSizeLimit(SalesEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: SalesEndpoints.MaxRequestBytes)
            .DisableAntiforgery();
        lab.MapPost("/{analysisId:guid}/load", LabEndpoints.LoadAsync).RequireBrandRole(editors).WithoutRequestTransaction();
        lab.MapPut("/{analysisId:guid}/selection", LabEndpoints.SelectAsync).RequireBrandRole(editors).WithoutRequestTransaction();
        lab.MapPost("/{analysisId:guid}/run", LabEndpoints.RunAsync).RequireBrandRole(editors);
        lab.MapPost("/{analysisId:guid}/memory", LabEndpoints.RememberAsync).RequireBrandRole(editors);
    }

    public static void MapLabJobs(this IEndpointRouteBuilder jobs) =>
        jobs.MapPost("/impact-lab/run", async (LabDispatcher dispatcher, IHostApplicationLifetime lifetime) =>
        {
            var ran = 0;
            for (var round = 0; round < MostRounds; round++)
            {
                var claimed = await dispatcher.RunDueAsync(lifetime.ApplicationStopping);
                if (claimed == 0)
                {
                    break;
                }

                ran += claimed;
            }

            return TypedResults.Ok(new LabSweep(ran));
        });
}
