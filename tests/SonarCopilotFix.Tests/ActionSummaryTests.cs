using NUnit.Framework;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ActionSummaryTests
{
    [Test]
    public static void WritesActionSummary()
    {
        var temp = Directory.CreateTempSubdirectory();
        var path = Path.Combine(temp.FullName, "summary.md");
        var configurationHelper = TestData.MockConfigurationHelper(gitHubStepSummary: path);
        var summary = new ActionSummary();
        summary.SetSelectedIssues(
        [
            TestData.SampleIssue() with { Effort = "1d 2h" },
            TestData.SampleIssue() with { Effort = "45min" },
            TestData.SampleIssue() with { Effort = null }
        ]);
        var pullRequestSummary = new PullRequestSummary(
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

        var contents = File.ReadAllText(path);
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
        var summary = new ActionSummary();
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
        var summary = new ActionSummary();
        summary.SetSelectedIssues([TestData.SampleIssue() with { Effort = "0min" }]);

        Assert.Equal("0min", summary.TotalEffortSaved);
    }

    [Test]
    public static void WritesNotCreatedWhenGroupHasNoPullRequest()
    {
        var temp = Directory.CreateTempSubdirectory();
        var path = Path.Combine(temp.FullName, "summary.md");
        var configurationHelper = TestData.MockConfigurationHelper(gitHubStepSummary: path);
        var summary = new ActionSummary();
        summary.Add(new PullRequestSummary(
            new IssueGroup("csharpsquid:S1", [TestData.SampleIssue()]),
            "main",
            "fix-branch",
            [],
            ""));

        new StepSummaryWriter(configurationHelper.Object).Write(summary);

        var contents = File.ReadAllText(path);
        Assert.Contains("| `csharpsquid:S1` | `ISSUE-1` | `fix-branch` | not created |", contents);
    }
}
