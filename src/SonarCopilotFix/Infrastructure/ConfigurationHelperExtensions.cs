namespace SonarCopilotFix.Infrastructure;

public static class ConfigurationHelperExtensions
{
    public static Uri GetSonarHostUri(this IConfigurationHelper configurationHelper)
    {
        string? host = configurationHelper.InputSonarHostUrl?.TrimEnd(Path.AltDirectorySeparatorChar);
        if (host is null)
        {
            throw new ControlledFailureException(
                "Input sonar_host_url must be an absolute URL.",
                ExitCodes.ConfigurationError);
        }

        UriBuilder builder = new(host);
        if (!builder.Uri.IsAbsoluteUri)
        {
            throw new ControlledFailureException(
                "Input sonar_host_url must be an absolute URL.",
                ExitCodes.ConfigurationError);
        }

        builder.Path = builder.Path.TrimEnd(Path.AltDirectorySeparatorChar) + Path.AltDirectorySeparatorChar;
        return builder.Uri;
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
