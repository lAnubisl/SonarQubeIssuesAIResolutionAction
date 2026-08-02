namespace SonarCopilotFix.Helpers;

public sealed class ConfigurationHelper : IConfigurationHelper
{
    private static readonly string[] ValidIssueTypes =
        ["CODE_SMELL", "BUG", "VULNERABILITY"];
    private static readonly string[] ValidCopilotProviderTypes =
        ["openai", "azure", "anthropic"];

    public ConfigurationHelper()
    {
        SonarHostUri = RequiredAbsoluteHttpUri(
            "INPUT_SONAR_HOST_URL",
            "Input sonar_host_url is required.",
            "Input sonar_host_url must be an absolute HTTP or HTTPS URL.",
            ensureTrailingSlash: true);
        SonarProjectKey = Required("INPUT_SONAR_PROJECT_KEY", "Input sonar_project_key is required.");
        InputComponents = Csv(Get("INPUT_COMPONENTS"));
        InputSonarBranch = Trimmed(Get("INPUT_SONAR_BRANCH"));
        InputSonarOrganization = Trimmed(Get("INPUT_SONAR_ORGANIZATION"));
        InputMaxIssues = PositiveInt("INPUT_MAX_ISSUES", 10);
        InputStatuses = Csv(Trimmed(Get("INPUT_STATUSES")) ?? "OPEN");
        InputType = Trimmed(Get("INPUT_TYPE"));
        InputSeverities = Csv(Get("INPUT_SEVERITIES"));
        InputImpactSoftwareQualities = Csv(Get("INPUT_IMPACT_SOFTWARE_QUALITIES"));
        InputImpactSeverities = Csv(Get("INPUT_IMPACT_SEVERITIES"));
        InputCleanCodeAttributeCategories = Csv(Get("INPUT_CLEAN_CODE_ATTRIBUTE_CATEGORIES"));
        InputRules = Csv(Get("INPUT_RULES"));
        InputIncludeRuleDetails = Bool("INPUT_INCLUDE_RULE_DETAILS", true);
        InputIncludeCodeSnippets = Bool("INPUT_INCLUDE_CODE_SNIPPETS", true);
        InputCodeSnippetContextLines = PositiveInt("INPUT_CODE_SNIPPET_CONTEXT_LINES", 20);
        InputCopilotModel = Trimmed(Get("INPUT_COPILOT_MODEL"));
        InputCopilotProviderType = LowerTrimmed(Get("INPUT_COPILOT_PROVIDER_TYPE"));
        InputCopilotProviderBaseUrl = Trimmed(Get("INPUT_COPILOT_PROVIDER_BASE_URL"));
        InputCopilotOffline = Bool("INPUT_COPILOT_OFFLINE", false);
        InputCopilotExtraInstructions = Trimmed(Get("INPUT_COPILOT_EXTRA_INSTRUCTIONS"));
        InputBranchPrefix = Trimmed(Get("INPUT_BRANCH_PREFIX")) ?? "copilot/sonar-fixes";
        InputBaseBranch = Trimmed(Get("INPUT_BASE_BRANCH"));
        InputPullRequestDraft = Bool("INPUT_PULL_REQUEST_DRAFT", true);
        InputFailIfNoIssues = Bool("INPUT_FAIL_IF_NO_ISSUES", false);
        InputCopilotAllowedTools = Csv(Get("INPUT_COPILOT_ALLOWED_TOOLS"));
        InputCopilotAllowAllTools = Bool("INPUT_COPILOT_ALLOW_ALL_TOOLS", false);
        SonarToken = Required("SONAR_TOKEN", "SONAR_TOKEN is required.");
        CopilotGitHubToken = Trimmed(Get("COPILOT_GITHUB_TOKEN"));
        CopilotProviderApiKey = Trimmed(Get("COPILOT_PROVIDER_API_KEY"));
        GitHubToken = Required("GH_TOKEN", "GH_TOKEN is required.");
        GitHubWorkspace = ExistingDirectoryPath(
            Trimmed(Get("GITHUB_WORKSPACE")) ?? Directory.GetCurrentDirectory(),
            "GITHUB_WORKSPACE");
        GitHubRepository = Trimmed(Get("GITHUB_REPOSITORY")) ?? "unknown/unknown";
        GitHubOutput = OptionalFullPath(Get("GITHUB_OUTPUT"), "GITHUB_OUTPUT");
        GitHubStepSummary = OptionalFullPath(Get("GITHUB_STEP_SUMMARY"), "GITHUB_STEP_SUMMARY");
        SafeEnvironmentVariables = BuildSafeEnvironment();

        ValidateSemanticValues();
    }

    public Uri SonarHostUri { get; }
    public string SonarProjectKey { get; }
    public IReadOnlyList<string> InputComponents { get; }
    public string? InputSonarBranch { get; }
    public string? InputSonarOrganization { get; }
    public int InputMaxIssues { get; }
    public IReadOnlyList<string> InputStatuses { get; }
    public string? InputType { get; }
    public IReadOnlyList<string> InputSeverities { get; }
    public IReadOnlyList<string> InputImpactSoftwareQualities { get; }
    public IReadOnlyList<string> InputImpactSeverities { get; }
    public IReadOnlyList<string> InputCleanCodeAttributeCategories { get; }
    public IReadOnlyList<string> InputRules { get; }
    public bool InputIncludeRuleDetails { get; }
    public bool InputIncludeCodeSnippets { get; }
    public int InputCodeSnippetContextLines { get; }
    public string? InputCopilotModel { get; }
    public string? InputCopilotProviderType { get; }
    public string? InputCopilotProviderBaseUrl { get; }
    public bool InputCopilotOffline { get; }
    public string? InputCopilotExtraInstructions { get; }
    public string InputBranchPrefix { get; }
    public string? InputBaseBranch { get; }
    public bool InputPullRequestDraft { get; }
    public bool InputFailIfNoIssues { get; }
    public IReadOnlyList<string> InputCopilotAllowedTools { get; }
    public bool InputCopilotAllowAllTools { get; }
    public string SonarToken { get; }
    public string? CopilotGitHubToken { get; }
    public string? CopilotProviderApiKey { get; }
    public string GitHubToken { get; }
    public string GitHubWorkspace { get; }
    public string GitHubRepository { get; }
    public string? GitHubOutput { get; }
    public string? GitHubStepSummary { get; }
    public IReadOnlyDictionary<string, string?> SafeEnvironmentVariables { get; }

    private void ValidateSemanticValues()
    {
        if (InputType is not null && !ValidIssueTypes.Contains(InputType, StringComparer.Ordinal))
        {
            throw ConfigurationError($"Input type must be one of: {string.Join(", ", ValidIssueTypes)}.");
        }

        ValidateGitReference(InputBranchPrefix, "branch_prefix");
        if (InputBaseBranch is not null)
        {
            ValidateGitReference(InputBaseBranch, "base_branch");
        }

        ValidateGitHubRepository(GitHubRepository);

        bool usesCustomProvider =
            InputCopilotProviderType is not null
            || InputCopilotProviderBaseUrl is not null
            || InputCopilotOffline;
        if (!usesCustomProvider)
        {
            if (CopilotGitHubToken is null)
            {
                throw ConfigurationError(
                    "COPILOT_GITHUB_TOKEN is required when using GitHub-hosted Copilot models. Configure copilot_provider_base_url and copilot_model to use a custom provider instead.");
            }

            return;
        }

        if (InputCopilotProviderType is not null
            && !ValidCopilotProviderTypes.Contains(InputCopilotProviderType, StringComparer.Ordinal))
        {
            throw ConfigurationError(
                $"Input copilot_provider_type must be one of: {string.Join(", ", ValidCopilotProviderTypes)}.");
        }

        if (InputCopilotProviderBaseUrl is null)
        {
            throw ConfigurationError(
                "Input copilot_provider_base_url is required when using a custom Copilot model provider.");
        }

        _ = AbsoluteHttpUri(
            InputCopilotProviderBaseUrl,
            "Input copilot_provider_base_url must be an absolute HTTP or HTTPS URL.");

        if (InputCopilotModel is null)
        {
            throw ConfigurationError(
                "Input copilot_model is required when using a custom Copilot model provider.");
        }

        if (ProviderRequiresApiKey(InputCopilotProviderType) && CopilotProviderApiKey is null)
        {
            throw ConfigurationError(
                $"COPILOT_PROVIDER_API_KEY is required when copilot_provider_type is '{InputCopilotProviderType}'.");
        }
    }

    private IReadOnlyDictionary<string, string?> BuildSafeEnvironment() =>
        new Dictionary<string, string?>
        {
            ["PATH"] = Get("PATH"),
            ["HOME"] = Get("HOME"),
            ["USER"] = Get("USER"),
            ["USERPROFILE"] = Get("USERPROFILE"),
            ["TMPDIR"] = Get("TMPDIR"),
            ["TEMP"] = Get("TEMP"),
            ["TMP"] = Get("TMP"),
            ["CI"] = Get("CI"),
            ["GITHUB_ACTIONS"] = Get("GITHUB_ACTIONS"),
            ["GITHUB_WORKSPACE"] = GitHubWorkspace,
            ["RUNNER_TEMP"] = Get("RUNNER_TEMP"),
            ["DOTNET_ROOT"] = Get("DOTNET_ROOT"),
            ["JAVA_HOME"] = Get("JAVA_HOME"),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = Trimmed(Get("DOTNET_CLI_TELEMETRY_OPTOUT")) ?? "1"
        };

    private static Uri RequiredAbsoluteHttpUri(
        string name,
        string missingMessage,
        string invalidMessage,
        bool ensureTrailingSlash)
    {
        string value = Required(name, missingMessage);
        Uri uri = AbsoluteHttpUri(value, invalidMessage);
        if (!ensureTrailingSlash)
        {
            return uri;
        }

        UriBuilder builder = new(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/"
        };
        return builder.Uri;
    }

    private static Uri AbsoluteHttpUri(string value, string invalidMessage)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw ConfigurationError(invalidMessage);
        }

        return uri;
    }

    private static string Required(string name, string message) =>
        Trimmed(Get(name)) ?? throw ConfigurationError(message);

    private static string? Get(string name) => Environment.GetEnvironmentVariable(name);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? LowerTrimmed(string? value) =>
        Trimmed(value)?.ToLowerInvariant();

    private static bool Bool(string name, bool fallback)
    {
        string? value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return bool.TryParse(value, out bool result)
            ? result
            : throw ConfigurationError($"Invalid boolean value '{value}' for {name}.");
    }

    private static int PositiveInt(string name, int fallback)
    {
        string? value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, out int result) && result > 0
            ? result
            : throw ConfigurationError($"Invalid positive integer value '{value}' for {name}.");
    }

    private static string[] Csv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static bool ProviderRequiresApiKey(string? providerType) =>
        string.Equals(providerType, "azure", StringComparison.Ordinal)
        || string.Equals(providerType, "anthropic", StringComparison.Ordinal);

    private static string? OptionalFullPath(string? value, string name)
    {
        string? trimmed = Trimmed(value);
        return trimmed is null ? null : FullPath(trimmed, name);
    }

    private static string ExistingDirectoryPath(string value, string name)
    {
        string path = FullPath(value, name);
        if (!Directory.Exists(path))
        {
            throw ConfigurationError($"{name} must identify an existing directory.");
        }

        return path;
    }

    private static string FullPath(string value, string name)
    {
        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw ConfigurationError($"{name} must be a valid file-system path.");
        }
    }

    private static void ValidateGitReference(string value, string inputName)
    {
        char[] invalidCharacters = ['~', '^', ':', '?', '*', '[', '\\'];
        string[] segments = value.Split('/');
        if (value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith("-", StringComparison.Ordinal)
            || value.EndsWith("/", StringComparison.Ordinal)
            || value is "@" or "HEAD"
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("@{", StringComparison.Ordinal)
            || value.Any(character => char.IsControl(character)
                || char.IsWhiteSpace(character)
                || invalidCharacters.Contains(character))
            || segments.Any(segment => segment.Length == 0
                || segment == "."
                || segment.EndsWith(".", StringComparison.Ordinal)
                || segment.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
        {
            throw ConfigurationError($"Input {inputName} is not a valid Git reference.");
        }
    }

    private static void ValidateGitHubRepository(string value)
    {
        string[] parts = value.Split('/');
        if (parts.Length != 2 || parts.Any(part => string.IsNullOrWhiteSpace(part)))
        {
            throw ConfigurationError("GITHUB_REPOSITORY must use the 'owner/repository' format.");
        }
    }

    private static ControlledFailureException ConfigurationError(string message) =>
        new(message, ExitCodes.ConfigurationError);
}
