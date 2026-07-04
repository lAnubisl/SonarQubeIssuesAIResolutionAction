using Moq;
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
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper();
        PullRequestSummary summary = new(
            new IssueGroup("csharpsquid:S1", [TestData.SampleIssue()]),
            "main",
            "copilot/sonar/proj/20260101000000",
            ["src/A.cs"],
            "Total usage est: 29.3k tokens\nTotal duration: 42s");

        string body = new PrBodyBuilder(configurationHelper.Object).Build(summary);

        Assert.Contains("Human review is required", body);
        Assert.Contains("ISSUE-1", body);
        Assert.Contains("src/A.cs", body);
        Assert.Contains("Validation is delegated to the repository's pull request checks", body);
        Assert.Contains("Copilot Session Summary", body);
        Assert.Contains("29.3k", body);
        Assert.Contains("42s", body);
        Assert.Contains("Total effort saved | `5min`", body);
    }

    [Test]
    public static void MissingOptionalValuesUseFallbackText()
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(inputBaseBranch: null);
        PullRequestSummary summary = new(
            new IssueGroup("rule:S1", [TestData.SampleIssue() with { Line = null }]),
            null!,
            null!,
            [],
            "");

        string body = new PrBodyBuilder(configuration.Object).Build(summary);

        Assert.Contains("Base branch | `not detected`", body);
        Assert.Contains("Generated branch | `not created`", body);
        Assert.Contains("Copilot CLI did not write session information", body);
        Assert.Contains("line `not specified`", body);
        Assert.Contains("- No changed files were detected.", body);
    }
}
