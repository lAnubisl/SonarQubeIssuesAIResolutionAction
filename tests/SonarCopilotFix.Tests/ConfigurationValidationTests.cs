using NUnit.Framework;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ConfigurationValidationTests
{
    [TestCase("INPUT_SONAR_HOST_URL", "sonar_host_url")]
    [TestCase("INPUT_SONAR_PROJECT_KEY", "sonar_project_key")]
    [TestCase("SONAR_TOKEN", "SONAR_TOKEN")]
    [TestCase("COPILOT_GITHUB_TOKEN", "COPILOT_GITHUB_TOKEN")]
    [TestCase("GH_TOKEN", "GH_TOKEN")]
    public static void RequiredValuesFailDuringConstruction(string name, string expectedMessage)
    {
        using EnvironmentScope environment = ValidEnvironment((name, null));

        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
            _ = new ConfigurationHelper());

        Assert.Contains(expectedMessage, exception.Message);
        Assert.Equal(ExitCodes.ConfigurationError, exception.ExitCode);
    }

    [TestCase("CODE_SMELL")]
    [TestCase("BUG")]
    [TestCase("VULNERABILITY")]
    public static void AcceptsSupportedIssueType(string issueType)
    {
        using EnvironmentScope environment = ValidEnvironment(("INPUT_TYPE", issueType));

        _ = new ConfigurationHelper();
    }

    [Test]
    public static void RejectsUnsupportedIssueTypeDuringConstruction()
    {
        using EnvironmentScope environment = ValidEnvironment(("INPUT_TYPE", "SECURITY_HOTSPOT"));

        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
            _ = new ConfigurationHelper());

        Assert.Contains("CODE_SMELL, BUG, VULNERABILITY", exception.Message);
    }

    [TestCase("INPUT_SONAR_HOST_URL", "not-a-url", "sonar_host_url")]
    [TestCase("INPUT_BRANCH_PREFIX", "fix branch", "branch_prefix")]
    [TestCase("INPUT_BASE_BRANCH", "feature..branch", "base_branch")]
    [TestCase("GITHUB_REPOSITORY", "owner/repository/extra", "GITHUB_REPOSITORY")]
    public static void InvalidStructuredValuesFailDuringConstruction(
        string name,
        string value,
        string expectedMessage)
    {
        using EnvironmentScope environment = ValidEnvironment((name, value));

        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
            _ = new ConfigurationHelper());

        Assert.Contains(expectedMessage, exception.Message);
    }

    [TestCase(null, "http://localhost:11434", null)]
    [TestCase("openai", "http://10.0.0.5:8000/v1", null)]
    [TestCase("azure", "https://foundry.example/openai", "provider-secret")]
    [TestCase("anthropic", "https://anthropic.example", "provider-secret")]
    public static void AcceptsValidCustomProvider(
        string? providerType,
        string providerBaseUrl,
        string? providerApiKey)
    {
        using EnvironmentScope environment = ValidEnvironment(
            ("COPILOT_GITHUB_TOKEN", null),
            ("INPUT_COPILOT_MODEL", "model"),
            ("INPUT_COPILOT_PROVIDER_TYPE", providerType),
            ("INPUT_COPILOT_PROVIDER_BASE_URL", providerBaseUrl),
            ("COPILOT_PROVIDER_API_KEY", providerApiKey));

        _ = new ConfigurationHelper();
    }

    [Test]
    public static void RejectsUnsupportedProviderType()
    {
        using EnvironmentScope environment = ValidEnvironment(
            ("INPUT_COPILOT_MODEL", "model"),
            ("INPUT_COPILOT_PROVIDER_TYPE", "foundry"),
            ("INPUT_COPILOT_PROVIDER_BASE_URL", "https://foundry.example"));

        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
            _ = new ConfigurationHelper());

        Assert.Contains("openai, azure, anthropic", exception.Message);
    }

    [TestCase("INPUT_COPILOT_PROVIDER_BASE_URL", "copilot_provider_base_url")]
    [TestCase("INPUT_COPILOT_MODEL", "copilot_model")]
    public static void CustomProviderRequiresUrlAndModel(string missingName, string expectedMessage)
    {
        using EnvironmentScope environment = ValidEnvironment(
            ("COPILOT_GITHUB_TOKEN", null),
            ("INPUT_COPILOT_MODEL", "model"),
            ("INPUT_COPILOT_PROVIDER_TYPE", "openai"),
            ("INPUT_COPILOT_PROVIDER_BASE_URL", "https://foundry.example"),
            (missingName, null));

        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
            _ = new ConfigurationHelper());

        Assert.Contains(expectedMessage, exception.Message);
    }

    [TestCase("azure")]
    [TestCase("anthropic")]
    public static void KeyedProviderRequiresApiKey(string providerType)
    {
        using EnvironmentScope environment = ValidEnvironment(
            ("COPILOT_GITHUB_TOKEN", null),
            ("INPUT_COPILOT_MODEL", "model"),
            ("INPUT_COPILOT_PROVIDER_TYPE", providerType),
            ("INPUT_COPILOT_PROVIDER_BASE_URL", "https://provider.example"),
            ("COPILOT_PROVIDER_API_KEY", null));

        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
            _ = new ConfigurationHelper());

        Assert.Contains("COPILOT_PROVIDER_API_KEY", exception.Message);
    }

    private static EnvironmentScope ValidEnvironment(params (string Name, string? Value)[] overrides)
    {
        Dictionary<string, string?> values = new(StringComparer.Ordinal)
        {
            ["INPUT_SONAR_HOST_URL"] = "https://sonar.example",
            ["INPUT_SONAR_PROJECT_KEY"] = "project",
            ["INPUT_TYPE"] = null,
            ["INPUT_COPILOT_MODEL"] = null,
            ["INPUT_COPILOT_PROVIDER_TYPE"] = null,
            ["INPUT_COPILOT_PROVIDER_BASE_URL"] = null,
            ["INPUT_COPILOT_OFFLINE"] = null,
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
