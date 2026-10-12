namespace Kairo.Testing;

internal static class ModuleFolders
{
    public static IReadOnlyList<string> Names { get; } = Find();

    private static string[] Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kairo.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException($"No Kairo.slnx found above {AppContext.BaseDirectory}.");
        }

        return Directory.GetDirectories(Path.Combine(dir.FullName, "src", "Modules"), "Kairo.Modules.*")
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
