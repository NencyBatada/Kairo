using System.Text.RegularExpressions;
using Kairo.Contracts.Audit;
using Kairo.Testing;

namespace Kairo.ArchitectureTests;

public sealed partial class AuditActionCatalogTests
{
    private static readonly IReadOnlyList<(string File, string Argument)> Recorded = FindRecordedActions();

    [Fact]
    public void Modules_record_audit_actions()
    {
        Assert.NotEmpty(Recorded);
    }

    [Fact]
    public void Every_recorded_action_is_a_literal_in_the_catalog()
    {
        var problems = new List<string>();
        foreach (var (file, argument) in Recorded)
        {
            var literals = Literal().Matches(argument).Select(match => match.Groups["value"].Value).ToList();
            if (literals.Count == 0)
            {
                problems.Add($"{file}: name the audit action with a string literal, not {argument.Trim()}.");
            }

            problems.AddRange(literals
                .Where(action => !AuditActions.IsKnown(action))
                .Select(action => $"{file}: add \"{action}\" and its label to AuditActions."));
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Every_catalog_action_is_recorded_somewhere()
    {
        var recorded = Recorded
            .SelectMany(item => Literal().Matches(item.Argument).Select(match => match.Groups["value"].Value))
            .ToHashSet(StringComparer.Ordinal);

        var unused = AuditActions.All.Select(pair => pair.Key).Where(action => !recorded.Contains(action)).ToList();

        Assert.True(unused.Count == 0, $"Nothing records {string.Join(", ", unused)}.");
    }

    [Fact]
    public void Catalog_actions_are_unique_and_labelled()
    {
        Assert.Equal(AuditActions.All.Count, AuditActions.All.Select(pair => pair.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(AuditActions.All, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value)));
    }

    private static List<(string File, string Argument)> FindRecordedActions() =>
        Directory.EnumerateFiles(ModuleFolders.ModulesDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(path => NewRecord().Matches(File.ReadAllText(path))
                .Select(match => (Path.GetFileName(path), match.Groups["action"].Value)))
            .ToList();

    [GeneratedRegex(@"new\s+AuditRecord\s*\(\s*(?<action>[^,]+),")]
    private static partial Regex NewRecord();

    [GeneratedRegex("\"(?<value>[^\"]+)\"")]
    private static partial Regex Literal();
}
