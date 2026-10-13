namespace Kairo.Modules.Identity.Users;

internal sealed class User
{
    public Guid Id { get; set; }

    public string GoogleSubject { get; set; } = "";

    public string Email { get; set; } = "";

    public string Name { get; set; } = "";

    public string? PictureUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSignedInAt { get; set; }
}
