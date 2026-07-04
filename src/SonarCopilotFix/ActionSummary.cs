using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix;

public sealed class ActionSummary
{
    public int IssuesFound { get; set; }
    public int IssuesSelected { get; private set; }
    public int RuleGroupsSelected { get; private set; }
    public string TotalEffortSaved { get; private set; } = "not available";
    public List<PullRequestSummary> PullRequestSummaries { get; } = [];

    public IReadOnlyList<string> GetChangedFiles() =>
        PullRequestSummaries
            .SelectMany(result => result.ChangedFiles)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> GetPullRequestUrls() =>
        PullRequestSummaries
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
        TotalEffortSaved = IssueEffortCalculator.CalculateTotal(issues);
    }

    public void Add(PullRequestSummary summary) =>
        PullRequestSummaries.Add(summary);
}
