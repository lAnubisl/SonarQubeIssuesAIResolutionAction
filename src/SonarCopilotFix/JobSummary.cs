using System.Globalization;
using System.Text.RegularExpressions;
using SonarCopilotFix.Models;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix;

public sealed partial class JobSummary
{
    private const long MinutesPerSonarDay = 8 * 60;

    [GeneratedRegex(
        @"^\s*(?:(?<days>\d+)d)?\s*(?:(?<hours>\d+)h)?\s*(?:(?<minutes>\d+)min)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex GetEffortPattern();

    public int IssuesFound { get; set; }
    public int IssuesSelected { get; set; }
    public int RuleGroupsSelected { get; private set; }
    public string TotalEffortSaved { get; private set; } = "not available";
    public string? BaseBranch { get; set; }
    public string? GeneratedBranch { get; set; }
    public IReadOnlyList<string> ChangedFiles { get; set; } = [];
    public string? PullRequestUrl { get; set; }
    public string? CopilotSessionSummary { get; set; }
    public List<GroupRunResult> GroupResults { get; } = [];
    public IReadOnlyList<string> GetPullRequestUrls() =>
        GroupResults
            .Where(result => !string.IsNullOrWhiteSpace(result.PullRequestUrl))
            .Select(result => result.PullRequestUrl!)
            .ToArray();

    public void SetSelectedIssues(IReadOnlyList<SonarIssue> issues)
    {
        IssuesSelected = issues.Count;
        RuleGroupsSelected = issues
            .Select(issue => issue.RuleKey)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var efforts = issues
            .Select(issue => ParseEffortMinutes(issue.Effort))
            .Where(minutes => minutes.HasValue)
            .Select(minutes => minutes!.Value)
            .ToArray();

        TotalEffortSaved = efforts.Length == 0
            ? "not available"
            : FormatEffort(efforts.Sum());
    }

    public void AddGroupResult(GroupRunResult result)
    {
        GroupResults.Add(result);
        GeneratedBranch = result.BranchName;
        PullRequestUrl = result.PullRequestUrl;
        CopilotSessionSummary = result.CopilotSessionSummary;
        ChangedFiles = ChangedFiles
            .Concat(result.ChangedFiles)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static long? ParseEffortMinutes(string? effort)
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

    private static string FormatEffort(long totalMinutes)
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
