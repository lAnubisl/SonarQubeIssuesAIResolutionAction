using NUnit.Framework;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ConfigurationValidatorTests
{
    [Test]
    public static void DryRunInputValidation()
    {
        var configurationHelper = TestData.MockConfigurationHelper();

        ConfigurationValidator.Validate(configurationHelper.Object);

        Assert.True(configurationHelper.Object.InputDryRun);
    }

    [Test]
    public static void NormalModeTokenValidation()
    {
        var configurationHelper = TestData.MockConfigurationHelper(inputDryRun: false);

        var ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("COPILOT_CLI_TOKEN", ex.Message);
    }
}
