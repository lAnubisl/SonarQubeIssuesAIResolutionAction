using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ConfigurationValidatorTests
{
    [Test]
    public static void RequiresCopilotToken()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(copilotCliToken: null);

        ControlledFailureException ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("COPILOT_CLI_TOKEN", ex.Message);
    }

    [Test]
    public static void RequiresGitHubCliToken()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            ghCliToken: null);

        ControlledFailureException ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("GH_CLI_TOKEN", ex.Message);
    }

    [TestCase("CODE_SMELL")]
    [TestCase("BUG")]
    [TestCase("VULNERABILITY")]
    public static void AcceptsSupportedIssueType(string issueType)
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(inputType: issueType);

        ConfigurationValidator.Validate(configurationHelper.Object);
    }

    [Test]
    public static void RejectsUnsupportedIssueType()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(inputType: "SECURITY_HOTSPOT");

        ControlledFailureException ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("CODE_SMELL, BUG, VULNERABILITY", ex.Message);
    }
}
