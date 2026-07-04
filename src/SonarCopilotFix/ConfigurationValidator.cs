using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix;

public static class ConfigurationValidator
{
    private static readonly string[] ValidIssueTypes =
        ["CODE_SMELL", "BUG", "VULNERABILITY"];

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

        if (string.IsNullOrWhiteSpace(copilotToken))
        {
            throw new ControlledFailureException("COPILOT_CLI_TOKEN is required.", ExitCodes.ConfigurationError);
        }

        if (string.IsNullOrWhiteSpace(ghCliToken))
        {
            throw new ControlledFailureException("GH_CLI_TOKEN is required.", ExitCodes.ConfigurationError);
        }
    }
}
