using NUnit.Framework;
using SonarCopilotFix.GitHub;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CopilotCliRunnerTests
{
    [Test]
    public static void DefaultCopilotCliArguments()
    {
        IReadOnlyList<string> restricted = CopilotCliRunner.BuildArguments(
            TestData.MockConfigurationHelper(inputCopilotModel: "gpt-5.2").Object,
            "Fix the selected issue.");
        CollectionAssert.AreEqual(
            [
                "--prompt", "Fix the selected issue.", "--no-ask-user", "--no-color",
                "--model", "gpt-5.2", "--allow-tool=write", "--deny-tool=shell(git commit)"
            ],
            restricted);
    }

    [Test]
    public static void RestrictedCopilotCliArguments()
    {
        IReadOnlyList<string> restricted = CopilotCliRunner.BuildArguments(
            TestData.MockConfigurationHelper(
                inputCopilotAllowedTools: ["shell(dotnet:*)", "shell(python:*)"]).Object,
            "Fix it.");

        Assert.True(restricted.Contains("--allow-tool=write,shell(dotnet:*),shell(python:*)"));
        Assert.True(restricted.Contains("--deny-tool=shell(git commit)"));
        Assert.False(restricted.Contains("--allow-all-tools"));
    }

    [Test]
    public static void AllowAllCopilotCliArguments()
    {
        IReadOnlyList<string> unrestricted = CopilotCliRunner.BuildArguments(
            TestData.MockConfigurationHelper(
                inputCopilotAllowedTools: ["shell(dotnet:*)"],
                inputCopilotAllowAllTools: true).Object,
            "Fix it.");
        Assert.True(unrestricted.Contains("--allow-all-tools"));
        Assert.True(unrestricted.Contains("--deny-tool=shell(git commit)"));
        Assert.False(unrestricted.Any(argument => argument.StartsWith("--allow-tool=", StringComparison.Ordinal)));
    }

    [Test]
    public static void MalformedCopilotToolPattern()
    {
        ControlledFailureException exception = Assert.Throws<ControlledFailureException>(() =>
            CopilotCliRunner.BuildArguments(
                TestData.MockConfigurationHelper(inputCopilotAllowedTools: ["shell(dotnet:*"]).Object,
                "Fix it."));

        Assert.Equal(ExitCodes.ConfigurationError, exception.ExitCode);
    }
}
