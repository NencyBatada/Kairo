using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.SharedKernel;

public interface IModule
{
    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapApi(IEndpointRouteBuilder api);

    void MapJobs(IEndpointRouteBuilder jobs);

    Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken);
}
