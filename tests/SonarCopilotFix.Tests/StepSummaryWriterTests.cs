using Moq;
using NUnit.Framework;
using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class StepSummaryWriterTests
{
    [Test]
    public static void MissingSummaryPathDoesNotWriteAFile()
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(gitHubStepSummary: null);
        StepSummaryWriter writer = new(configuration.Object);

        writer.Write(new ActionSummary(TestData.EffortCalculator()));

        configuration.VerifyGet(value => value.GitHubStepSummary, Times.Once);
    }

    [Test]
    public static void EmptyResultWritesFallbackRowsAndSessionMessage()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "summary.md");
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(gitHubStepSummary: path);

        ActionSummary summary = new(TestData.EffortCalculator());
        summary.RecordIssues(4, []);
        new StepSummaryWriter(configuration.Object).Write(summary);

        string text = File.ReadAllText(path);
        Assert.Contains("| n/a | n/a | n/a | no rule groups processed |", text);
        Assert.Contains("Not available because Copilot CLI did not write session information", text);
        Assert.Contains("Pull requests created: `0`", text);
    }

    [Test]
    public static void ResultIncludesPullRequestAndDistinctChangedFiles()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "summary.md");
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(gitHubStepSummary: path);
        ActionSummary summary = new(TestData.EffortCalculator());
        PullRequestSummary result = new(
            TestData.EffortCalculator(),
            new IssueGroup("rule:S1", [TestData.SampleIssue()]),
            "main",
            "fix/rule",
            ["src/B.cs", "src/A.cs", "src/A.cs"],
            "session details")
        {
            PullRequestUrl = "https://github.example/pull/1"
        };
        summary.Add(result);

        new StepSummaryWriter(configuration.Object).Write(summary);

        string text = File.ReadAllText(path);
        Assert.Contains("https://github.example/pull/1", text);
        Assert.Contains("### rule:S1", text);
        Assert.Contains("Files changed: `2`", text);
        Assert.Contains("Pull requests created: `1`", text);
    }

}
