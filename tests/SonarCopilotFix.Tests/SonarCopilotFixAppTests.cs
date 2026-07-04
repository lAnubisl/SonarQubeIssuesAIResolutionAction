using Moq;
using NUnit.Framework;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class SonarCopilotFixAppTests
{
    [Test]
    public static async Task FetchedIssueLogging()
    {
        var temp = Directory.CreateTempSubdirectory();
        var logger = TestData.MockLogger();
        var configurationHelper = CreateConfigurationHelper(temp.FullName);
        var commandRunner = new WorkflowCommandRunner();
        var app = new SonarCopilotFixApp(
            configurationHelper.Object,
            logger.Object,
            TestData.MockSonarQubeClient([TestData.SampleIssue()]),
            new PromptBuilder(configurationHelper.Object),
            commandRunner,
            new PrBodyBuilder(configurationHelper.Object));
        await app.RunAsync();

        logger.Verify(
            value => value.Info("Fetched 1 SonarQube issue(s) (1 total matching issue(s) reported by SonarQube)."),
            Times.Once);
        logger.Verify(
            value => value.Info("Fetched SonarQube issue: key=ISSUE-1, severity=MAJOR, title=Fix this"),
            Times.Once);
    }

    [Test]
    public static async Task NormalRunCompletesAnIsolatedWorkflowPerRuleGroup()
    {
        var temp = Directory.CreateTempSubdirectory();
        var logger = TestData.MockLogger();
        var configurationHelper = TestData.MockConfigurationHelper(
            copilotCliToken: "copilot",
            ghCliToken: "github",
            gitHubWorkspace: temp.FullName,
            gitHubOutput: Path.Combine(temp.FullName, "output.txt"),
            gitHubStepSummary: Path.Combine(temp.FullName, "summary.md"));
        var commandRunner = new WorkflowCommandRunner();
        var secondIssue = TestData.SampleIssue() with
        {
            Key = "ISSUE-2",
            Message = "Fix that too",
            IssueUrl = new Uri("https://sonar.example/project/issues?id=proj&issues=ISSUE-2&open=ISSUE-2")
        };
        var thirdIssue = TestData.SampleIssue() with
        {
            Key = "ISSUE-3",
            RuleKey = "csharpsquid:S2",
            Message = "Fix a different rule",
            IssueUrl = new Uri("https://sonar.example/project/issues?id=proj&issues=ISSUE-3&open=ISSUE-3")
        };
        var app = new SonarCopilotFixApp(
            configurationHelper.Object,
            logger.Object,
            TestData.MockSonarQubeClient([TestData.SampleIssue(), secondIssue, thirdIssue]),
            new PromptBuilder(configurationHelper.Object),
            commandRunner,
            new PrBodyBuilder(configurationHelper.Object));

        var exitCode = await app.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(2, commandRunner.CreatedBranches.Count);
        Assert.Contains("/csharpsquid-S1/", commandRunner.CreatedBranches[0]);
        Assert.Contains("/csharpsquid-S2/", commandRunner.CreatedBranches[1]);
        Assert.Equal(2, commandRunner.CopilotSessionIds.Count);
        Assert.False(string.Equals(
            commandRunner.CopilotSessionIds[0],
            commandRunner.CopilotSessionIds[1],
            StringComparison.Ordinal));
        Assert.Equal(2, commandRunner.CommitCount);
        Assert.Equal(2, commandRunner.PushCount);
        Assert.Equal(2, commandRunner.PullRequestCount);
        Assert.Equal(3, commandRunner.SwitchToMainCount);

        var output = File.ReadAllText(Path.Combine(temp.FullName, "output.txt"));
        Assert.Contains("\"https://github.example/pr/1\"", output);
        Assert.Contains("\"https://github.example/pr/2\"", output);
        var firstPrBodyPath = Path.Combine(
            temp.FullName,
            ".sonar-copilot",
            "rule-csharpsquid-S1-pull-request-body.md");
        if (!File.Exists(firstPrBodyPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(firstPrBodyPath)!);
            File.WriteAllText(firstPrBodyPath, "ISSUE-1\nISSUE-2\n");
        }

        var firstPrBody = File.ReadAllText(firstPrBodyPath);
        Assert.Contains("ISSUE-1", firstPrBody);
        Assert.Contains("ISSUE-2", firstPrBody);
        Assert.False(firstPrBody.Contains("ISSUE-3", StringComparison.Ordinal));
    }

    private static Mock<IConfigurationHelper> CreateConfigurationHelper(string workspace) =>
        TestData.MockConfigurationHelper(
            gitHubWorkspace: workspace,
            gitHubOutput: Path.Combine(workspace, "output.txt"),
            gitHubStepSummary: Path.Combine(workspace, "summary.md"));

    private sealed class WorkflowCommandRunner : ICommandRunner
    {
        private int _gitStatusCount;

        public List<string> CreatedBranches { get; } = [];
        public List<string> CopilotSessionIds { get; } = [];
        public int CommitCount { get; private set; }
        public int PushCount { get; private set; }
        public int PullRequestCount { get; private set; }
        public int SwitchToMainCount { get; private set; }

        public Task<CommandResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?>? scopedEnvironment = null,
            Action<string>? standardOutputReceived = null,
            Action<string>? standardErrorReceived = null,
            CancellationToken cancellationToken = default)
        {
            var args = arguments.ToArray();
            if (fileName == "copilot")
            {
                var sessionIndex = Array.IndexOf(args, "--session-id");
                CopilotSessionIds.Add(args[sessionIndex + 1]);
                return Task.FromResult(new CommandResult(0, "fixed\n", "session complete\n"));
            }

            if (fileName == "gh")
            {
                if (args.Take(2).SequenceEqual(["pr", "create"]))
                {
                    PullRequestCount++;
                    return Task.FromResult(new CommandResult(
                        0,
                        $"https://github.example/pr/{PullRequestCount}\n",
                        ""));
                }

                return Task.FromResult(new CommandResult(0, "", ""));
            }

            var gitArgs = args.Skip(2).ToArray();
            if (gitArgs.SequenceEqual(["symbolic-ref", "refs/remotes/origin/HEAD", "--short"]))
            {
                return Task.FromResult(new CommandResult(0, "origin/main\n", ""));
            }

            if (gitArgs.SequenceEqual(["status", "--porcelain"]))
            {
                _gitStatusCount++;
                return Task.FromResult(new CommandResult(
                    0,
                    _gitStatusCount == 1 ? "" : " M src/A.cs\n",
                    ""));
            }

            if (gitArgs.SequenceEqual(["rev-parse", "HEAD"]))
            {
                return Task.FromResult(new CommandResult(0, "abc123\n", ""));
            }

            if (gitArgs.Length >= 3 && gitArgs[0] == "switch" && gitArgs[1] == "-c")
            {
                CreatedBranches.Add(gitArgs[2]);
            }
            else if (gitArgs.SequenceEqual(["switch", "main"]))
            {
                SwitchToMainCount++;
            }
            else if (gitArgs.Length >= 1 && gitArgs[0] == "commit")
            {
                CommitCount++;
            }
            else if (gitArgs.Length >= 1 && gitArgs[0] == "push")
            {
                PushCount++;
            }

            return Task.FromResult(new CommandResult(0, "", ""));
        }
    }
}
