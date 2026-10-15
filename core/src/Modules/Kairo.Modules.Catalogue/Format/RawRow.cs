namespace Kairo.Modules.Catalogue.Format;

internal sealed class RawRow(int row)
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

    public int Row { get; } = row;

    public bool IsEmpty => _values.Count == 0;

    public void Add(string attribute, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (!_values.TryGetValue(attribute, out var values))
        {
            _values[attribute] = values = [];
        }

        values.Add(trimmed);
    }

    public string? First(string attribute) => _values.TryGetValue(attribute, out var values) ? values[0] : null;

    public IReadOnlyList<string> All(string attribute) => _values.TryGetValue(attribute, out var values) ? values : [];
}
