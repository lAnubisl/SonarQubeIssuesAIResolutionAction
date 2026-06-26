using System.Text.RegularExpressions;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.Git;

public sealed partial class GitService(CommandRunner commandRunner, string workspace)
{
    public async Task<string> CurrentBranchAsync(CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync("git", ["branch", "--show-current"], workspace, cancellationToken: cancellationToken);
        return result.StandardOutput.Trim().Length > 0 ? result.StandardOutput.Trim() : "detached";
    }

    public async Task<string> DetectDefaultBranchAsync(CancellationToken cancellationToken)
    {
        var symbolic = await commandRunner.RunAsync("git", ["symbolic-ref", "refs/remotes/origin/HEAD", "--short"], workspace, cancellationToken: cancellationToken);
        if (symbolic.ExitCode == 0 && symbolic.StandardOutput.Trim().StartsWith("origin/", StringComparison.Ordinal))
        {
            return symbolic.StandardOutput.Trim()["origin/".Length..];
        }

        var remote = await commandRunner.RunAsync("git", ["remote", "show", "origin"], workspace, cancellationToken: cancellationToken);
        var match = Regex.Match(remote.StandardOutput, @"HEAD branch:\s*(?<branch>\S+)");
        return match.Success ? match.Groups["branch"].Value : "main";
    }

    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(bool excludeGenerated, CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync("git", ["status", "--porcelain"], workspace, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException("Failed to inspect git status.", ExitCodes.GitFailure);
        }

        return result.StandardOutput
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseStatusPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !excludeGenerated || !path.StartsWith(".sonar-copilot/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public string BuildBranchName(string branchPrefix, string projectKey, DateTimeOffset timestamp)
    {
        var safeProject = UnsafeBranchChars().Replace(projectKey, "-").Trim('-');
        return $"{branchPrefix.TrimEnd('/')}/{safeProject}/{timestamp:yyyyMMddHHmmss}";
    }

    public async Task CreateBranchAsync(string branchName, CancellationToken cancellationToken)
    {
        await EnsureSuccess("create git branch", commandRunner.RunAsync("git", ["switch", "-c", branchName], workspace, cancellationToken: cancellationToken), ExitCodes.GitFailure);
    }

    public async Task ConfigureBotUserAsync(CancellationToken cancellationToken)
    {
        await EnsureSuccess("configure git user email", commandRunner.RunAsync("git", ["config", "user.email", "github-actions[bot]@users.noreply.github.com"], workspace, cancellationToken: cancellationToken), ExitCodes.GitFailure);
        await EnsureSuccess("configure git user name", commandRunner.RunAsync("git", ["config", "user.name", "github-actions[bot]"], workspace, cancellationToken: cancellationToken), ExitCodes.GitFailure);
    }

    public async Task StageFilesAsync(IReadOnlyList<string> changedFiles, CancellationToken cancellationToken)
    {
        foreach (var file in changedFiles)
        {
            await EnsureSuccess($"stage {file}", commandRunner.RunAsync("git", ["add", "--", file], workspace, cancellationToken: cancellationToken), ExitCodes.GitFailure);
        }
    }

    public async Task CommitAsync(string message, CancellationToken cancellationToken)
    {
        await EnsureSuccess("commit changes", commandRunner.RunAsync("git", ["commit", "-m", message], workspace, cancellationToken: cancellationToken), ExitCodes.GitFailure);
    }

    public async Task PushBranchAsync(string branchName, string gitHubToken, CancellationToken cancellationToken)
    {
        var env = new Dictionary<string, string?> { ["GH_TOKEN"] = gitHubToken };
        await EnsureSuccess("push generated branch", commandRunner.RunAsync("git", ["push", "--set-upstream", "origin", branchName], workspace, env, cancellationToken), ExitCodes.GitFailure);
    }

    private static string ParseStatusPath(string statusLine)
    {
        var path = statusLine.Length > 3 ? statusLine[3..] : statusLine;
        var renameIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
        if (renameIndex >= 0)
        {
            path = path[(renameIndex + 4)..];
        }

        return path.Replace('\\', '/').Trim('"');
    }

    private static async Task EnsureSuccess(string operation, Task<CommandResult> commandTask, int exitCode)
    {
        var result = await commandTask;
        if (result.ExitCode != 0)
        {
            throw new ControlledFailureException($"Failed to {operation}.", exitCode);
        }
    }

    [GeneratedRegex(@"[^A-Za-z0-9._/-]+")]
    private static partial Regex UnsafeBranchChars();
}
