using System.Security.Cryptography;
using Google.Cloud.Kms.V1;
using Google.Protobuf;
using Grpc.Core;

namespace Kairo.Modules.Brands.Connections;

internal sealed class KmsTokenCipher(string keyName) : ITokenCipher
{
    private readonly Lazy<Task<KeyManagementServiceClient>> _client = new(() => KeyManagementServiceClient.CreateAsync());

    public async Task<SealedToken> SealAsync(string token, string context, CancellationToken cancellationToken)
    {
        var client = await _client.Value;
        var response = await client.EncryptAsync(
            new EncryptRequest
            {
                Name = keyName,
                Plaintext = ByteString.CopyFromUtf8(token),
                AdditionalAuthenticatedData = ByteString.CopyFromUtf8(context),
            },
            cancellationToken);
        return new SealedToken(response.Ciphertext.ToByteArray(), response.Name);
    }

    public bool CanOpen(string? keyVersion) => keyVersion?.StartsWith($"{keyName}/cryptoKeyVersions/", StringComparison.Ordinal) == true;

    public async Task<string> OpenAsync(byte[] ciphertext, string context, CancellationToken cancellationToken)
    {
        var client = await _client.Value;
        try
        {
            var response = await client.DecryptAsync(
                new DecryptRequest
                {
                    Name = keyName,
                    Ciphertext = ByteString.CopyFrom(ciphertext),
                    AdditionalAuthenticatedData = ByteString.CopyFromUtf8(context),
                },
                cancellationToken);
            return response.Plaintext.ToStringUtf8();
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.InvalidArgument)
        {
            throw new CryptographicException("The token could not be decrypted.", exception);
        }
    }
}
