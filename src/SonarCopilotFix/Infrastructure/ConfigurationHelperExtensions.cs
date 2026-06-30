namespace SonarCopilotFix.Infrastructure;

public static class ConfigurationHelperExtensions
{
    public static Uri GetSonarHostUri(this IConfigurationHelper configurationHelper) =>
        Uri.TryCreate(configurationHelper.InputSonarHostUrl?.TrimEnd('/'), UriKind.Absolute, out var hostUri)
            ? hostUri
            : throw new ControlledFailureException(
                "Input sonar_host_url must be an absolute URL.",
                ExitCodes.ConfigurationError);

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

    public static string GetEffectiveGitHubToken(this IConfigurationHelper configurationHelper) =>
        !string.IsNullOrWhiteSpace(configurationHelper.GhCliToken)
            ? configurationHelper.GhCliToken
            : configurationHelper.InputAllowGitHubTokenFallback
                && !string.IsNullOrWhiteSpace(configurationHelper.GitHubToken)
                    ? configurationHelper.GitHubToken
                    : throw new ControlledFailureException(
                        "No GitHub CLI token is available.",
                        ExitCodes.ConfigurationError);
}
