namespace Kairo.Testing;

internal static class ModuleFolders
{
    public static string ModulesDirectory { get; } = FindModulesDirectory();

    public static IReadOnlyList<string> Names { get; } = Directory.GetDirectories(ModulesDirectory, "Kairo.Modules.*")
        .Select(path => Path.GetFileName(path))
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static string ProjectFile(string name) => Path.Combine(ModulesDirectory, name, $"{name}.csproj");

    private static string FindModulesDirectory()
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

        return Path.Combine(dir.FullName, "src", "Modules");
    }
}
