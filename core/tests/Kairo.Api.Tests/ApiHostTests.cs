using System.Net;
using Kairo.SharedKernel;
using Kairo.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Kairo.Api.Tests;

public sealed class ApiHostTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Alive_endpoint_reports_healthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Health_endpoint_reports_only_the_status_when_dependencies_are_down()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Registers_every_module()
    {
        var registered = factory.Services.GetServices<IModule>()
            .Select(module => module.GetType().Assembly.GetName().Name!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ModuleFolders.Names, registered);
    }

    [Fact]
    public void Registers_redis_when_a_connection_string_is_set()
    {
        Assert.NotNull(factory.Services.GetService<IConnectionMultiplexer>());
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", "Server=127.0.0.1,1;Database=kairo;User Id=test;Password=test;Connect Timeout=1;ConnectRetryCount=0;TrustServerCertificate=true");
        builder.UseSetting("ConnectionStrings:redis", "127.0.0.1:1,abortConnect=false,connectRetry=0,connectTimeout=1000");
    }
}

public sealed class ApiWithoutRedisTests(ApiWithoutRedisFactory factory) : IClassFixture<ApiWithoutRedisFactory>
{
    [Fact]
    public async Task Starts_without_redis()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(factory.Services.GetService<IConnectionMultiplexer>());
    }
}

public sealed class ApiWithoutRedisFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", "Server=127.0.0.1,1;Database=kairo;User Id=test;Password=test;Connect Timeout=1;ConnectRetryCount=0;TrustServerCertificate=true");
    }
}
