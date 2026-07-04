using NUnit.Framework;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ConfigurationValidatorTests
{
    [Test]
    public static void RequiresCopilotToken()
    {
        var configurationHelper = TestData.MockConfigurationHelper(copilotCliToken: null);

        var ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("COPILOT_CLI_TOKEN", ex.Message);
    }

    [Test]
    public static void RequiresGitHubCliToken()
    {
        var configurationHelper = TestData.MockConfigurationHelper(
            ghCliToken: null);

        var ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("GH_CLI_TOKEN", ex.Message);
    }

    [TestCase("CODE_SMELL")]
    [TestCase("BUG")]
    [TestCase("VULNERABILITY")]
    public static void AcceptsSupportedIssueType(string issueType)
    {
        var configurationHelper = TestData.MockConfigurationHelper(inputType: issueType);

        ConfigurationValidator.Validate(configurationHelper.Object);
    }

    [Test]
    public static void RejectsUnsupportedIssueType()
    {
        var configurationHelper = TestData.MockConfigurationHelper(inputType: "SECURITY_HOTSPOT");

        var ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("CODE_SMELL, BUG, VULNERABILITY", ex.Message);
    }
}
