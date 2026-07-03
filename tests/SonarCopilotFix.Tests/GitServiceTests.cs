using Moq;
using NUnit.Framework;
using SonarCopilotFix.Git;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GitServiceTests
{
    private static readonly string[] GitStatusArguments = new[] { "status", "--porcelain" };
    private static readonly string[] GitSafeDirectoryRevParseSuffix = new[] { "rev-parse", "HEAD" };
    private static readonly string[] GitSafeDirectoryDiffSuffix = new[] { "diff", "--name-only", "--diff-filter=ACDMRTUXB", "base123", "HEAD", "--" };
    [Test]
    public static void BranchNameIncludesRuleKey()
    {
        var configurationHelper = TestData.MockConfigurationHelper(inputSonarProjectKey: "my project");
        var git = new GitService(Mock.Of<ICommandRunner>(), configurationHelper.Object);

        var branchName = git.BuildBranchName(
            "csharpsquid:unsafe rule",
            new DateTimeOffset(2026, 7, 3, 12, 34, 56, 789, TimeSpan.Zero));

        Assert.Equal("copilot/sonar-fixes/my-project/csharpsquid-unsafe-rule/20260703123456789", branchName);
    }

    [Test]
    public static async Task SwitchBranch()
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
                    "switch",
                    "main"
                })),
                workspace,
                null,
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(0, "", ""));
        var configurationHelper = TestData.MockConfigurationHelper(gitHubWorkspace: workspace);
        var git = new GitService(commandRunner.Object, configurationHelper.Object);

        await git.SwitchBranchAsync("main", CancellationToken.None);

        commandRunner.VerifyAll();
    }

    [Test]
    public static async Task GitChangedFiles()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "workspace");
        var commandRunner = new Mock<ICommandRunner>(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                "git",
                It.Is<IEnumerable<string>>(arguments => arguments.SequenceEqual(
                    new[] { "-c", $"safe.directory={Path.GetFullPath(workspace)}" }.Concat(GitStatusArguments))),
                workspace,
                null,
                null,
                null,
                CancellationToken.None))
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

    [Test]
    public static async Task HeadCommitAndCommittedChangedFiles()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "workspace");
        var safeDirectoryArguments = new[]
        {
            "-c",
            $"safe.directory={Path.GetFullPath(workspace)}"
        };
        var commandRunner = new Mock<ICommandRunner>(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                "git",
                It.Is<IEnumerable<string>>(arguments => arguments.SequenceEqual(
                    safeDirectoryArguments.Concat(GitSafeDirectoryRevParseSuffix))),
                workspace,
                null,
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(0, "abc123\n", ""));
        commandRunner
            .Setup(value => value.RunAsync(
                "git",
                It.Is<IEnumerable<string>>(arguments => arguments.SequenceEqual(
                    safeDirectoryArguments.Concat(GitSafeDirectoryDiffSuffix))),
                workspace,
                null,
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(
                0,
                "src/Changed.cs\n.sonar-copilot/issues-prompt.md\ntests/ChangedTests.cs\n",
                ""));
        var configurationHelper = TestData.MockConfigurationHelper(gitHubWorkspace: workspace);
        var git = new GitService(commandRunner.Object, configurationHelper.Object);

        var head = await git.GetHeadCommitAsync(CancellationToken.None);
        var changedFiles = await git.GetChangedFilesSinceAsync(
            "base123",
            excludeGenerated: true,
            CancellationToken.None);

        Assert.Equal("abc123", head);
        CollectionAssert.AreEqual(["src/Changed.cs", "tests/ChangedTests.cs"], changedFiles);
        commandRunner.VerifyAll();
    }
}
