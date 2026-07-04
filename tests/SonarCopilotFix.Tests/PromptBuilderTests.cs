using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class PromptBuilderTests
{
    [Test]
    public static void PromptGeneration()
    {
        SonarIssue issue = TestData.SampleIssue() with { CodeSnippet = new CodeSnippet("src/A.cs", true, 1, 1, "    1: code") };

        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper();
        string prompt = new PromptBuilder(configuration.Object).Build([issue], "feature", "main");

        Assert.Contains("Fix only the listed SonarQube issues", prompt);
        Assert.Contains("The fix branch is already checked out", prompt);
        Assert.Contains("Leave all file changes uncommitted", prompt);
        Assert.Contains("Current branch: `feature`", prompt);
        Assert.Contains("ISSUE-1", prompt);
        Assert.Contains("src/A.cs", prompt);
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

        string prompt = new PromptBuilder(configuration.Object).Build([issue], "fix", "main");

        Assert.Contains("## Extra Instructions", prompt);
        Assert.Contains("Run the focused test project.", prompt);
        Assert.Contains("Local file not found: File was not found.", prompt);
    }

    [Test]
    public static void PromptExplainsWhenSnippetWasNotRequested()
    {
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper();

        string prompt = new PromptBuilder(configuration.Object).Build([TestData.SampleIssue()], "fix", "main");

        Assert.Contains("Code snippet was not requested.", prompt);
    }
}
