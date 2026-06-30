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

        var prompt = new PromptBuilder().Build(TestData.Options(), [issue], "feature", "main");

        Assert.Contains("Fix only the listed SonarQube issues", prompt);
        Assert.Contains("ISSUE-1", prompt);
        Assert.Contains("src/A.cs", prompt);
    }
}
