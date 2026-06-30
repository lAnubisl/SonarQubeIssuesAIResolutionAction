using NUnit.Framework;
using SonarCopilotFix.GitHub;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GitHubCliServiceTests
{
    [Test]
    public static void GitHubCliEnvironment()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "github-workspace");

        var environment = GitHubCliService.BuildEnvironment("github-secret", workspace);

        Assert.Equal("github-secret", environment["GH_TOKEN"]);
        Assert.Equal("1", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("safe.directory", environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal(Path.GetFullPath(workspace), environment["GIT_CONFIG_VALUE_0"]);
    }
}
