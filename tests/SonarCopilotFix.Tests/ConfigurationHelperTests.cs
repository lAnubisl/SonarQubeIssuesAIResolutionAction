using NUnit.Framework;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ConfigurationHelperTests
{
    [Test]
    public static void ReadsTrimmedTypedAndDistinctInputs()
    {
        using EnvironmentScope environment = new(
            ("INPUT_SONAR_PROJECT_KEY", "  project  "),
            ("INPUT_MAX_ISSUES", "25"),
            ("INPUT_STATUSES", " OPEN, CONFIRMED,open "),
            ("INPUT_INCLUDE_CODE_SNIPPETS", "false"),
            ("INPUT_COPILOT_PROVIDER_TYPE", " Azure "),
            ("INPUT_COPILOT_PROVIDER_BASE_URL", " https://foundry.example/openai/v1 "),
            ("INPUT_COPILOT_OFFLINE", "true"),
            ("COPILOT_PROVIDER_API_KEY", " provider-secret "),
            ("INPUT_BRANCH_PREFIX", null));
        ConfigurationHelper configuration = new();

        Assert.Equal("project", configuration.InputSonarProjectKey);
        Assert.Equal(25, configuration.InputMaxIssues);
        CollectionAssert.AreEqual(["OPEN", "CONFIRMED"], configuration.InputStatuses);
        Assert.False(configuration.InputIncludeCodeSnippets);
        Assert.Equal("azure", configuration.InputCopilotProviderType);
        Assert.Equal("https://foundry.example/openai/v1", configuration.InputCopilotProviderBaseUrl);
        Assert.True(configuration.InputCopilotOffline);
        Assert.Equal("provider-secret", configuration.CopilotProviderApiKey);
        Assert.Equal("copilot/sonar-fixes", configuration.InputBranchPrefix);
    }

    [TestCase("INPUT_MAX_ISSUES", "0")]
    [TestCase("INPUT_MAX_ISSUES", "invalid")]
    [TestCase("INPUT_PULL_REQUEST_DRAFT", "sometimes")]
    public static void InvalidTypedInputThrowsConfigurationError(string name, string value)
    {
        using EnvironmentScope environment = new((name, value));
        ConfigurationHelper configuration = new();

        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
        {
            _ = name == "INPUT_MAX_ISSUES"
                ? configuration.InputMaxIssues.ToString()
                : configuration.InputPullRequestDraft.ToString();
        });

        Assert.Equal(ExitCodes.ConfigurationError, exception.ExitCode);
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

        public EnvironmentScope(params (string Name, string? Value)[] values)
        {
            foreach ((string name, string? value) in values)
            {
                _original[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in _original)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
