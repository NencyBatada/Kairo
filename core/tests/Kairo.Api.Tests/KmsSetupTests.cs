using Kairo.Contracts.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.Api.Tests;

public sealed class KmsSetupTests
{
    private const string KeyName = "projects/kairo-test/locations/asia-south1/keyRings/kairo/cryptoKeys/connection-tokens";

    [Fact]
    public async Task A_configured_key_makes_connections_use_Cloud_KMS_without_calling_it_at_startup()
    {
        await using var factory = new ApiFactory().WithWebHostBuilder(builder => builder.UseSetting("Kairo:Kms:KeyName", KeyName));
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IConnections>());
        Assert.Equal("KmsTokenCipher", Cipher(scope.ServiceProvider).GetType().Name);
    }

    [Fact]
    public async Task Without_a_key_connections_use_Data_Protection()
    {
        await using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.Equal("DataProtectionTokenCipher", Cipher(scope.ServiceProvider).GetType().Name);
    }

    [Fact]
    public async Task Outside_Development_the_API_refuses_to_start_without_a_key()
    {
        await using var factory = new ApiFactory().WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Record.Exception(() => factory.Services);

        Assert.NotNull(exception);
        Assert.Contains("Kairo:Kms:KeyName is not set", Flatten(exception));
    }

    [Fact]
    public async Task Outside_Development_the_API_starts_with_a_key()
    {
        await using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production").UseSetting("Kairo:Kms:KeyName", KeyName));
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.Equal("KmsTokenCipher", Cipher(scope.ServiceProvider).GetType().Name);
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} {Flatten(exception.InnerException)}";

    private static object Cipher(IServiceProvider services) =>
        services.GetRequiredService(Type.GetType("Kairo.Modules.Brands.Connections.ITokenCipher, Kairo.Modules.Brands", throwOnError: true)!);
}
