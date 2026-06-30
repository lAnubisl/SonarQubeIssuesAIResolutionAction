namespace SonarCopilotFix.Infrastructure;

public sealed class ConfigurationHelper : IConfigurationHelper
{
    public string? InputSonarHostUrl => Trimmed(Get("INPUT_SONAR_HOST_URL"));
    public string? InputSonarProjectKey => Trimmed(Get("INPUT_SONAR_PROJECT_KEY"));
    public IReadOnlyList<string> InputComponents => Csv(Get("INPUT_COMPONENTS"));
    public string? InputSonarBranch => Trimmed(Get("INPUT_SONAR_BRANCH"));
    public string? InputSonarOrganization => Trimmed(Get("INPUT_SONAR_ORGANIZATION"));
    public int InputMaxIssues => PositiveInt(Get("INPUT_MAX_ISSUES"), 10);
    public IReadOnlyList<string> InputStatuses => Csv(Trimmed(Get("INPUT_STATUSES")) ?? "OPEN");
    public IReadOnlyList<string> InputSeverities => Csv(Get("INPUT_SEVERITIES"));
    public IReadOnlyList<string> InputImpactSoftwareQualities => Csv(Get("INPUT_IMPACT_SOFTWARE_QUALITIES"));
    public IReadOnlyList<string> InputImpactSeverities => Csv(Get("INPUT_IMPACT_SEVERITIES"));
    public IReadOnlyList<string> InputCleanCodeAttributeCategories => Csv(Get("INPUT_CLEAN_CODE_ATTRIBUTE_CATEGORIES"));
    public IReadOnlyList<string> InputRules => Csv(Get("INPUT_RULES"));
    public bool InputIncludeRuleDetails => Bool(Get("INPUT_INCLUDE_RULE_DETAILS"), true);
    public bool InputIncludeCodeSnippets => Bool(Get("INPUT_INCLUDE_CODE_SNIPPETS"), true);
    public int InputCodeSnippetContextLines => PositiveInt(Get("INPUT_CODE_SNIPPET_CONTEXT_LINES"), 20);
    public string? InputCopilotModel => Trimmed(Get("INPUT_COPILOT_MODEL"));
    public string? InputCopilotExtraInstructions => Trimmed(Get("INPUT_COPILOT_EXTRA_INSTRUCTIONS"));
    public string InputBranchPrefix => Trimmed(Get("INPUT_BRANCH_PREFIX")) ?? "copilot/sonar-fixes";
    public string? InputBaseBranch => Trimmed(Get("INPUT_BASE_BRANCH"));
    public bool InputPullRequestDraft => Bool(Get("INPUT_PULL_REQUEST_DRAFT"), true);
    public bool InputDryRun => Bool(Get("INPUT_DRY_RUN"), false);
    public bool InputFailIfNoIssues => Bool(Get("INPUT_FAIL_IF_NO_ISSUES"), false);
    public bool InputAllowGitHubTokenFallback => Bool(Get("INPUT_ALLOW_GITHUB_TOKEN_FALLBACK"), false);
    public bool InputCopilotAllowAllTools => Bool(Get("INPUT_COPILOT_ALLOW_ALL_TOOLS"), false);
    public string? SonarToken => Trimmed(Get("SONAR_TOKEN"));
    public string? CopilotCliToken => Trimmed(Get("COPILOT_CLI_TOKEN"));
    public string? GhCliToken => Trimmed(Get("GH_CLI_TOKEN"));
    public string? GitHubToken => Trimmed(Get("GITHUB_TOKEN"));
    public string GitHubWorkspace => Trimmed(Get("GITHUB_WORKSPACE")) ?? Directory.GetCurrentDirectory();
    public string GitHubRepository => Trimmed(Get("GITHUB_REPOSITORY")) ?? "unknown/unknown";
    public string? GitHubOutput => Trimmed(Get("GITHUB_OUTPUT"));
    public string? GitHubStepSummary => Trimmed(Get("GITHUB_STEP_SUMMARY"));
    public string? Path => Get("PATH");
    public string? Home => Get("HOME");
    public string? User => Get("USER");
    public string? UserProfile => Get("USERPROFILE");
    public string? TmpDir => Get("TMPDIR");
    public string? Temp => Get("TEMP");
    public string? Tmp => Get("TMP");
    public string? Ci => Get("CI");
    public string? GitHubActions => Get("GITHUB_ACTIONS");
    public string? RunnerTemp => Get("RUNNER_TEMP");
    public string? DotNetRoot => Get("DOTNET_ROOT");
    public string DotNetCliTelemetryOptOut => Trimmed(Get("DOTNET_CLI_TELEMETRY_OPTOUT")) ?? "1";

    private static string? Get(string name) => Environment.GetEnvironmentVariable(name);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Bool(string? value, bool fallback) =>
        value is null
            ? fallback
            : bool.TryParse(value, out var result)
                ? result
                : throw new ControlledFailureException(
                    $"Invalid boolean value '{value}'.",
                    ExitCodes.ConfigurationError);

    private static int PositiveInt(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, out var result) && result > 0
            ? result
            : throw new ControlledFailureException(
                $"Invalid positive integer value '{value}'.",
                ExitCodes.ConfigurationError);
    }

    private static IReadOnlyList<string> Csv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}
