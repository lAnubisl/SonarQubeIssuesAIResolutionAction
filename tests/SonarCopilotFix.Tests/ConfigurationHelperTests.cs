using NUnit.Framework;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ConfigurationHelperTests
{
    [Test]
    public static void ReadsTrimmedTypedAndDistinctInputs()
    {
        using EnvironmentScope environment = ValidEnvironment(
            ("INPUT_SONAR_PROJECT_KEY", "  project  "),
            ("INPUT_MAX_ISSUES", "25"),
            ("INPUT_STATUSES", " OPEN, CONFIRMED,open "),
            ("INPUT_INCLUDE_CODE_SNIPPETS", "false"),
            ("INPUT_COPILOT_PROVIDER_TYPE", " Azure "),
            ("INPUT_COPILOT_PROVIDER_BASE_URL", " https://foundry.example/openai/v1 "),
            ("INPUT_COPILOT_MODEL", " model "),
            ("INPUT_COPILOT_OFFLINE", "true"),
            ("COPILOT_GITHUB_TOKEN", " copilot-secret "),
            ("COPILOT_PROVIDER_API_KEY", " provider-secret "),
            ("GH_TOKEN", " github-secret "),
            ("INPUT_BRANCH_PREFIX", null));
        ConfigurationHelper configuration = new();

        Assert.Equal("project", configuration.SonarProjectKey);
        Assert.Equal(25, configuration.InputMaxIssues);
        CollectionAssert.AreEqual(["OPEN", "CONFIRMED"], configuration.InputStatuses);
        Assert.False(configuration.InputIncludeCodeSnippets);
        Assert.Equal("azure", configuration.InputCopilotProviderType);
        Assert.Equal("https://foundry.example/openai/v1", configuration.InputCopilotProviderBaseUrl);
        Assert.True(configuration.InputCopilotOffline);
        Assert.Equal("copilot-secret", configuration.CopilotGitHubToken);
        Assert.Equal("provider-secret", configuration.CopilotProviderApiKey);
        Assert.Equal("github-secret", configuration.GitHubToken);
        Assert.Equal("copilot/sonar-fixes", configuration.InputBranchPrefix);
    }

    [TestCase("INPUT_MAX_ISSUES", "0")]
    [TestCase("INPUT_MAX_ISSUES", "invalid")]
    [TestCase("INPUT_PULL_REQUEST_DRAFT", "sometimes")]
    public static void InvalidTypedInputThrowsConfigurationError(string name, string value)
    {
        using EnvironmentScope environment = ValidEnvironment((name, value));

        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
            _ = new ConfigurationHelper());

        Assert.Equal(ExitCodes.ConfigurationError, exception.ExitCode);
    }

    private static EnvironmentScope ValidEnvironment(params (string Name, string? Value)[] overrides)
    {
        Dictionary<string, string?> values = new(StringComparer.Ordinal)
        {
            ["INPUT_SONAR_HOST_URL"] = "https://sonar.example",
            ["INPUT_SONAR_PROJECT_KEY"] = "project",
            ["SONAR_TOKEN"] = "sonar-secret",
            ["COPILOT_GITHUB_TOKEN"] = "copilot-secret",
            ["COPILOT_PROVIDER_API_KEY"] = null,
            ["GH_TOKEN"] = "github-secret"
        };
        foreach ((string name, string? value) in overrides)
        {
            values[name] = value;
        }

        return new EnvironmentScope(values.Select(pair => (pair.Key, pair.Value)).ToArray());
    }
}
