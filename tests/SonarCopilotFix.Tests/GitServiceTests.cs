using Moq;
using NUnit.Framework;
using SonarCopilotFix.Git;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.Infrastructure.Models;

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
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(inputSonarProjectKey: "my project");
        GitService git = new(Mock.Of<ICommandRunner>(), configurationHelper.Object);

        string branchName = git.BuildBranchName(
            "csharpsquid:unsafe rule",
            new DateTimeOffset(2026, 7, 3, 12, 34, 56, 789, TimeSpan.Zero));

        Assert.Equal("copilot/sonar-fixes/my-project/csharpsquid-unsafe-rule/20260703123456789", branchName);
    }

    [Test]
    public static async Task ResolveBaseBranchUsesConfiguredBranchWithoutCallingGit()
    {
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(inputBaseBranch: "release");
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        GitService git = new(commandRunner.Object, configurationHelper.Object);

        string baseBranch = await git.ResolveBaseBranchAsync(CancellationToken.None);

        Assert.Equal("release", baseBranch);
        commandRunner.VerifyNoOtherCalls();
    }

    [Test]
    public static async Task SwitchBranch()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "workspace");
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
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
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(gitHubWorkspace: workspace);
        GitService git = new(commandRunner.Object, configurationHelper.Object);

        await git.SwitchBranchAsync("main", CancellationToken.None);

        commandRunner.VerifyAll();
    }

    [Test]
    public static async Task GitChangedFiles()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "workspace");
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
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
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(gitHubWorkspace: workspace);
        GitService git = new(commandRunner.Object, configurationHelper.Object);

        IReadOnlyList<string> changedFiles = await git.GetChangedFilesAsync(excludeGenerated: true, CancellationToken.None);

        Assert.Equal(2, changedFiles.Count);
        CollectionAssert.AreEqual(["HostFilmMonitoring.cs", "untracked.txt"], changedFiles);
        commandRunner.VerifyAll();
    }

    [Test]
    public static async Task HeadCommitAndCommittedChangedFiles()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "workspace");
        string[] safeDirectoryArguments = new[]
        {
            "-c",
            $"safe.directory={Path.GetFullPath(workspace)}"
        };
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
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
        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(gitHubWorkspace: workspace);
        GitService git = new(commandRunner.Object, configurationHelper.Object);

        string head = await git.GetHeadCommitAsync(CancellationToken.None);
        IReadOnlyList<string> changedFiles = await git.GetChangedFilesSinceAsync(
            "base123",
            excludeGenerated: true,
            CancellationToken.None);

        Assert.Equal("abc123", head);
        CollectionAssert.AreEqual(["src/Changed.cs", "tests/ChangedTests.cs"], changedFiles);
        commandRunner.VerifyAll();
    }
}
