using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections;

internal sealed class Connection : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public ConnectedProduct Product { get; set; }

    public ConnectionState State { get; set; }

    public string? ExternalId { get; set; }

    public string? DisplayName { get; set; }

    public string Scopes { get; set; } = "";

    public string GoogleSubject { get; set; } = "";

    public byte[]? RefreshToken { get; set; }

    public string? KeyVersion { get; set; }

    public Guid ConnectedBy { get; set; }

    public string ConnectedByName { get; set; } = "";

    public DateTimeOffset ConnectedAt { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    public string? ReconnectReason { get; set; }
}
