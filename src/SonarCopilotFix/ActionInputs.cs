using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix;

public sealed record ActionInputs(
    Uri SonarHostUrl,
    string SonarProjectKey,
    string? SonarBranch,
    string? SonarOrganization,
    int MaxIssues,
    IReadOnlyList<string> IssueStatuses,
    IReadOnlyList<string> Severities,
    IReadOnlyList<string> CleanCodeAttributeCategories,
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
    public static ActionInputs FromEnvironment(IEnvironment environment)
    {
        static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        static bool Bool(string? value, bool fallback) => value is null ? fallback : bool.TryParse(value, out var result) ? result : throw new ControlledFailureException($"Invalid boolean value '{value}'.", ExitCodes.ConfigurationError);
        static int Int(string? value, int fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return int.TryParse(value, out var result) && result > 0
                ? result
                : throw new ControlledFailureException($"Invalid positive integer value '{value}'.", ExitCodes.ConfigurationError);
        }

        static IReadOnlyList<string> Csv(string? value) => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var host = Trimmed(environment.Get("INPUT_SONAR_HOST_URL"))
            ?? throw new ControlledFailureException("Input sonar_host_url is required.", ExitCodes.ConfigurationError);
        if (!Uri.TryCreate(host.TrimEnd('/'), UriKind.Absolute, out var hostUri))
        {
            throw new ControlledFailureException("Input sonar_host_url must be an absolute URL.", ExitCodes.ConfigurationError);
        }

        var projectKey = Trimmed(environment.Get("INPUT_SONAR_PROJECT_KEY"))
            ?? throw new ControlledFailureException("Input sonar_project_key is required.", ExitCodes.ConfigurationError);
        var sonarToken = Trimmed(environment.Get("SONAR_TOKEN"))
            ?? throw new ControlledFailureException("SONAR_TOKEN is required.", ExitCodes.ConfigurationError);

        var dryRun = Bool(environment.Get("INPUT_DRY_RUN"), false);
        var allowFallback = Bool(environment.Get("INPUT_ALLOW_GITHUB_TOKEN_FALLBACK"), false);
        var ghCliToken = Trimmed(environment.Get("GH_CLI_TOKEN"));
        var githubToken = Trimmed(environment.Get("GITHUB_TOKEN"));
        var copilotToken = Trimmed(environment.Get("COPILOT_CLI_TOKEN"));

        if (!dryRun && string.IsNullOrWhiteSpace(copilotToken))
        {
            throw new ControlledFailureException("COPILOT_CLI_TOKEN is required outside dry_run mode.", ExitCodes.ConfigurationError);
        }

        if (!dryRun && string.IsNullOrWhiteSpace(ghCliToken) && !(allowFallback && !string.IsNullOrWhiteSpace(githubToken)))
        {
            throw new ControlledFailureException("GH_CLI_TOKEN is required outside dry_run mode unless allow_github_token_fallback is true and GITHUB_TOKEN is available.", ExitCodes.ConfigurationError);
        }

        var workspace = Trimmed(environment.Get("GITHUB_WORKSPACE")) ?? Directory.GetCurrentDirectory();

        return new ActionInputs(
            hostUri,
            projectKey,
            Trimmed(environment.Get("INPUT_SONAR_BRANCH")),
            Trimmed(environment.Get("INPUT_SONAR_ORGANIZATION")),
            Int(environment.Get("INPUT_MAX_ISSUES"), 10),
            Csv(environment.Get("INPUT_ISSUE_STATUSES")),
            Csv(environment.Get("INPUT_SEVERITIES")),
            Csv(environment.Get("INPUT_CLEAN_CODE_ATTRIBUTE_CATEGORIES")),
            Bool(environment.Get("INPUT_INCLUDE_RULE_DETAILS"), true),
            Bool(environment.Get("INPUT_INCLUDE_CODE_SNIPPETS"), true),
            Int(environment.Get("INPUT_CODE_SNIPPET_CONTEXT_LINES"), 20),
            Trimmed(environment.Get("INPUT_COPILOT_MODEL")),
            Trimmed(environment.Get("INPUT_COPILOT_EXTRA_INSTRUCTIONS")),
            Trimmed(environment.Get("INPUT_BRANCH_PREFIX")) ?? "copilot/sonar-fixes",
            Trimmed(environment.Get("INPUT_BASE_BRANCH")),
            Bool(environment.Get("INPUT_PULL_REQUEST_DRAFT"), true),
            dryRun,
            Bool(environment.Get("INPUT_FAIL_IF_NO_ISSUES"), false),
            allowFallback,
            Bool(environment.Get("INPUT_COPILOT_ALLOW_ALL_TOOLS"), false),
            sonarToken,
            copilotToken,
            ghCliToken,
            githubToken,
            workspace,
            Trimmed(environment.Get("GITHUB_REPOSITORY")) ?? "unknown/unknown");
    }

    public string EffectiveGitHubToken => !string.IsNullOrWhiteSpace(GhCliToken)
        ? GhCliToken
        : AllowGitHubTokenFallback && !string.IsNullOrWhiteSpace(GitHubToken)
            ? GitHubToken
            : throw new ControlledFailureException("No GitHub CLI token is available.", ExitCodes.ConfigurationError);
}
