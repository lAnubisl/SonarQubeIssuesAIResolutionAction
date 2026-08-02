using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Interfaces;

public interface IPullRequestResult
{
    IssueGroup IssueGroup { get; }
    string BaseBranch { get; }
    string GeneratedBranch { get; }
    IReadOnlyList<string> ChangedFiles { get; }
    string CopilotSessionSummary { get; }
    string TotalEffortSaved { get; }
    string PullRequestUrl { get; set; }
}
