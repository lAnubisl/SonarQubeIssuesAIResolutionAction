using NUnit.Framework;
using SonarCopilotFix.GitHub;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CopilotCliRunnerTests
{
    [Test]
    public static void CopilotCliArguments()
    {
        var restricted = CopilotCliRunner.BuildArguments(
            TestData.MockConfigurationHelper(inputCopilotModel: "gpt-5.2").Object,
            "Fix the selected issue.");
        CollectionAssert.AreEqual(
            ["--prompt", "Fix the selected issue.", "--no-ask-user", "--no-color", "--model", "gpt-5.2", "--allow-tool=write"],
            restricted);

        var unrestricted = CopilotCliRunner.BuildArguments(
            TestData.MockConfigurationHelper(inputCopilotAllowAllTools: true).Object,
            "Fix it.");
        Assert.True(unrestricted.Contains("--allow-all-tools"));
        Assert.False(unrestricted.Contains("--allow-tool=write"));
    }
}
