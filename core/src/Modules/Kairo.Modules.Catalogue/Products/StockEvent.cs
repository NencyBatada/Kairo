namespace Kairo.Modules.Catalogue.Products;

internal sealed class StockEvent : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid ProductId { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public Guid ImportId { get; set; }

    public DateTimeOffset At { get; set; }
}
