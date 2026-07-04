using Moq;
using NUnit.Framework;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube.Models;

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

        writer.Write(new ActionSummary());

        configuration.VerifyGet(value => value.GitHubStepSummary, Times.Once);
    }

    [Test]
    public static void EmptyResultWritesFallbackRowsAndSessionMessage()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "summary.md");
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(gitHubStepSummary: path);

        new StepSummaryWriter(configuration.Object).Write(new ActionSummary { IssuesFound = 4 });

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
        ActionSummary summary = new();
        PullRequestSummary result = new(
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

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Path = Directory.CreateTempSubdirectory().FullName;
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
