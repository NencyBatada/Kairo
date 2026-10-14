using System.Net;
using Kairo.SharedKernel;
using Kairo.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.Worker.Tests;

public sealed class WorkerHostTests(WorkerFactory factory) : IClassFixture<WorkerFactory>
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
}

public sealed class WorkerKmsTests
{
    [Fact]
    public async Task Outside_Development_the_worker_refuses_to_start_without_a_key()
    {
        await using var factory = new WorkerFactory().WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Record.Exception(() => factory.Services);

        Assert.NotNull(exception);
        Assert.Contains("Kairo:Kms:KeyName is not set", Flatten(exception));
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} {Flatten(exception.InnerException)}";
}

public sealed class WorkerFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", "Server=127.0.0.1,1;Database=kairo;User Id=test;Password=test;Connect Timeout=1;ConnectRetryCount=0;TrustServerCertificate=true");
    }
}
