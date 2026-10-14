namespace Kairo.Modules.BrandKit.Kit;

internal enum VersionStatus
{
    Draft,
    Approved,
    Superseded,
    Discarded,
}

internal enum VersionOrigin
{
    Manual,
    Drafted,
    Restored,
}

internal sealed class KitVersion : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public int Number { get; set; }

    public VersionStatus Status { get; set; }

    public string Content { get; set; } = "{}";

    public int Revision { get; set; }

    public VersionOrigin Origin { get; set; }

    public int? RestoredFrom { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
}
