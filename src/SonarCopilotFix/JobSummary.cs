using System.Globalization;
using System.Text.RegularExpressions;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix;

public sealed class JobSummary(IConfigurationHelper configurationHelper)
{
    private const long MinutesPerSonarDay = 8 * 60;
    private static readonly Regex EffortPattern = new(
        @"^\s*(?:(?<days>\d+)d)?\s*(?:(?<hours>\d+)h)?\s*(?:(?<minutes>\d+)min)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int IssuesFound { get; set; }
    public int IssuesSelected { get; set; }
    public string TotalEffortSaved { get; private set; } = "not available";
    public bool CopilotExecuted { get; set; }
    public string? PromptFile { get; set; }
    public string? BaseBranch { get; set; }
    public string? GeneratedBranch { get; set; }
    public IReadOnlyList<string> ChangedFiles { get; set; } = [];
    public string? PullRequestUrl { get; set; }
    public string? CopilotSessionSummary { get; set; }

    public void SetSelectedIssues(IReadOnlyList<SonarIssue> issues)
    {
        IssuesSelected = issues.Count;
        var efforts = issues
            .Select(issue => ParseEffortMinutes(issue.Effort))
            .Where(minutes => minutes.HasValue)
            .Select(minutes => minutes!.Value)
            .ToArray();

        TotalEffortSaved = efforts.Length == 0
            ? "not available"
            : FormatEffort(efforts.Sum());
    }

    public void Write()
    {
        var path = configurationHelper.GitHubStepSummary;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var lines = new List<string>
        {
            "# SonarQube Copilot Fix",
            "",
            $"* SonarQube project: `{configurationHelper.GetSonarProjectKey()}`",
            $"* SonarQube branch: `{configurationHelper.InputSonarBranch ?? "not specified"}`",
            $"* Issues found: `{IssuesFound}`",
            $"* Issues selected: `{IssuesSelected}`",
            $"* Total effort saved: `{TotalEffortSaved}`",
            $"* Dry run: `{configurationHelper.InputDryRun}`",
            $"* Copilot CLI executed: `{CopilotExecuted}`",
            "",
            "## Copilot Session Summary",
            "",
            "```text",
            string.IsNullOrWhiteSpace(CopilotSessionSummary)
                ? "Not available because Copilot CLI did not write session information to stderr."
                : CopilotSessionSummary,
            "```",
            "",
            "## Result",
            "",
            $"* Files changed: `{ChangedFiles.Count}`",
            $"* Pull request: `{PullRequestUrl ?? "not created"}`",
            $"* Prompt file: `{PromptFile ?? "not generated"}`"
        };

        File.AppendAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static long? ParseEffortMinutes(string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return null;
        }

        var match = EffortPattern.Match(effort);
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
