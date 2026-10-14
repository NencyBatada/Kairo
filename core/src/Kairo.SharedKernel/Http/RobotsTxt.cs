using System.Text.RegularExpressions;

namespace Kairo.SharedKernel.Http;

public sealed class RobotsTxt
{
    public const string Robot = "kairobot";

    private readonly IReadOnlyList<Rule> _rules;

    private RobotsTxt(IReadOnlyList<Rule> rules) => _rules = rules;

    public static RobotsTxt AllowAll { get; } = new([]);

    public static RobotsTxt Parse(string text)
    {
        var groups = new List<Group>();
        Group? current = null;
        var readingAgents = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (key == "user-agent")
            {
                if (!readingAgents || current is null)
                {
                    current = new Group();
                    groups.Add(current);
                }

                current.Agents.Add(value.ToLowerInvariant());
                readingAgents = true;
                continue;
            }

            readingAgents = false;
            if ((key == "allow" || key == "disallow") && current is not null && value.Length > 0)
            {
                current.Rules.Add(new Rule(key == "allow", Compile(value), value.Length));
            }
        }

        var ours = groups.Where(group => group.Agents.Contains(Robot)).ToList();
        var chosen = ours.Count > 0 ? ours : groups.Where(group => group.Agents.Contains("*")).ToList();
        return new RobotsTxt(chosen.SelectMany(group => group.Rules).ToList());
    }

    public bool Allows(string pathAndQuery)
    {
        var best = _rules
            .Where(rule => rule.Pattern.IsMatch(pathAndQuery))
            .OrderByDescending(rule => rule.Length)
            .ThenByDescending(rule => rule.Allow)
            .FirstOrDefault();
        return best is null || best.Allow;
    }

    private static Regex Compile(string path)
    {
        var anchored = path.EndsWith('$');
        var body = Regex.Escape(anchored ? path[..^1] : path).Replace("\\*", ".*", StringComparison.Ordinal);
        return new Regex($"^{body}{(anchored ? "$" : "")}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private sealed class Group
    {
        public List<string> Agents { get; } = [];

        public List<Rule> Rules { get; } = [];
    }

    private sealed record Rule(bool Allow, Regex Pattern, int Length);
}
