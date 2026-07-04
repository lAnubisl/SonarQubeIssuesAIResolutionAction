using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.Infrastructure.Models;

namespace SonarCopilotFix.Tests;

[TestFixture]
internal sealed class CommandRunnerTests
{
    [Test]
    public static void TokenIsolationEnvironment()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            sonarToken: "sonar-secret",
            safeEnvironmentVariables: new Dictionary<string, string?>
            {
                ["PATH"] = "test-path",
                ["DOTNET_ROOT"] = "/opt/dotnet",
                ["JAVA_HOME"] = "/opt/java"
            });
        CommandRunner commandRunner = new(TestData.MockLogger().Object, configurationHelper.Object);

        IReadOnlyDictionary<string, string> safe = commandRunner.BuildSafeEnvironment(new Dictionary<string, string?> { ["GH_TOKEN"] = "github-secret" });

        Assert.True(safe.ContainsKey("GH_TOKEN"));
        Assert.Equal("test-path", safe["PATH"]);
        Assert.Equal("/opt/dotnet", safe["DOTNET_ROOT"]);
        Assert.Equal("/opt/java", safe["JAVA_HOME"]);
        Assert.False(safe.ContainsKey("SONAR_TOKEN"));
        Assert.False(safe.ContainsKey("COPILOT_CLI_TOKEN"));
    }

    [Test]
    public static async Task CommandOutputForwarding()
    {
        List<string> received = [];
        Mock<IConfigurationHelper> configurationHelper = TestData.MockSystemConfigurationHelper();
        CommandRunner commandRunner = new(TestData.MockLogger().Object, configurationHelper.Object);

        CommandResult result = await commandRunner.RunAsync(
            "dotnet",
            ["--version"],
            Directory.GetCurrentDirectory(),
            cancellationToken: CancellationToken.None,
            standardOutputReceived: line => received.Add(line));

        Assert.Equal(0, result.ExitCode);
        Assert.True(received.Count > 0);
        Assert.Equal(result.StandardOutput.Trim(), string.Join(Environment.NewLine, received));
    }

    [Test]
    public static async Task CommandDetailLogging()
    {
        Mock<ILogger> logger = TestData.MockLogger();
        Mock<IConfigurationHelper> configurationHelper = TestData.MockSystemConfigurationHelper();
        CommandRunner commandRunner = new(logger.Object, configurationHelper.Object);

        CommandResult result = await commandRunner.RunAsync(
            "dotnet",
            ["--version"],
            Directory.GetCurrentDirectory(),
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        logger.Verify(value => value.Info("Starting command 'dotnet --version'."), Times.Once);
        logger.Verify(value => value.Info("Command 'dotnet' exited with code 0."), Times.Once);
    }
}
