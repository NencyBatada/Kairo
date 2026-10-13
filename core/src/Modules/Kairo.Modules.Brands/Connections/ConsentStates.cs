using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kairo.Contracts.Connections;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.Modules.Brands.Connections;

internal sealed record ConsentState(
    Guid BrandId,
    ConnectedProduct Product,
    Guid UserId,
    string Verifier,
    string Nonce,
    DateTimeOffset ExpiresAt)
{
    public string Challenge() => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
}

internal sealed class ConsentStates(IDataProtectionProvider dataProtection, TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Kairo.Connections.Consent");

    public ConsentState Create(Guid brandId, ConnectedProduct product, Guid userId) =>
        new(brandId, product, userId, RandomText(32), RandomText(16), time.GetUtcNow() + Lifetime);

    public string Protect(ConsentState state) => _protector.Protect(JsonSerializer.Serialize(state));

    public ConsentState? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ConsentState>(_protector.Unprotect(value));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static string RandomText(int bytes) => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));
}
