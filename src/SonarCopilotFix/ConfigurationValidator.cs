using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix;

public static class ConfigurationValidator
{
    public static void Validate(IConfigurationHelper configurationHelper)
    {
        var host = configurationHelper.InputSonarHostUrl
            ?? throw new ControlledFailureException("Input sonar_host_url is required.", ExitCodes.ConfigurationError);
        if (!Uri.TryCreate(host.TrimEnd('/'), UriKind.Absolute, out _))
        {
            throw new ControlledFailureException("Input sonar_host_url must be an absolute URL.", ExitCodes.ConfigurationError);
        }

        _ = configurationHelper.InputSonarProjectKey
            ?? throw new ControlledFailureException("Input sonar_project_key is required.", ExitCodes.ConfigurationError);
        _ = configurationHelper.SonarToken
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
    }
}
