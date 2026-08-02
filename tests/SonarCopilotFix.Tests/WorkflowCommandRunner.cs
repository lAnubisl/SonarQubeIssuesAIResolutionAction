
namespace SonarCopilotFix.Tests;

internal sealed class WorkflowCommandRunner : ICommandRunner
{
    private int _gitStatusCount;

    public List<string> CreatedBranches { get; } = [];
    public List<string> CopilotSessionIds { get; } = [];
    public List<string> CopilotPrompts { get; } = [];
    public List<string> PullRequestBodies { get; } = [];
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
        string[] args = arguments.ToArray();
        if (fileName == "copilot")
        {
            int sessionIndex = Array.IndexOf(args, "--session-id");
            CopilotSessionIds.Add(args[sessionIndex + 1]);
            int promptIndex = Array.IndexOf(args, "--prompt");
            CopilotPrompts.Add(args[promptIndex + 1]);
            return Task.FromResult(new CommandResult(0, "fixed\n", "session complete\n"));
        }

        if (fileName == "gh")
        {
            if (args.Take(2).SequenceEqual(["pr", "create"]))
            {
                PullRequestCount++;
                int bodyIndex = Array.IndexOf(args, "--body");
                PullRequestBodies.Add(args[bodyIndex + 1]);
                return Task.FromResult(new CommandResult(
                    0,
                    $"https://github.example/pr/{PullRequestCount}\n",
                    ""));
            }

            return Task.FromResult(new CommandResult(0, "", ""));
        }

        string[] gitArgs = args.Skip(2).ToArray();
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
