using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Kairo.Modules.Brands.Connections;

internal sealed record SealedToken(byte[] Ciphertext, string KeyVersion);

internal interface ITokenCipher
{
    Task<SealedToken> SealAsync(string token, string context, CancellationToken cancellationToken);

    Task<string> OpenAsync(byte[] ciphertext, string context, CancellationToken cancellationToken);
}

internal sealed class DataProtectionTokenCipher(IDataProtectionProvider dataProtection) : ITokenCipher
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Kairo.Connections.Tokens");

    public Task<SealedToken> SealAsync(string token, string context, CancellationToken cancellationToken) =>
        Task.FromResult(new SealedToken(_protector.CreateProtector(context).Protect(Encoding.UTF8.GetBytes(token)), "data-protection"));

    public Task<string> OpenAsync(byte[] ciphertext, string context, CancellationToken cancellationToken) =>
        Task.FromResult(Encoding.UTF8.GetString(_protector.CreateProtector(context).Unprotect(ciphertext)));
}
