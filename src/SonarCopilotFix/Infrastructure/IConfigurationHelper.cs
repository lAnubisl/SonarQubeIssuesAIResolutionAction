namespace SonarCopilotFix.Infrastructure;

public interface IConfigurationHelper
{
    string? InputSonarHostUrl { get; }
    string? InputSonarProjectKey { get; }
    IReadOnlyList<string> InputComponents { get; }
    string? InputSonarBranch { get; }
    string? InputSonarOrganization { get; }
    int InputMaxIssues { get; }
    IReadOnlyList<string> InputStatuses { get; }
    IReadOnlyList<string> InputSeverities { get; }
    IReadOnlyList<string> InputImpactSoftwareQualities { get; }
    IReadOnlyList<string> InputImpactSeverities { get; }
    IReadOnlyList<string> InputCleanCodeAttributeCategories { get; }
    IReadOnlyList<string> InputRules { get; }
    bool InputIncludeRuleDetails { get; }
    bool InputIncludeCodeSnippets { get; }
    int InputCodeSnippetContextLines { get; }
    string? InputCopilotModel { get; }
    string? InputCopilotExtraInstructions { get; }
    string InputBranchPrefix { get; }
    string? InputBaseBranch { get; }
    bool InputPullRequestDraft { get; }
    bool InputDryRun { get; }
    bool InputFailIfNoIssues { get; }
    bool InputAllowGitHubTokenFallback { get; }
    bool InputCopilotAllowAllTools { get; }
    string? SonarToken { get; }
    string? CopilotCliToken { get; }
    string? GhCliToken { get; }
    string? GitHubToken { get; }
    string GitHubWorkspace { get; }
    string GitHubRepository { get; }
    string? GitHubOutput { get; }
    string? GitHubStepSummary { get; }
    string? Path { get; }
    string? Home { get; }
    string? User { get; }
    string? UserProfile { get; }
    string? TmpDir { get; }
    string? Temp { get; }
    string? Tmp { get; }
    string? Ci { get; }
    string? GitHubActions { get; }
    string? RunnerTemp { get; }
    string? DotNetRoot { get; }
    string DotNetCliTelemetryOptOut { get; }
}
