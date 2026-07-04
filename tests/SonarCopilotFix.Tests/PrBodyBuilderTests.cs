using NUnit.Framework;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class PrBodyBuilderTests
{
    [Test]
    public static void PrBody()
    {
        IConfigurationHelper configurationHelper = TestData.Configuration();
        PullRequestSummary summary = new(
            new IssueGroup("csharpsquid:S1", [TestData.SampleIssue()]),
            "main",
            "copilot/sonar/proj/20260101000000",
            ["src/A.cs"],
            "Total usage est: 29.3k tokens\nTotal duration: 42s");

        string body = new PrBodyBuilder(configurationHelper).Build(summary);

        Assert.Contains("Human review is required", body);
        Assert.Contains("ISSUE-1", body);
        Assert.Contains("src/A.cs", body);
        Assert.Contains("Validation is delegated to the repository's pull request checks", body);
        Assert.Contains("Copilot Session Summary", body);
        Assert.Contains("29.3k", body);
        Assert.Contains("42s", body);
        Assert.Contains("Total effort saved | `5min`", body);
    }
}
