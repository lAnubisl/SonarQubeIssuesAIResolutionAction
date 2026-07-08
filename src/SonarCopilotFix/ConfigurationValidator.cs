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
        string? copilotToken = configurationHelper.CopilotCliToken;
        ValidateCopilotProvider(configurationHelper);

        if (string.IsNullOrWhiteSpace(copilotToken))
        {
            throw new ControlledFailureException("COPILOT_CLI_TOKEN is required.", ExitCodes.ConfigurationError);
        }

        if (string.IsNullOrWhiteSpace(ghCliToken))
        {
            throw new ControlledFailureException("GH_CLI_TOKEN is required.", ExitCodes.ConfigurationError);
        }
    }

    private static void ValidateCopilotProvider(IConfigurationHelper configurationHelper)
    {
        string? providerType = configurationHelper.InputCopilotProviderType;
        string? providerBaseUrl = configurationHelper.InputCopilotProviderBaseUrl;
        string? providerApiKey = configurationHelper.CopilotProviderApiKey;
        bool usesCustomProvider =
            !string.IsNullOrWhiteSpace(providerType)
            || !string.IsNullOrWhiteSpace(providerBaseUrl)
            || !string.IsNullOrWhiteSpace(providerApiKey)
            || configurationHelper.InputCopilotOffline;

        if (!usesCustomProvider)
        {
            return;
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

        if (!providerUri.IsLoopback && string.IsNullOrWhiteSpace(providerApiKey))
        {
            throw new ControlledFailureException(
                "COPILOT_PROVIDER_API_KEY is required when using a remote custom Copilot model provider.",
                ExitCodes.ConfigurationError);
        }
    }
}
