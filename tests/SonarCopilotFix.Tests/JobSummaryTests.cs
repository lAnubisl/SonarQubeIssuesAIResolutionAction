using NUnit.Framework;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class JobSummaryTests
{
    [Test]
    public static void JobSummary()
    {
        var temp = Directory.CreateTempSubdirectory();
        var path = Path.Combine(temp.FullName, "summary.md");
        var configurationHelper = TestData.MockConfigurationHelper(gitHubStepSummary: path);
        var summary = new JobSummary(configurationHelper.Object)
        {
            CopilotSessionSummary = "Total usage est: 1k tokens\nTotal duration: 5s"
        };
        summary.SetSelectedIssues(
        [
            TestData.SampleIssue() with { Effort = "1d 2h" },
            TestData.SampleIssue() with { Effort = "45min" },
            TestData.SampleIssue() with { Effort = null }
        ]);

        summary.Write();

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
        var summary = new JobSummary(TestData.Configuration());
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
        var summary = new JobSummary(TestData.Configuration());
        summary.SetSelectedIssues([TestData.SampleIssue() with { Effort = "0min" }]);

        Assert.Equal("0min", summary.TotalEffortSaved);
    }
}
