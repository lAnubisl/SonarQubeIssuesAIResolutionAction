using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Services;

public sealed class PullRequestSummary : IPullRequestResult
{
    public PullRequestSummary(
        IEffortCalculator effortCalculator,
        IssueGroup issueGroup,
        string baseBranch,
        string generatedBranch,
        IReadOnlyList<string> changedFiles,
        string copilotSessionSummary)
    {
        IssueGroup = issueGroup;
        BaseBranch = baseBranch;
        GeneratedBranch = generatedBranch;
        ChangedFiles = changedFiles;
        CopilotSessionSummary = copilotSessionSummary;
        TotalEffortSaved = effortCalculator.CalculateTotal(IssueGroup.Issues);
    }

    public IssueGroup IssueGroup { get; }
    public string BaseBranch { get; }
    public string GeneratedBranch { get; }
    public IReadOnlyList<string> ChangedFiles { get; }
    public string CopilotSessionSummary { get; }
    public string TotalEffortSaved { get; }
    public string PullRequestUrl { get; set; } = string.Empty;
}
