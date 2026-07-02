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
    string? InputType { get; }
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
    IReadOnlyList<string> InputCopilotAllowedTools { get; }
    bool InputCopilotAllowAllTools { get; }
    string? SonarToken { get; }
    string? CopilotCliToken { get; }
    string? GhCliToken { get; }
    string GitHubWorkspace { get; }
    string GitHubRepository { get; }
    string? GitHubOutput { get; }

    /// <summary>
    /// Gets the path to the GitHub Actions step summary file.
    /// documentation: https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands#adding-a-job-summary
    /// </summary>
    string? GitHubStepSummary { get; }
    IReadOnlyDictionary<string, string?> SafeEnvironmentVariables { get; }
}
