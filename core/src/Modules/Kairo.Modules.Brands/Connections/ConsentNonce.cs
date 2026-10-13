namespace Kairo.Modules.Brands.Connections;

internal sealed class ConsentNonce
{
    public string Nonce { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }
}
