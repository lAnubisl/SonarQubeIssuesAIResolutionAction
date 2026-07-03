using NUnit.Framework;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class PromptBuilderTests
{
    [Test]
    public static void PromptGeneration()
    {
        var issue = TestData.SampleIssue() with { CodeSnippet = new CodeSnippet("src/A.cs", true, 1, 1, "    1: code") };

        var prompt = new PromptBuilder(TestData.Configuration()).Build([issue], "feature", "main");

        Assert.Contains("Fix only the listed SonarQube issues", prompt);
        Assert.Contains("The fix branch is already checked out", prompt);
        Assert.Contains("Leave all file changes uncommitted", prompt);
        Assert.Contains("Current branch: `feature`", prompt);
        Assert.Contains("Do not run `git commit`", prompt);
        Assert.Contains("ISSUE-1", prompt);
        Assert.Contains("src/A.cs", prompt);
    }
}
