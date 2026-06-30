using NUnit.Framework;
using SonarCopilotFix.GitHub;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class PrBodyBuilderTests
{
    [Test]
    public static void PrBody()
    {
        var configurationHelper = TestData.Configuration();
        var summary = new JobSummary(configurationHelper)
        {
            BaseBranch = "main",
            GeneratedBranch = "copilot/sonar/proj/20260101000000",
            ChangedFiles = ["src/A.cs"],
            CopilotSessionSummary = "Total usage est: 29.3k tokens\nTotal duration: 42s"
        };

        var body = new PrBodyBuilder(configurationHelper).Build([TestData.SampleIssue()], summary);

        Assert.Contains("Human review is required", body);
        Assert.Contains("ISSUE-1", body);
        Assert.Contains("src/A.cs", body);
        Assert.Contains("Validation is delegated to the repository's pull request checks", body);
        Assert.Contains("Copilot Session Summary", body);
        Assert.Contains("29.3k", body);
        Assert.Contains("42s", body);
    }
}
