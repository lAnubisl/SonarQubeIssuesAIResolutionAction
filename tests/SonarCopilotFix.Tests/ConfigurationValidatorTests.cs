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

    [Test]
    public static void AcceptsRemoteCopilotProviderWithRequiredSettings()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            inputCopilotModel: "gpt-5.2",
            inputCopilotProviderType: "openai",
            inputCopilotProviderBaseUrl: "https://foundry.example/openai/v1",
            copilotProviderApiKey: "provider-secret");

        ConfigurationValidator.Validate(configurationHelper.Object);
    }

    [Test]
    public static void AcceptsLoopbackCopilotProviderWithoutApiKey()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            inputCopilotModel: "llama3.2",
            inputCopilotProviderBaseUrl: "http://localhost:11434");

        ConfigurationValidator.Validate(configurationHelper.Object);
    }

    [Test]
    public static void AcceptsRemoteOpenAiCompatibleProviderWithoutApiKey()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            inputCopilotModel: "llama3.2",
            inputCopilotProviderType: "openai",
            inputCopilotProviderBaseUrl: "http://10.0.0.5:8000/v1");

        ConfigurationValidator.Validate(configurationHelper.Object);
    }

    [Test]
    public static void RejectsUnsupportedCopilotProviderType()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            inputCopilotModel: "gpt-5.2",
            inputCopilotProviderType: "foundry",
            inputCopilotProviderBaseUrl: "https://foundry.example/openai/v1",
            copilotProviderApiKey: "provider-secret");

        ControlledFailureException ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("openai, azure, anthropic", ex.Message);
    }

    [Test]
    public static void RejectsCopilotProviderWithoutBaseUrl()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            inputCopilotModel: "gpt-5.2",
            inputCopilotProviderType: "openai",
            copilotProviderApiKey: "provider-secret");

        ControlledFailureException ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("copilot_provider_base_url", ex.Message);
    }

    [Test]
    public static void RejectsCopilotProviderWithoutModel()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            inputCopilotProviderType: "openai",
            inputCopilotProviderBaseUrl: "https://foundry.example/openai/v1",
            copilotProviderApiKey: "provider-secret");

        ControlledFailureException ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("copilot_model", ex.Message);
    }

    [Test]
    [TestCase("azure")]
    [TestCase("anthropic")]
    public static void RejectsKeyRequiredCopilotProviderTypesWithoutApiKey(string providerType)
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            inputCopilotModel: "gpt-5.2",
            inputCopilotProviderType: providerType,
            inputCopilotProviderBaseUrl: "https://foundry.example/openai/v1");

        ControlledFailureException ex = Assert.Throws<ControlledFailureException>(() => ConfigurationValidator.Validate(configurationHelper.Object));

        Assert.Contains("COPILOT_PROVIDER_API_KEY", ex.Message);
    }
}
