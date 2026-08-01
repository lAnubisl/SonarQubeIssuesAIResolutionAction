using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix;

public static class ConfigurationValidator
{
    private static readonly string[] ValidIssueTypes =
        ["CODE_SMELL", "BUG", "VULNERABILITY"];
    private static readonly string[] ValidCopilotProviderTypes =
        ["openai", "azure", "anthropic"];

    public static void Validate(IConfigurationHelper configurationHelper)
    {
        string host = configurationHelper.InputSonarHostUrl
            ?? throw new ControlledFailureException("Input sonar_host_url is required.", ExitCodes.ConfigurationError);
        if (!Uri.TryCreate(host.TrimEnd('/'), UriKind.Absolute, out _))
        {
            throw new ControlledFailureException("Input sonar_host_url must be an absolute URL.", ExitCodes.ConfigurationError);
        }

        _ = configurationHelper.InputSonarProjectKey
            ?? throw new ControlledFailureException("Input sonar_project_key is required.", ExitCodes.ConfigurationError);
        _ = configurationHelper.SonarToken
            ?? throw new ControlledFailureException("SONAR_TOKEN is required.", ExitCodes.ConfigurationError);

        if (configurationHelper.InputType is { } issueType && !ValidIssueTypes.Contains(issueType))
        {
            throw new ControlledFailureException(
                $"Input type must be one of: {string.Join(", ", ValidIssueTypes)}.",
                ExitCodes.ConfigurationError);
        }

        string? ghCliToken = configurationHelper.GhCliToken;
        bool usesCustomProvider = ValidateCopilotProvider(configurationHelper);

        if (!usesCustomProvider && string.IsNullOrWhiteSpace(configurationHelper.CopilotGitHubToken))
        {
            throw new ControlledFailureException(
                "COPILOT_GITHUB_TOKEN is required when using GitHub-hosted Copilot models. Configure copilot_provider_base_url and copilot_model to use a custom provider instead.",
                ExitCodes.ConfigurationError);
        }

        if (string.IsNullOrWhiteSpace(ghCliToken))
        {
            throw new ControlledFailureException("GH_CLI_TOKEN is required.", ExitCodes.ConfigurationError);
        }
    }

    private static bool ValidateCopilotProvider(IConfigurationHelper configurationHelper)
    {
        string? providerType = configurationHelper.InputCopilotProviderType;
        string? providerBaseUrl = configurationHelper.InputCopilotProviderBaseUrl;
        string? providerApiKey = configurationHelper.CopilotProviderApiKey;
        bool usesCustomProvider =
            !string.IsNullOrWhiteSpace(providerType)
            || !string.IsNullOrWhiteSpace(providerBaseUrl)
            || configurationHelper.InputCopilotOffline;

        if (!usesCustomProvider)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(providerType)
            && !ValidCopilotProviderTypes.Contains(providerType, StringComparer.Ordinal))
        {
            throw new ControlledFailureException(
                $"Input copilot_provider_type must be one of: {string.Join(", ", ValidCopilotProviderTypes)}.",
                ExitCodes.ConfigurationError);
        }

        if (string.IsNullOrWhiteSpace(providerBaseUrl))
        {
            throw new ControlledFailureException(
                "Input copilot_provider_base_url is required when using a custom Copilot model provider.",
                ExitCodes.ConfigurationError);
        }

        if (!Uri.TryCreate(providerBaseUrl, UriKind.Absolute, out Uri? providerUri)
            || string.IsNullOrWhiteSpace(providerUri.Scheme))
        {
            throw new ControlledFailureException(
                "Input copilot_provider_base_url must be an absolute URL.",
                ExitCodes.ConfigurationError);
        }

        if (string.IsNullOrWhiteSpace(configurationHelper.InputCopilotModel))
        {
            throw new ControlledFailureException(
                "Input copilot_model is required when using a custom Copilot model provider.",
                ExitCodes.ConfigurationError);
        }

        if (ProviderRequiresApiKey(providerType) && string.IsNullOrWhiteSpace(providerApiKey))
        {
            throw new ControlledFailureException(
                $"COPILOT_PROVIDER_API_KEY is required when copilot_provider_type is '{providerType}'.",
                ExitCodes.ConfigurationError);
        }

        return true;
    }

    private static bool ProviderRequiresApiKey(string? providerType) =>
        string.Equals(providerType, "azure", StringComparison.Ordinal)
        || string.Equals(providerType, "anthropic", StringComparison.Ordinal);
}
