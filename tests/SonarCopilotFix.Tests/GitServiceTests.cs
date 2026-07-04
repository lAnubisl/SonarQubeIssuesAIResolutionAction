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
    private static readonly string[] SymbolicRefSuffix = new[] { "symbolic-ref", "refs/remotes/origin/HEAD", "--short" };
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

    [Test]
    public static async Task DetectDefaultBranchUsesSymbolicReference()
    {
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                "git",
                It.Is<IEnumerable<string>>(arguments =>
                    arguments.TakeLast(3).SequenceEqual(SymbolicRefSuffix)),
                It.IsAny<string>(),
                null,
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(0, "origin/develop\n", ""));
        GitService git = new(commandRunner.Object, TestData.MockConfigurationHelper().Object);

        string branch = await git.DetectDefaultBranchAsync(CancellationToken.None);

        Assert.Equal("develop", branch);
        commandRunner.VerifyAll();
    }

    [Test]
    public static async Task DetectDefaultBranchFallsBackToRemoteDescription()
    {
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        commandRunner
            .SetupSequence(value => value.RunAsync(
                "git",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                null,
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(new CommandResult(1, "", "missing"))
            .ReturnsAsync(new CommandResult(0, "  HEAD branch: trunk\n", ""));
        GitService git = new(commandRunner.Object, TestData.MockConfigurationHelper().Object);

        string branch = await git.ResolveBaseBranchAsync(CancellationToken.None);

        Assert.Equal("trunk", branch);
        commandRunner.Verify(
            value => value.RunAsync(
                "git",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                null,
                null,
                null,
                CancellationToken.None),
            Times.Exactly(2));
    }

    [Test]
    public static async Task CurrentBranchReturnsDetachedWhenGitReturnsNoName()
    {
        Mock<ICommandRunner> commandRunner = SuccessfulGitRunner(new CommandResult(0, " \n", ""));
        GitService git = new(commandRunner.Object, TestData.MockConfigurationHelper().Object);

        string branch = await git.CurrentBranchAsync(CancellationToken.None);

        Assert.Equal("detached", branch);
    }

    [Test]
    public static async Task MutationCommandsAreDelegatedWithExpectedArguments()
    {
        List<string[]> calls = [];
        List<IReadOnlyDictionary<string, string?>?> environments = [];
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                "git",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string?>?>(),
                null,
                null,
                CancellationToken.None))
            .Callback((string _, IEnumerable<string> arguments, string _, IReadOnlyDictionary<string, string?>? environment,
                Action<string>? _, Action<string>? _, CancellationToken _) =>
            {
                calls.Add(arguments.Skip(2).ToArray());
                environments.Add(environment);
            })
            .ReturnsAsync(new CommandResult(0, "", ""));
        GitService git = new(commandRunner.Object, TestData.MockConfigurationHelper(ghCliToken: "gh-token").Object);

        await git.CreateBranchAsync("fix/test", CancellationToken.None);
        await git.ConfigureBotUserAsync(CancellationToken.None);
        await git.StageFilesAsync(["src/A.cs", "src/B.cs"], CancellationToken.None);
        await git.CommitAsync("fix issues", CancellationToken.None);
        await git.PushBranchAsync("fix/test", CancellationToken.None);

        Assert.True(calls.Any(call => call.SequenceEqual(new[] { "switch", "-c", "fix/test" })));
        Assert.True(calls.Any(call => call.SequenceEqual(new[] { "config", "user.email", "github-actions[bot]@users.noreply.github.com" })));
        Assert.True(calls.Any(call => call.SequenceEqual(new[] { "config", "user.name", "github-actions[bot]" })));
        Assert.True(calls.Any(call => call.SequenceEqual(new[] { "add", "--", "src/A.cs" })));
        Assert.True(calls.Any(call => call.SequenceEqual(new[] { "add", "--", "src/B.cs" })));
        Assert.True(calls.Any(call => call.SequenceEqual(new[] { "commit", "-m", "fix issues" })));
        Assert.True(calls.Any(call => call.SequenceEqual(new[] { "push", "--set-upstream", "origin", "fix/test" })));
        Assert.Equal("gh-token", environments.Last()!["GH_TOKEN"]);
    }

    [Test]
    public static async Task GitFailureIncludesOperationAndCommandOutput()
    {
        Mock<ICommandRunner> commandRunner = SuccessfulGitRunner(new CommandResult(2, "output", "error"));
        GitService git = new(commandRunner.Object, TestData.MockConfigurationHelper().Object);

        ControlledFailureException exception = await Assert.ThrowsAsync<ControlledFailureException>(
            () => git.CommitAsync("message", CancellationToken.None));

        Assert.Equal(ExitCodes.GitFailure, exception.ExitCode);
        Assert.Contains("commit changes", exception.Message);
        Assert.Contains("output", exception.Message);
        Assert.Contains("error", exception.Message);
    }

    private static Mock<ICommandRunner> SuccessfulGitRunner(CommandResult result)
    {
        Mock<ICommandRunner> commandRunner = new(MockBehavior.Strict);
        commandRunner
            .Setup(value => value.RunAsync(
                "git",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string?>?>(),
                null,
                null,
                CancellationToken.None))
            .ReturnsAsync(result);
        return commandRunner;
    }
}
