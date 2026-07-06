using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.Tests;

[TestFixture]
internal sealed class SonarQubeHttpClientTests
{
    [TestCase("https://sonar.example/custom", "https://sonar.example/custom/")]
    [TestCase("https://sonar.example/custom/", "https://sonar.example/custom/")]
    public static void ConstructorUsesNormalizedConfiguredBaseAddressAndToken(
        string configuredUrl,
        string expectedUrl)
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(
            inputSonarHostUrl: configuredUrl,
            sonarToken: "secret");

        using SonarQubeHttpClient client = new(configuration.Object);

        Assert.Equal(new Uri(expectedUrl), client.BaseAddress);
        configuration.VerifyGet(value => value.InputSonarHostUrl, Times.Once);
        configuration.VerifyGet(value => value.SonarToken, Times.Once);
    }
}
