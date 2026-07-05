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
            new IssueGroup(
                "csharpsquid:S1",
                [TestData.SampleIssue()],
                new SonarRule(
                    [new SonarRuleDescriptionSection("root_cause", "<p>This code is difficult to maintain.</p>")],
                    "Avoid difficult code")),
            "main",
            "copilot/sonar/proj/20260101000000",
            ["src/A.cs"],
            "Total usage est: 29.3k tokens\nTotal duration: 42s");

        string body = new PrBodyBuilder(configurationHelper.Object).Build(summary);

        Assert.Contains("## Problem Description", body);
        Assert.Contains("**Avoid difficult code**", body);
        Assert.Contains("### Root cause", body);
        Assert.Contains("This code is difficult to maintain", body);
        Assert.Contains("ISSUE-1", body);
        Assert.Contains("| Issue | Title | Location |", body);
        Assert.Contains("| [ISSUE-1]", body);
        Assert.Contains("| Fix this | `src/A.cs:4` |", body);
        Assert.Equal(2, body.Split("Fix this", StringSplitOptions.None).Length);
        Assert.False(body.Contains("## Original Problem", StringComparison.Ordinal));
        Assert.False(body.Contains("### Issue title(s) reported by SonarQube", StringComparison.Ordinal));
        Assert.False(body.Contains("## SonarQube Rule", StringComparison.Ordinal));
        Assert.False(body.Contains("csharpsquid:S1` `src/A.cs", StringComparison.Ordinal));
        Assert.False(body.Contains("## Changed Files", StringComparison.Ordinal));
        Assert.False(body.Contains("## Review Notes", StringComparison.Ordinal));
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
        Assert.Contains("`src/A.cs:not specified`", body);
        Assert.Contains("Rule information was not requested or could not be retrieved", body);
        Assert.False(body.Contains("No changed files were detected", StringComparison.Ordinal));
    }
}
