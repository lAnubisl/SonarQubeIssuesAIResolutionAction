using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix;

public sealed record ActionInputs(
    Uri SonarHostUrl,
    string SonarProjectKey,
    IReadOnlyList<string> Components,
    string? SonarBranch,
    string? SonarOrganization,
    int MaxIssues,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> Severities,
    IReadOnlyList<string> ImpactSoftwareQualities,
    IReadOnlyList<string> ImpactSeverities,
    IReadOnlyList<string> CleanCodeAttributeCategories,
    IReadOnlyList<string> Rules,
    bool IncludeRuleDetails,
    bool IncludeCodeSnippets,
    int CodeSnippetContextLines,
    string? CopilotModel,
    string? CopilotExtraInstructions,
    string BranchPrefix,
    string? BaseBranch,
    bool PullRequestDraft,
    bool DryRun,
    bool FailIfNoIssues,
    bool AllowGitHubTokenFallback,
    bool CopilotAllowAllTools,
    string SonarToken,
    string? CopilotCliToken,
    string? GhCliToken,
    string? GitHubToken,
    string Workspace,
    string Repository)
{
    public static ActionInputs FromEnvironment(IConfigurationHelper configurationHelper)
    {
        var host = configurationHelper.InputSonarHostUrl
            ?? throw new ControlledFailureException("Input sonar_host_url is required.", ExitCodes.ConfigurationError);
        if (!Uri.TryCreate(host.TrimEnd('/'), UriKind.Absolute, out var hostUri))
        {
            throw new ControlledFailureException("Input sonar_host_url must be an absolute URL.", ExitCodes.ConfigurationError);
        }

        var projectKey = configurationHelper.InputSonarProjectKey
            ?? throw new ControlledFailureException("Input sonar_project_key is required.", ExitCodes.ConfigurationError);
        var sonarToken = configurationHelper.SonarToken
            ?? throw new ControlledFailureException("SONAR_TOKEN is required.", ExitCodes.ConfigurationError);

        var dryRun = configurationHelper.InputDryRun;
        var allowFallback = configurationHelper.InputAllowGitHubTokenFallback;
        var ghCliToken = configurationHelper.GhCliToken;
        var githubToken = configurationHelper.GitHubToken;
        var copilotToken = configurationHelper.CopilotCliToken;

        if (!dryRun && string.IsNullOrWhiteSpace(copilotToken))
        {
            throw new ControlledFailureException("COPILOT_CLI_TOKEN is required outside dry_run mode.", ExitCodes.ConfigurationError);
        }

        if (!dryRun && string.IsNullOrWhiteSpace(ghCliToken) && !(allowFallback && !string.IsNullOrWhiteSpace(githubToken)))
        {
            throw new ControlledFailureException("GH_CLI_TOKEN is required outside dry_run mode unless allow_github_token_fallback is true and GITHUB_TOKEN is available.", ExitCodes.ConfigurationError);
        }

        return new ActionInputs(
            hostUri,
            projectKey,
            configurationHelper.InputComponents,
            configurationHelper.InputSonarBranch,
            configurationHelper.InputSonarOrganization,
            configurationHelper.InputMaxIssues,
            configurationHelper.InputStatuses,
            configurationHelper.InputSeverities,
            configurationHelper.InputImpactSoftwareQualities,
            configurationHelper.InputImpactSeverities,
            configurationHelper.InputCleanCodeAttributeCategories,
            configurationHelper.InputRules,
            configurationHelper.InputIncludeRuleDetails,
            configurationHelper.InputIncludeCodeSnippets,
            configurationHelper.InputCodeSnippetContextLines,
            configurationHelper.InputCopilotModel,
            configurationHelper.InputCopilotExtraInstructions,
            configurationHelper.InputBranchPrefix,
            configurationHelper.InputBaseBranch,
            configurationHelper.InputPullRequestDraft,
            dryRun,
            configurationHelper.InputFailIfNoIssues,
            allowFallback,
            configurationHelper.InputCopilotAllowAllTools,
            sonarToken,
            copilotToken,
            ghCliToken,
            githubToken,
            configurationHelper.GitHubWorkspace,
            configurationHelper.GitHubRepository);
    }

    public string EffectiveGitHubToken => !string.IsNullOrWhiteSpace(GhCliToken)
        ? GhCliToken
        : AllowGitHubTokenFallback && !string.IsNullOrWhiteSpace(GitHubToken)
            ? GitHubToken
            : throw new ControlledFailureException("No GitHub CLI token is available.", ExitCodes.ConfigurationError);
}
