using System.Security.Cryptography;

namespace Kairo.Modules.Brands.Connections;

internal sealed class ConnectionTokens(ITokenCipher cipher)
{
    public async Task SealAsync(Connection connection, string refreshToken, CancellationToken cancellationToken)
    {
        var sealedToken = await cipher.SealAsync(refreshToken, Context(connection), cancellationToken);
        connection.RefreshToken = sealedToken.Ciphertext;
        connection.KeyVersion = sealedToken.KeyVersion;
    }

    public async Task<string?> OpenAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (connection.RefreshToken is null)
        {
            return null;
        }

        try
        {
            return await cipher.OpenAsync(connection.RefreshToken, Context(connection), cancellationToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string Context(Connection connection) => $"{connection.BrandId}:{connection.Id}";
}
