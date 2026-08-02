using Moq;
using NUnit.Framework;
using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class PromptBuilderTests
{
    [Test]
    public static void PromptGeneration()
    {
        SonarIssue issue = TestData.SampleIssue() with { CodeSnippet = new CodeSnippet("src/A.cs", true, 1, 1, "    1: code") };
        IssueGroup group = new(
            issue.RuleKey,
            [issue],
            new SonarRule(
            [
                new SonarRuleDescriptionSection(
                    "root_cause",
                    "<p>Description</p>"),
                new SonarRuleDescriptionSection(
                    "how_to_fix",
                    "<p>Use the Spring fix.</p>")
            ]));

        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper();
        string prompt = new PromptBuilder(configuration.Object).Build(group, "feature", "main");

        Assert.Contains("Fix only the listed SonarQube issues", prompt);
        Assert.Contains("The fix branch is already checked out", prompt);
        Assert.Contains("Leave all file changes uncommitted", prompt);
        Assert.Contains("Current branch: `feature`", prompt);
        Assert.Contains("ISSUE-1", prompt);
        Assert.Contains("src/A.cs", prompt);
        Assert.Contains("## Rule Details", prompt);
        Assert.Contains("### root_cause", prompt);
        Assert.Contains("<p>Description</p>", prompt);
        Assert.Contains("### how_to_fix", prompt);
        Assert.Contains("<p>Use the Spring fix.</p>", prompt);
    }

    [Test]
    public static void PromptIncludesExtraInstructionsAndMissingSnippetState()
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(
            inputCopilotExtraInstructions: "Run the focused test project.");
        SonarIssue issue = TestData.SampleIssue() with
        {
            CodeSnippet = new CodeSnippet("src/Missing.cs", false, null, null, "File was not found.")
        };

        string prompt = new PromptBuilder(configuration.Object).Build(
            new IssueGroup(issue.RuleKey, [issue]),
            "fix",
            "main");

        Assert.Contains("## Extra Instructions", prompt);
        Assert.Contains("Run the focused test project.", prompt);
        Assert.Contains("Local file not found: File was not found.", prompt);
    }

    [Test]
    public static void PromptExplainsWhenSnippetWasNotRequested()
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper();

        SonarIssue issue = TestData.SampleIssue();
        string prompt = new PromptBuilder(configuration.Object).Build(
            new IssueGroup(issue.RuleKey, [issue]),
            "fix",
            "main");

        Assert.Contains("Code snippet was not requested.", prompt);
    }
}
