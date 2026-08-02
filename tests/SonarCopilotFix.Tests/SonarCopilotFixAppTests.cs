using Moq;
using NUnit.Framework;
using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class SonarCopilotFixAppTests
{
    [Test]
    public static async Task FetchedIssueLogging()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory();
        Mock<ILogger> logger = TestData.MockLogger();
        Mock<IConfigurationHelper> configurationHelper = CreateConfigurationHelper(temp.FullName);
        WorkflowCommandRunner commandRunner = new();
        PrBodyBuilder prBodyBuilder = new(configurationHelper.Object);
        SonarCopilotFixApp app = new(
            configurationHelper.Object,
            logger.Object,
            TestData.MockSonarQubeClient([TestData.SampleIssue()]),
            new PromptBuilder(configurationHelper.Object),
            new StepSummaryWriter(configurationHelper.Object),
            new GitService(commandRunner, configurationHelper.Object),
            new GitHubCliService(commandRunner, configurationHelper.Object, logger.Object, prBodyBuilder),
            new CopilotCliRunner(commandRunner, configurationHelper.Object, logger.Object),
            TestData.EffortCalculator());
        await app.RunAsync();

        logger.Verify(
            value => value.Info("Fetched 1 SonarQube issue(s) (1 total matching issue(s) reported by SonarQube)."),
            Times.Once);
        logger.Verify(
            value => value.Info("Fetched SonarQube issue: key=ISSUE-1, severity=MAJOR, title=Fix this"),
            Times.Once);
    }

    [Test]
    public static async Task NormalRunCompletesAnIsolatedWorkflowPerRuleGroup()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory();
        Mock<ILogger> logger = TestData.MockLogger();
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            copilotGitHubToken: "copilot",
            gitHubToken: "github",
            gitHubWorkspace: temp.FullName,
            gitHubOutput: Path.Combine(temp.FullName, "output.txt"),
            gitHubStepSummary: Path.Combine(temp.FullName, "summary.md"));
        WorkflowCommandRunner commandRunner = new();
        SonarIssue secondIssue = TestData.SampleIssue() with
        {
            Key = "ISSUE-2",
            Message = "Fix that too",
            IssueUrl = new Uri("https://sonar.example/project/issues?id=proj&issues=ISSUE-2&open=ISSUE-2")
        };
        SonarIssue thirdIssue = TestData.SampleIssue() with
        {
            Key = "ISSUE-3",
            RuleKey = "csharpsquid:S2",
            Message = "Fix a different rule",
            IssueUrl = new Uri("https://sonar.example/project/issues?id=proj&issues=ISSUE-3&open=ISSUE-3")
        };
        PrBodyBuilder prBodyBuilder = new(configurationHelper.Object);
        SonarCopilotFixApp app = new(
            configurationHelper.Object,
            logger.Object,
            TestData.MockSonarQubeClient([TestData.SampleIssue(), secondIssue, thirdIssue]),
            new PromptBuilder(configurationHelper.Object),
            new StepSummaryWriter(configurationHelper.Object),
            new GitService(commandRunner, configurationHelper.Object),
            new GitHubCliService(commandRunner, configurationHelper.Object, logger.Object, prBodyBuilder),
            new CopilotCliRunner(commandRunner, configurationHelper.Object, logger.Object),
            TestData.EffortCalculator());

        int exitCode = await app.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(2, commandRunner.CreatedBranches.Count);
        Assert.Contains("/csharpsquid-S1/", commandRunner.CreatedBranches[0]);
        Assert.Contains("/csharpsquid-S2/", commandRunner.CreatedBranches[1]);
        Assert.Equal(2, commandRunner.CopilotSessionIds.Count);
        Assert.Equal(2, commandRunner.CopilotPrompts.Count);
        Assert.Contains("ISSUE-1", commandRunner.CopilotPrompts[0]);
        Assert.Contains("ISSUE-2", commandRunner.CopilotPrompts[0]);
        Assert.Contains("ISSUE-3", commandRunner.CopilotPrompts[1]);
        Assert.False(Directory.EnumerateFiles(
            Path.Combine(temp.FullName, ".sonar-copilot"),
            "*-prompt.md").Any());
        Assert.False(string.Equals(
            commandRunner.CopilotSessionIds[0],
            commandRunner.CopilotSessionIds[1],
            StringComparison.Ordinal));
        Assert.Equal(2, commandRunner.CommitCount);
        Assert.Equal(2, commandRunner.PushCount);
        Assert.Equal(2, commandRunner.PullRequestCount);
        Assert.Equal(3, commandRunner.SwitchToMainCount);
        Assert.Equal(2, commandRunner.PullRequestBodies.Count);

        string firstPrBody = commandRunner.PullRequestBodies[0];
        Assert.Contains("ISSUE-1", firstPrBody);
        Assert.Contains("ISSUE-2", firstPrBody);
        Assert.False(firstPrBody.Contains("ISSUE-3", StringComparison.Ordinal));
    }

    private static Mock<IConfigurationHelper> CreateConfigurationHelper(string workspace) =>
        TestData.MockConfigurationHelper(
            gitHubWorkspace: workspace,
            gitHubOutput: Path.Combine(workspace, "output.txt"),
            gitHubStepSummary: Path.Combine(workspace, "summary.md"));

}
