using System.ComponentModel;
using Moq;
using NUnit.Framework;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.Infrastructure.Models;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CopilotCliRunnerTests
{
    [Test]
    public static void DefaultCopilotCliArguments()
    {
        IReadOnlyList<string> restricted = CopilotCliRunner.BuildArguments(
            TestData.MockConfigurationHelper(inputCopilotModel: "gpt-5.2").Object,
            "Fix the selected issue.");
        CollectionAssert.AreEqual(
            [
                "--prompt", "Fix the selected issue.", "--no-ask-user", "--no-color",
                "--model", "gpt-5.2", "--allow-tool=write", "--deny-tool=shell(git commit)"
            ],
            restricted);
    }

    [Test]
    public static void RestrictedCopilotCliArguments()
    {
        IReadOnlyList<string> restricted = CopilotCliRunner.BuildArguments(
            TestData.MockConfigurationHelper(
                inputCopilotAllowedTools: ["shell(dotnet:*)", "shell(python:*)"]).Object,
            "Fix it.");

        Assert.True(restricted.Contains("--allow-tool=write,shell(dotnet:*),shell(python:*)"));
        Assert.True(restricted.Contains("--deny-tool=shell(git commit)"));
        Assert.False(restricted.Contains("--allow-all-tools"));
    }

    [Test]
    public static void AllowAllCopilotCliArguments()
    {
        IReadOnlyList<string> unrestricted = CopilotCliRunner.BuildArguments(
            TestData.MockConfigurationHelper(
                inputCopilotAllowedTools: ["shell(dotnet:*)"],
                inputCopilotAllowAllTools: true).Object,
            "Fix it.");
        Assert.True(unrestricted.Contains("--allow-all-tools"));
        Assert.True(unrestricted.Contains("--deny-tool=shell(git commit)"));
        Assert.False(unrestricted.Any(argument => argument.StartsWith("--allow-tool=", StringComparison.Ordinal)));
    }

    [Test]
    public static void MalformedCopilotToolPattern()
    {
        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
            CopilotCliRunner.BuildArguments(
                TestData.MockConfigurationHelper(inputCopilotAllowedTools: ["shell(dotnet:*"]).Object,
                "Fix it."));

        Assert.Equal(ExitCodes.ConfigurationError, exception.ExitCode);
    }

    [Test]
    public static async Task SuccessfulRunUsesGuardedEnvironmentAndReturnsSessionSummary()
    {
        using TempDirectory temp = new();
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        Mock<ILogger> logger = new(MockBehavior.Strict);
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(
            copilotCliToken: "token",
            gitHubWorkspace: temp.Path);
        commandRunner
            .Setup(value => value.RunAsync(
                "copilot",
                It.Is<IEnumerable<string>>(arguments =>
                    arguments.Contains("--prompt") && arguments.Contains("fix it") && arguments.Contains("--session-id")),
                temp.Path,
                It.Is<IReadOnlyDictionary<string, string?>>(environment =>
                    environment["COPILOT_GITHUB_TOKEN"] == "token"
                    && environment["COPILOT_AUTO_UPDATE"] == "false"
                    && environment["GIT_CONFIG_KEY_0"] == "core.hooksPath"),
                It.IsAny<Action<string>>(),
                It.IsAny<Action<string>>(),
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(0, "done", " session summary "));
        logger.Setup(value => value.Info("GitHub Copilot CLI completed."));
        CopilotCliRunner runner = new(commandRunner.Object, configuration.Object, logger.Object);

        string result = await runner.RunAsync("fix it", CancellationToken.None);

        Assert.Equal("session summary", result);
        Assert.True(File.Exists(Path.Combine(temp.Path, ".sonar-copilot", "copilot-git-hooks", "pre-commit")));
        commandRunner.VerifyAll();
        logger.VerifyAll();
    }

    [Test]
    public static async Task CustomProviderRunPassesProviderEnvironmentToCopilot()
    {
        using TempDirectory temp = new();
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        Mock<ILogger> logger = new(MockBehavior.Strict);
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(
            inputCopilotModel: "foundry-deployment",
            inputCopilotProviderType: "openai",
            inputCopilotProviderBaseUrl: "https://foundry.example/openai/v1",
            inputCopilotOffline: true,
            copilotProviderApiKey: "provider-secret",
            gitHubWorkspace: temp.Path);
        commandRunner
            .Setup(value => value.RunAsync(
                "copilot",
                It.IsAny<IEnumerable<string>>(),
                temp.Path,
                It.Is<IReadOnlyDictionary<string, string?>>(environment =>
                    environment["COPILOT_PROVIDER_BASE_URL"] == "https://foundry.example/openai/v1"
                    && environment["COPILOT_PROVIDER_TYPE"] == "openai"
                    && environment["COPILOT_PROVIDER_API_KEY"] == "provider-secret"
                    && environment["COPILOT_MODEL"] == "foundry-deployment"
                    && environment["COPILOT_OFFLINE"] == "true"),
                It.IsAny<Action<string>>(),
                It.IsAny<Action<string>>(),
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(0, "done", "session"));
        logger.Setup(value => value.Info("GitHub Copilot CLI completed."));
        CopilotCliRunner runner = new(commandRunner.Object, configuration.Object, logger.Object);

        string result = await runner.RunAsync("fix it", CancellationToken.None);

        Assert.Equal("session", result);
        commandRunner.VerifyAll();
        logger.VerifyAll();
    }

    [Test]
    public static async Task NonZeroExitIsMappedToControlledFailure()
    {
        using TempDirectory temp = new();
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                "copilot",
                It.IsAny<IEnumerable<string>>(),
                temp.Path,
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                It.IsAny<Action<string>>(),
                It.IsAny<Action<string>>(),
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(9, "", "permission denied"));
        CopilotCliRunner runner = new(
            commandRunner.Object,
            TestData.MockConfigurationHelper(gitHubWorkspace: temp.Path).Object,
            Mock.Of<ILogger>());

        ControlledFailureException exception = await Assert.ThrowsAsync<ControlledFailureException>(
            () => runner.RunAsync("fix it", CancellationToken.None));

        Assert.Equal(ExitCodes.CopilotFailure, exception.ExitCode);
        Assert.Contains("exit code 9", exception.Message);
        Assert.Contains("permission denied", exception.Message);
    }

    [Test]
    public static async Task MissingExecutableIsMappedToControlledFailure()
    {
        using TempDirectory temp = new();
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                It.IsAny<Action<string>>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Win32Exception("not found"));
        CopilotCliRunner runner = new(
            commandRunner.Object,
            TestData.MockConfigurationHelper(gitHubWorkspace: temp.Path).Object,
            Mock.Of<ILogger>());

        ControlledFailureException exception = await Assert.ThrowsAsync<ControlledFailureException>(
            () => runner.RunAsync("fix it", CancellationToken.None));

        Assert.Equal(ExitCodes.CopilotFailure, exception.ExitCode);
        Assert.Contains("could not be started", exception.Message);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Path = Directory.CreateTempSubdirectory().FullName;
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
