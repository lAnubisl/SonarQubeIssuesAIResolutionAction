using NUnit.Framework;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ActionInputsTests
{
    [Test]
    public static void DryRunInputValidation()
    {
        var configurationHelper = TestData.MockConfigurationHelper();

        var options = ActionInputs.FromEnvironment(configurationHelper.Object);

        Assert.True(options.DryRun);
    }

    [Test]
    public static void NormalModeTokenValidation()
    {
        var configurationHelper = TestData.MockConfigurationHelper(inputDryRun: false);

        var ex = Assert.Throws<ControlledFailureException>(() => ActionInputs.FromEnvironment(configurationHelper.Object));

        Assert.Contains("COPILOT_CLI_TOKEN", ex.Message);
    }
}
