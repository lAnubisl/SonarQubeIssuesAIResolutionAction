namespace SonarCopilotFix.Models;

public sealed record GroupRunResult(
    string RuleKey,
    IReadOnlyList<string> IssueKeys,
    string? BranchName,
    string PromptFile,
    IReadOnlyList<string> ChangedFiles,
    string? PullRequestUrl,
    string? CopilotSessionSummary,
    string Outcome);
