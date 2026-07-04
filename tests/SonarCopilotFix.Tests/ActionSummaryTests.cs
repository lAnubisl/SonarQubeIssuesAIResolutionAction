using Moq;
using NUnit.Framework;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ActionSummaryTests
{
    [Test]
    public static void WritesActionSummary()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory();
        string path = Path.Combine(temp.FullName, "summary.md");
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(gitHubStepSummary: path);
        ActionSummary summary = new();
        summary.SetSelectedIssues(
        [
            TestData.SampleIssue() with { Effort = "1d 2h" },
            TestData.SampleIssue() with { Effort = "45min" },
            TestData.SampleIssue() with { Effort = null }
        ]);
        PullRequestSummary pullRequestSummary = new(
            new IssueGroup("csharpsquid:S1234", [TestData.SampleIssue()]),
            "main",
            "fix-branch",
            ["fixed.cs"],
            "Total usage est: 1k tokens\nTotal duration: 5s")
        {
            PullRequestUrl = "https://github.example/pr/1"
        };
        summary.Add(pullRequestSummary);

        new StepSummaryWriter(configurationHelper.Object).Write(summary);

        string contents = File.ReadAllText(path);
        Assert.Contains("Copilot Session Summary", contents);
        Assert.Contains("1k tokens", contents);
        Assert.Contains("5s", contents);
        Assert.Contains("Issues selected: `3`", contents);
        Assert.Contains("Rule groups selected: `1`", contents);
        Assert.Contains("Total effort saved: `1d 2h 45min`", contents);
    }

    [Test]
    public static void UnavailableEffort()
    {
        ActionSummary summary = new();
        summary.SetSelectedIssues(
        [
            TestData.SampleIssue() with { Effort = null },
            TestData.SampleIssue() with { Effort = "unknown" }
        ]);

        Assert.Equal("not available", summary.TotalEffortSaved);
    }

    [Test]
    public static void ZeroEffort()
    {
        ActionSummary summary = new();
        summary.SetSelectedIssues([TestData.SampleIssue() with { Effort = "0min" }]);

        Assert.Equal("0min", summary.TotalEffortSaved);
    }

    [Test]
    public static void WritesNotCreatedWhenGroupHasNoPullRequest()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory();
        string path = Path.Combine(temp.FullName, "summary.md");
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(gitHubStepSummary: path);
        ActionSummary summary = new();
        summary.Add(new PullRequestSummary(
            new IssueGroup("csharpsquid:S1", [TestData.SampleIssue()]),
            "main",
            "fix-branch",
            [],
            ""));

        new StepSummaryWriter(configurationHelper.Object).Write(summary);

        string contents = File.ReadAllText(path);
        Assert.Contains("| `csharpsquid:S1` | `ISSUE-1` | `fix-branch` | not created |", contents);
    }
}
