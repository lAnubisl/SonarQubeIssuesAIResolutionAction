using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.Infrastructure.Models;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GitHubCliServiceTests
{
    [Test]
    public static void GitHubCliEnvironment()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "github-workspace");
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            ghCliToken: "github-secret",
            gitHubWorkspace: workspace);

        IReadOnlyDictionary<string, string?> environment = GitHubCliService.BuildEnvironment(configurationHelper.Object);

        Assert.Equal("github-secret", environment["GH_TOKEN"]);
        Assert.Equal("1", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("safe.directory", environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal(Path.GetFullPath(workspace), environment["GIT_CONFIG_VALUE_0"]);
    }

    [Test]
    public static async Task PullRequestFailureIncludesCapturedOutput()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "github-workspace");
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            ghCliToken: "github-secret",
            gitHubWorkspace: workspace);
        Mock<IPrBodyBuilder> prBodyBuilder = new(MockBehavior.Strict);
        PullRequestSummary pullRequestSummary = new(
            new IssueGroup("csharpsquid:S1", [TestData.SampleIssue()]),
            "main",
            "fix/issues",
            ["src/A.cs"],
            "Total usage est: 1k tokens");
        string[] expectedArguments = new[]
        {
            "pr", "create",
            "--title", "Fix SonarQube rule csharpsquid:S1 (1 issue(s))",
            "--body", "generated body",
            "--base", "main",
            "--head", "fix/issues",
            "--draft"
        };
        prBodyBuilder.Setup(value => value.Build(pullRequestSummary)).Returns("generated body");
        commandRunner
            .Setup(value => value.RunAsync(
                "gh",
                It.Is<IEnumerable<string>>(arguments => arguments.SequenceEqual(expectedArguments)),
                workspace,
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(
                1,
                "stdout detail",
                "permission denied"));
        GitHubCliService service = new(
            commandRunner.Object,
            configurationHelper.Object,
            TestData.MockLogger().Object,
            prBodyBuilder.Object);

        ControlledFailureException exception = await Assert.ThrowsAsync<ControlledFailureException>(() =>
            service.CreatePullRequestAsync(
                pullRequestSummary,
                CancellationToken.None));

        Assert.Equal(ExitCodes.GitHubCliFailure, exception.ExitCode);
        Assert.Contains("GitHub CLI failed to create a pull request.", exception.Message);
        Assert.Contains("stdout detail", exception.Message);
        Assert.Contains("permission denied", exception.Message);
        commandRunner.VerifyAll();
        prBodyBuilder.VerifyAll();
    }

    [Test]
    public static async Task AuthenticationFailureIsMappedToControlledFailure()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "github-workspace");
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                "gh",
                It.Is<IEnumerable<string>>(arguments => arguments.SequenceEqual(new[] { "auth", "setup-git" })),
                workspace,
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(1, "", "failed"));
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(gitHubWorkspace: workspace);
        GitHubCliService service = new(
            commandRunner.Object,
            configuration.Object,
            Mock.Of<ILogger>(),
            Mock.Of<IPrBodyBuilder>());

        ControlledFailureException exception = await Assert.ThrowsAsync<ControlledFailureException>(
            () => service.SetupGitAuthenticationAsync(CancellationToken.None));

        Assert.Equal(ExitCodes.GitHubCliFailure, exception.ExitCode);
        commandRunner.VerifyAll();
    }

    [Test]
    public static async Task SuccessfulPullRequestStoresUrlAndLogsIt()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "github-workspace");
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        Mock<IPrBodyBuilder> bodyBuilder = new(MockBehavior.Strict);
        Mock<ILogger> logger = new(MockBehavior.Strict);
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(
            gitHubWorkspace: workspace,
            inputPullRequestDraft: false);
        PullRequestSummary summary = new(
            new IssueGroup("rule:S1", [TestData.SampleIssue()]),
            "main",
            "fix/rule",
            [],
            "");
        bodyBuilder.Setup(value => value.Build(summary)).Returns("body");
        commandRunner
            .Setup(value => value.RunAsync(
                "gh",
                It.Is<IEnumerable<string>>(arguments =>
                    arguments.SequenceEqual(new[]
                    {
                        "pr", "create", "--title", "Fix SonarQube rule rule:S1 (1 issue(s))",
                        "--body", "body", "--base", "main", "--head", "fix/rule"
                    })),
                workspace,
                It.IsAny<IReadOnlyDictionary<string, string?>>(),
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(0, "notice\nhttps://github.example/pull/7\n", ""));
        logger.Setup(value => value.Info("Created pull request: https://github.example/pull/7"));
        GitHubCliService service = new(commandRunner.Object, configuration.Object, logger.Object, bodyBuilder.Object);

        await service.CreatePullRequestAsync(summary, CancellationToken.None);

        Assert.Equal("https://github.example/pull/7", summary.PullRequestUrl);
        commandRunner.VerifyAll();
        bodyBuilder.VerifyAll();
        logger.VerifyAll();
    }
}
