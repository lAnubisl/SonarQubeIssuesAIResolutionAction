using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Services;

public sealed class ActionSummary(IEffortCalculator effortCalculator) : IRunSummary
{
    private readonly List<IPullRequestResult> _pullRequestSummaries = [];

    public int IssuesFound { get; private set; }
    public int IssuesSelected { get; private set; }
    public int RuleGroupsSelected { get; private set; }
    public string TotalEffortSaved { get; private set; } = "not available";
    public IReadOnlyList<IPullRequestResult> PullRequestSummaries => _pullRequestSummaries;

    public IReadOnlyList<string> GetChangedFiles() =>
        _pullRequestSummaries
            .SelectMany(result => result.ChangedFiles)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> GetPullRequestUrls() =>
        _pullRequestSummaries
            .Where(result => !string.IsNullOrWhiteSpace(result.PullRequestUrl))
            .Select(result => result.PullRequestUrl)
            .ToArray();

    public void RecordIssues(int totalFound, IReadOnlyList<SonarIssue> selectedIssues)
    {
        IssuesFound = totalFound;
        IssuesSelected = selectedIssues.Count;
        RuleGroupsSelected = selectedIssues
            .Select(issue => issue.RuleKey)
            .Distinct(StringComparer.Ordinal)
            .Count();
        TotalEffortSaved = effortCalculator.CalculateTotal(selectedIssues);
    }

    public void Add(IPullRequestResult result) =>
        _pullRequestSummaries.Add(result);
}
