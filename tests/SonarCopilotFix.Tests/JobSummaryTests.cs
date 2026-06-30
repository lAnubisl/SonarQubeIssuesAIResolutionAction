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
        var summary = new JobSummary(TestData.Options())
        {
            CopilotExecuted = true,
            CopilotSessionSummary = "Total usage est: 1k tokens\nTotal duration: 5s"
        };

        var configurationHelper = TestData.MockConfigurationHelper(gitHubStepSummary: path);
        summary.Write(configurationHelper.Object);

        var contents = File.ReadAllText(path);
        Assert.Contains("Copilot Session Summary", contents);
        Assert.Contains("1k tokens", contents);
        Assert.Contains("5s", contents);
    }
}
