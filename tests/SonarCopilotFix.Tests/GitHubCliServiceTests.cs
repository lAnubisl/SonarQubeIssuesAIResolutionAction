using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.Infrastructure.Models;
using SonarCopilotFix.GitHub;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GitHubCliServiceTests
{
    private const string PrBody = "Fixes the following issues:\n\n- ISSUE-1";
    private static readonly string[] PrCreateArguments = ["pr", "create", "--title", "Fix issues", "--body", PrBody, "--base", "main", "--head", "fix/issues", "--draft"];
    [Test]
    public static void GitHubCliEnvironment()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "github-workspace");
        var configurationHelper = TestData.MockConfigurationHelper(
            ghCliToken: "github-secret",
            gitHubWorkspace: workspace);

        var environment = GitHubCliService.BuildEnvironment(configurationHelper.Object);

        Assert.Equal("github-secret", environment["GH_TOKEN"]);
        Assert.Equal("1", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("safe.directory", environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal(Path.GetFullPath(workspace), environment["GIT_CONFIG_VALUE_0"]);
    }

    [Test]
    public static async Task PullRequestFailureIncludesCapturedOutput()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "github-workspace");
        var commandRunner = new Mock<ICommandRunner>(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                "gh",
                It.Is<IEnumerable<string>>(arguments => arguments.SequenceEqual(PrCreateArguments)),
                workspace,
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(
                1,
                "stdout detail",
                "permission denied"));
        var configurationHelper = TestData.MockConfigurationHelper(
            ghCliToken: "github-secret",
            gitHubWorkspace: workspace);
        var service = new GitHubCliService(
            commandRunner.Object,
            configurationHelper.Object,
            TestData.MockLogger().Object);

        var exception = await Assert.ThrowsAsync<ControlledFailureException>(() =>
            service.CreatePullRequestAsync(
                "Fix issues",
                PrBody,
                "main",
                "fix/issues",
                CancellationToken.None));

        Assert.Equal(ExitCodes.GitHubCliFailure, exception.ExitCode);
        Assert.Contains("GitHub CLI failed to create a pull request.", exception.Message);
        Assert.Contains("stdout detail", exception.Message);
        Assert.Contains("permission denied", exception.Message);
        commandRunner.VerifyAll();
    }
}
