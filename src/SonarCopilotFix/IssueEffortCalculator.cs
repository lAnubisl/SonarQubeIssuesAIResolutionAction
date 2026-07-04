using System.Globalization;
using System.Text.RegularExpressions;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix;

internal static partial class IssueEffortCalculator
{
    private const long MinutesPerSonarDay = 8 * 60;

    [GeneratedRegex(
        @"^\s*(?:(?<days>\d+)d)?\s*(?:(?<hours>\d+)h)?\s*(?:(?<minutes>\d+)min)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex GetEffortPattern();

    public static string CalculateTotal(IReadOnlyList<SonarIssue> issues)
    {
        var efforts = issues
            .Select(issue => ParseMinutes(issue.Effort))
            .Where(minutes => minutes.HasValue)
            .Select(minutes => minutes!.Value)
            .ToArray();

        return efforts.Length == 0
            ? "not available"
            : Format(efforts.Sum());
    }

    private static long? ParseMinutes(string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return null;
        }

        var match = GetEffortPattern().Match(effort);
        if (!match.Success
            || (!match.Groups["days"].Success
                && !match.Groups["hours"].Success
                && !match.Groups["minutes"].Success))
        {
            return null;
        }

        return checked(
            ParsePart(match, "days") * MinutesPerSonarDay
            + ParsePart(match, "hours") * 60
            + ParsePart(match, "minutes"));
    }

    private static long ParsePart(Match match, string groupName) =>
        match.Groups[groupName].Success
            ? long.Parse(match.Groups[groupName].Value, CultureInfo.InvariantCulture)
            : 0;

    private static string Format(long totalMinutes)
    {
        var parts = new List<string>();
        var days = totalMinutes / MinutesPerSonarDay;
        var remainingMinutes = totalMinutes % MinutesPerSonarDay;
        var hours = remainingMinutes / 60;
        var minutes = remainingMinutes % 60;

        if (days > 0)
        {
            parts.Add($"{days}d");
        }

        if (hours > 0)
        {
            parts.Add($"{hours}h");
        }

        if (minutes > 0 || parts.Count == 0)
        {
            parts.Add($"{minutes}min");
        }

        return string.Join(" ", parts);
    }
}
