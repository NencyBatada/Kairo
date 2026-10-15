namespace Kairo.Modules.Triggers.Festivals;

internal sealed class FestivalEvent : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Name { get; set; } = "";

    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }

    public List<string> Regions { get; set; } = [];

    public string Origin { get; set; } = FestivalOrigins.Brand;

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class BrandTriggerState : IBrandOwned
{
    public Guid BrandId { get; set; }

    public DateTimeOffset FestivalsSeededAt { get; set; }
}

internal static class FestivalOrigins
{
    public const string Kairo = "kairo";
    public const string Brand = "brand";
}
