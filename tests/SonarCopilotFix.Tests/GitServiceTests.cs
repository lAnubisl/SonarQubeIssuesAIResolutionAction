using Moq;
using NUnit.Framework;
using SonarCopilotFix.Git;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GitServiceTests
{
    [Test]
    public static async Task GitChangedFiles()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "workspace");
        var commandRunner = new Mock<ICommandRunner>(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                "git",
                It.Is<IEnumerable<string>>(arguments => arguments.SequenceEqual(new[]
                {
                    "-c",
                    $"safe.directory={Path.GetFullPath(workspace)}",
                    "status",
                    "--porcelain"
                })),
                workspace,
                null,
                CancellationToken.None,
                null,
                null,
                true))
            .ReturnsAsync(new CommandResult(
                0,
                " M HostFilmMonitoring.cs\n?? untracked.txt\n?? .sonar-copilot/issues-prompt.md\n",
                ""));
        var configurationHelper = TestData.MockConfigurationHelper(gitHubWorkspace: workspace);
        var git = new GitService(commandRunner.Object, configurationHelper.Object);

        var changedFiles = await git.GetChangedFilesAsync(excludeGenerated: true, CancellationToken.None);

        Assert.Equal(2, changedFiles.Count);
        CollectionAssert.AreEqual(["HostFilmMonitoring.cs", "untracked.txt"], changedFiles);
        commandRunner.VerifyAll();
    }
}
