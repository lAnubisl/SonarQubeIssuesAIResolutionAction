using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.Tests;

[TestFixture]
internal sealed class SonarQubeHttpClientTests
{
    [Test]
    public static void ConstructorUsesConfiguredBaseAddressAndToken()
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(
            inputSonarHostUrl: "https://sonar.example/custom/",
            sonarToken: "secret");

        using SonarQubeHttpClient client = new(configuration.Object);

        Assert.Equal(new Uri("https://sonar.example/custom"), client.BaseAddress);
        configuration.VerifyGet(value => value.InputSonarHostUrl, Times.Once);
        configuration.VerifyGet(value => value.SonarToken, Times.Once);
    }
}
