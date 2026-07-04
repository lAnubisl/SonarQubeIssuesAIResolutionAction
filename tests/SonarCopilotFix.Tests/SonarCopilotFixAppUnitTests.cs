using Moq;
using NUnit.Framework;
using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.Tests;

[TestFixture]
internal sealed class SonarCopilotFixAppUnitTests
{
    [Test]
    public static async Task NoIssuesWritesSummaryAndReturnsSuccess()
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(inputFailIfNoIssues: false);
        Mock<ILogger> logger = new();
        Mock<ISonarQubeClient> sonar = new(MockBehavior.Strict);
        Mock<IStepSummaryWriter> summaryWriter = new(MockBehavior.Strict);
        sonar.Setup(value => value.GetIssuesAsync(CancellationToken.None))
            .ReturnsAsync(new SonarIssueSearchResult(0, []));
        summaryWriter.Setup(value => value.Write(It.Is<ActionSummary>(summary =>
            summary.IssuesFound == 0 && summary.IssuesSelected == 0)));
        SonarCopilotFixApp app = CreateApp(
            configuration.Object,
            logger.Object,
            sonar.Object,
            summaryWriter.Object);

        int exitCode = await app.RunAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        sonar.VerifyAll();
        summaryWriter.VerifyAll();
    }

    [Test]
    public static async Task NoIssuesCanBeConfiguredAsFailure()
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(inputFailIfNoIssues: true);
        Mock<ISonarQubeClient> sonar = new(MockBehavior.Strict);
        Mock<IStepSummaryWriter> summaryWriter = new(MockBehavior.Strict);
        sonar.Setup(value => value.GetIssuesAsync(CancellationToken.None))
            .ReturnsAsync(new SonarIssueSearchResult(0, []));
        summaryWriter.Setup(value => value.Write(It.IsAny<ActionSummary>()));
        SonarCopilotFixApp app = CreateApp(
            configuration.Object,
            Mock.Of<ILogger>(),
            sonar.Object,
            summaryWriter.Object);

        ControlledFailureException exception = await Assert.ThrowsAsync<ControlledFailureException>(
            () => app.RunAsync());

        Assert.Equal(ExitCodes.NoIssuesFound, exception.ExitCode);
        summaryWriter.VerifyAll();
    }

    [Test]
    public static async Task PreExistingChangesStopBeforeAuthenticationOrCopilot()
    {
        SonarIssue issue = TestData.SampleIssue();
        IssueGroup group = new(issue.RuleKey, [issue]);
        Mock<ISonarQubeClient> sonar = new(MockBehavior.Strict);
        sonar.Setup(value => value.GetIssuesAsync(CancellationToken.None))
            .ReturnsAsync(new SonarIssueSearchResult(1, [issue]));
        sonar.Setup(value => value.EnrichIssues(It.IsAny<IReadOnlyList<SonarIssue>>())).Returns([issue]);
        sonar.Setup(value => value.GroupIssuesByRule(It.IsAny<IReadOnlyList<SonarIssue>>())).Returns([group]);
        Mock<IGitService> git = new(MockBehavior.Strict);
        git.Setup(value => value.ResolveBaseBranchAsync(CancellationToken.None)).ReturnsAsync("main");
        git.Setup(value => value.GetChangedFilesAsync(true, CancellationToken.None)).ReturnsAsync(["unrelated.cs"]);
        Mock<IGitHubCliService> github = new(MockBehavior.Strict);
        Mock<ICopilotCliRunner> copilot = new(MockBehavior.Strict);
        SonarCopilotFixApp app = CreateApp(
            TestData.MockConfigurationHelper().Object,
            Mock.Of<ILogger>(),
            sonar.Object,
            Mock.Of<IStepSummaryWriter>(),
            git.Object,
            github.Object,
            copilot.Object);

        ControlledFailureException exception = await Assert.ThrowsAsync<ControlledFailureException>(
            () => app.RunAsync());

        Assert.Equal(ExitCodes.GitFailure, exception.ExitCode);
        github.VerifyNoOtherCalls();
        copilot.VerifyNoOtherCalls();
    }

    [Test]
    public static async Task GroupWithoutChangesReturnsToBaseAndWritesResult()
    {
        SonarIssue issue = TestData.SampleIssue();
        IssueGroup group = new(issue.RuleKey, [issue]);
        Mock<ISonarQubeClient> sonar = new(MockBehavior.Strict);
        sonar.Setup(value => value.GetIssuesAsync(CancellationToken.None))
            .ReturnsAsync(new SonarIssueSearchResult(1, [issue]));
        sonar.Setup(value => value.EnrichIssues(It.IsAny<IReadOnlyList<SonarIssue>>())).Returns([issue]);
        sonar.Setup(value => value.GroupIssuesByRule(It.IsAny<IReadOnlyList<SonarIssue>>())).Returns([group]);
        Mock<IGitService> git = new(MockBehavior.Strict);
        git.Setup(value => value.ResolveBaseBranchAsync(CancellationToken.None)).ReturnsAsync("main");
        git.SetupSequence(value => value.GetChangedFilesAsync(true, CancellationToken.None))
            .ReturnsAsync([])
            .ReturnsAsync([]);
        git.Setup(value => value.SwitchBranchAsync("main", CancellationToken.None)).Returns(Task.CompletedTask);
        git.Setup(value => value.BuildBranchName(issue.RuleKey, It.IsAny<DateTimeOffset>())).Returns("fix/rule");
        git.Setup(value => value.CreateBranchAsync("fix/rule", CancellationToken.None)).Returns(Task.CompletedTask);
        git.SetupSequence(value => value.GetHeadCommitAsync(CancellationToken.None))
            .ReturnsAsync("abc")
            .ReturnsAsync("abc");
        Mock<IPromptBuilder> prompts = new(MockBehavior.Strict);
        prompts.Setup(value => value.Build(group.Issues, "fix/rule", "main")).Returns("prompt");
        Mock<IGitHubCliService> github = new(MockBehavior.Strict);
        github.Setup(value => value.SetupGitAuthenticationAsync(CancellationToken.None)).Returns(Task.CompletedTask);
        Mock<ICopilotCliRunner> copilot = new(MockBehavior.Strict);
        copilot.Setup(value => value.RunAsync("prompt", CancellationToken.None)).ReturnsAsync("session");
        Mock<IStepSummaryWriter> summaryWriter = new(MockBehavior.Strict);
        summaryWriter.Setup(value => value.Write(It.Is<ActionSummary>(summary =>
            summary.PullRequestSummaries.Count == 1
            && summary.PullRequestSummaries[0].ChangedFiles.Count == 0)));
        SonarCopilotFixApp app = new(
            TestData.MockConfigurationHelper().Object,
            Mock.Of<ILogger>(),
            sonar.Object,
            prompts.Object,
            summaryWriter.Object,
            new AppDependencies(git.Object, github.Object, copilot.Object));

        int exitCode = await app.RunAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        git.Verify(value => value.SwitchBranchAsync("main", CancellationToken.None), Times.Exactly(2));
        github.Verify(value => value.CreatePullRequestAsync(It.IsAny<PullRequestSummary>(), It.IsAny<CancellationToken>()), Times.Never);
        summaryWriter.VerifyAll();
    }

    private static SonarCopilotFixApp CreateApp(
        IConfigurationHelper configuration,
        ILogger logger,
        ISonarQubeClient sonar,
        IStepSummaryWriter summaryWriter,
        IGitService? git = null,
        IGitHubCliService? github = null,
        ICopilotCliRunner? copilot = null) =>
        new(
            configuration,
            logger,
            sonar,
            Mock.Of<IPromptBuilder>(),
            summaryWriter,
            new AppDependencies(
                git ?? Mock.Of<IGitService>(),
                github ?? Mock.Of<IGitHubCliService>(),
                copilot ?? Mock.Of<ICopilotCliRunner>()));
}
