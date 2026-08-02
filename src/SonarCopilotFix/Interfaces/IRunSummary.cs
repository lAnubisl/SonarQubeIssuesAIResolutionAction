using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Interfaces;

public interface IRunSummary
{
    int IssuesFound { get; }
    int IssuesSelected { get; }
    int RuleGroupsSelected { get; }
    string TotalEffortSaved { get; }
    IReadOnlyList<IPullRequestResult> PullRequestSummaries { get; }

    IReadOnlyList<string> GetChangedFiles();
    IReadOnlyList<string> GetPullRequestUrls();
    void RecordIssues(int totalFound, IReadOnlyList<SonarIssue> selectedIssues);
    void Add(IPullRequestResult result);
}
