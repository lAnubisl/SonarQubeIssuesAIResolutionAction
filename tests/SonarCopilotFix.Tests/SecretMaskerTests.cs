using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class SecretMaskerTests
{
    [Test]
    public static void WritesMaskCommandsForNonEmptySecretsAndLogsCompletion()
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(
            sonarToken: "sonar-secret",
            copilotCliToken: null,
            ghCliToken: "github-secret");
        Mock<ILogger> logger = new(MockBehavior.Strict);
        logger.Setup(value => value.Info("Configured log masking for known token secrets."));
        TextWriter original = Console.Out;
        using StringWriter output = new();
        try
        {
            Console.SetOut(output);
            SecretMasker.MaskKnownSecrets(configuration.Object, logger.Object);
        }
        finally
        {
            Console.SetOut(original);
        }

        string text = output.ToString();
        Assert.Contains("::add-mask::sonar-secret", text);
        Assert.Contains("::add-mask::github-secret", text);
        Assert.False(text.Contains("::add-mask::" + Environment.NewLine, StringComparison.Ordinal));
        logger.VerifyAll();
    }
}
