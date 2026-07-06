namespace SonarCopilotFix.Infrastructure;

public static class ConfigurationHelperExtensions
{
    public static Uri GetSonarHostUri(this IConfigurationHelper configurationHelper)
    {
        string? host = configurationHelper.InputSonarHostUrl?.TrimEnd('/');
        if (!Uri.TryCreate(host is null ? null : host + "/", UriKind.Absolute, out Uri? hostUri))
        {
            throw new ControlledFailureException(
                "Input sonar_host_url must be an absolute URL.",
                ExitCodes.ConfigurationError);
        }

        return hostUri;
    }

    public static string GetSonarProjectKey(this IConfigurationHelper configurationHelper) =>
        configurationHelper.InputSonarProjectKey
        ?? throw new ControlledFailureException(
            "Input sonar_project_key is required.",
            ExitCodes.ConfigurationError);

    public static string GetSonarToken(this IConfigurationHelper configurationHelper) =>
        configurationHelper.SonarToken
        ?? throw new ControlledFailureException(
            "SONAR_TOKEN is required.",
            ExitCodes.ConfigurationError);

    public static string GetGitHubToken(this IConfigurationHelper configurationHelper) =>
        configurationHelper.GhCliToken
        ?? throw new ControlledFailureException(
            "GH_CLI_TOKEN is required.",
            ExitCodes.ConfigurationError);
}
