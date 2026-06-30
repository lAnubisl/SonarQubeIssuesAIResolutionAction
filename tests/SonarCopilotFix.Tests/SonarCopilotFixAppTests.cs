using Moq;
using NUnit.Framework;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class SonarCopilotFixAppTests
{
    [Test]
    public static async Task DryRunAppBehavior()
    {
        var temp = Directory.CreateTempSubdirectory();
        var logger = TestData.MockLogger();
        var configurationHelper = CreateConfigurationHelper(temp.FullName);
        var commandRunner = new CommandRunner(logger.Object, configurationHelper.Object);
        var gitInit = await commandRunner.RunAsync("git", ["init"], temp.FullName);
        Assert.Equal(0, gitInit.ExitCode);
        var options = ActionInputs.FromEnvironment(configurationHelper.Object);
        var app = new SonarCopilotFixApp(
            options,
            configurationHelper.Object,
            logger.Object,
            TestData.MockSonarQubeClient([TestData.SampleIssue()]),
            new CodeSnippetReader(),
            new PromptBuilder(),
            commandRunner,
            new PrBodyBuilder());

        var exitCode = await app.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(temp.FullName, ".sonar-copilot", "issues-prompt.md")));
        Assert.False(Directory.Exists(Path.Combine(temp.FullName, ".git", "refs", "heads", "copilot")));
    }

    [Test]
    public static async Task FetchedIssueLogging()
    {
        var temp = Directory.CreateTempSubdirectory();
        var logger = TestData.MockLogger();
        var configurationHelper = CreateConfigurationHelper(temp.FullName);
        var commandRunner = new CommandRunner(logger.Object, configurationHelper.Object);
        var gitInit = await commandRunner.RunAsync("git", ["init"], temp.FullName);
        Assert.Equal(0, gitInit.ExitCode);
        var options = ActionInputs.FromEnvironment(configurationHelper.Object);
        var app = new SonarCopilotFixApp(
            options,
            configurationHelper.Object,
            logger.Object,
            TestData.MockSonarQubeClient([TestData.SampleIssue()]),
            new CodeSnippetReader(),
            new PromptBuilder(),
            commandRunner,
            new PrBodyBuilder());
        await app.RunAsync();

        logger.Verify(
            value => value.Info("Fetched 1 SonarQube issue(s) (1 total matching issue(s) reported by SonarQube)."),
            Times.Once);
        logger.Verify(
            value => value.Info("Fetched SonarQube issue: key=ISSUE-1, severity=MAJOR, title=Fix this"),
            Times.Once);
    }

    private static Mock<IConfigurationHelper> CreateConfigurationHelper(string workspace) =>
        TestData.MockConfigurationHelper(gitHubWorkspace: workspace);
}
