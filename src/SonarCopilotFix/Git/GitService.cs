using System.Text.RegularExpressions;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.Infrastructure.Models;

namespace SonarCopilotFix.Git;

public sealed partial class GitService(ICommandRunner commandRunner, IConfigurationHelper configurationHelper)
{
    public async Task<string> ResolveBaseBranchAsync(CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(configurationHelper.InputBaseBranch)
            ? await DetectDefaultBranchAsync(cancellationToken)
            : configurationHelper.InputBaseBranch;

    public async Task<string> CurrentBranchAsync(CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(["branch", "--show-current"], cancellationToken: cancellationToken);
        return result.StandardOutput.Trim().Length > 0 ? result.StandardOutput.Trim() : "detached";
    }

    public async Task<string> DetectDefaultBranchAsync(CancellationToken cancellationToken)
    {
        var symbolic = await RunGitAsync(["symbolic-ref", "refs/remotes/origin/HEAD", "--short"], cancellationToken: cancellationToken);
        if (symbolic.ExitCode == 0 && symbolic.StandardOutput.Trim().StartsWith("origin/", StringComparison.Ordinal))
        {
            return symbolic.StandardOutput.Trim()["origin/".Length..];
        }

        var remote = await RunGitAsync(["remote", "show", "origin"], cancellationToken: cancellationToken);
        var match = HeadBranchRegex().Match(remote.StandardOutput);
        return match.Success ? match.Groups["branch"].Value : "main";
    }

    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(bool excludeGenerated, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(["status", "--porcelain"], cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw GitFailure("inspect git status", result);
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseStatusPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !excludeGenerated || !path.StartsWith(".sonar-copilot/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<string> GetHeadCommitAsync(CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(["rev-parse", "HEAD"], cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw GitFailure("resolve the current HEAD commit", result);
        }

        return result.StandardOutput.Trim();
    }

    public async Task<IReadOnlyList<string>> GetChangedFilesSinceAsync(
        string baseCommit,
        bool excludeGenerated,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            ["diff", "--name-only", "--diff-filter=ACDMRTUXB", baseCommit, "HEAD", "--"],
            cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw GitFailure($"inspect files changed since commit {baseCommit}", result);
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/').Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !excludeGenerated || !path.StartsWith(".sonar-copilot/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public string BuildBranchName(string ruleKey, DateTimeOffset timestamp)
    {
        var safeProject = UnsafeBranchChars().Replace(configurationHelper.GetSonarProjectKey(), "-").Trim('-');
        var safeRuleKey = UnsafeBranchChars().Replace(ruleKey, "-").Trim('-');
        return $"{configurationHelper.InputBranchPrefix.TrimEnd('/')}/{safeProject}/{safeRuleKey}/{timestamp:yyyyMMddHHmmssfff}";
    }

    public async Task CreateBranchAsync(string branchName, CancellationToken cancellationToken)
    {
        await EnsureSuccess("create git branch", RunGitAsync(["switch", "-c", branchName], cancellationToken: cancellationToken), ExitCodes.GitFailure);
    }

    public async Task SwitchBranchAsync(string branchName, CancellationToken cancellationToken)
    {
        await EnsureSuccess($"switch to git branch {branchName}", RunGitAsync(["switch", branchName], cancellationToken: cancellationToken), ExitCodes.GitFailure);
    }

    public async Task ConfigureBotUserAsync(CancellationToken cancellationToken)
    {
        await EnsureSuccess("configure git user email", RunGitAsync(["config", "user.email", "github-actions[bot]@users.noreply.github.com"], cancellationToken: cancellationToken), ExitCodes.GitFailure);
        await EnsureSuccess("configure git user name", RunGitAsync(["config", "user.name", "github-actions[bot]"], cancellationToken: cancellationToken), ExitCodes.GitFailure);
    }

    public async Task StageFilesAsync(IReadOnlyList<string> changedFiles, CancellationToken cancellationToken)
    {
        foreach (var file in changedFiles)
        {
            await EnsureSuccess($"stage {file}", RunGitAsync(["add", "--", file], cancellationToken: cancellationToken), ExitCodes.GitFailure);
        }
    }

    public async Task CommitAsync(string message, CancellationToken cancellationToken)
    {
        await EnsureSuccess("commit changes", RunGitAsync(["commit", "-m", message], cancellationToken: cancellationToken), ExitCodes.GitFailure);
    }

    public async Task PushBranchAsync(string branchName, CancellationToken cancellationToken)
    {
        var env = new Dictionary<string, string?> { ["GH_TOKEN"] = configurationHelper.GetGitHubToken() };
        await EnsureSuccess("push generated branch", RunGitAsync(["push", "--set-upstream", "origin", branchName], env, cancellationToken), ExitCodes.GitFailure);
    }

    private Task<CommandResult> RunGitAsync(
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? scopedEnvironment = null,
        CancellationToken cancellationToken = default)
    {
        // Scope Git's ownership exception to the checked-out workspace. This also
        // keeps the action compatible with stricter self-hosted runner setups.
        return commandRunner.RunAsync(
            "git",
            ["-c", $"safe.directory={Path.GetFullPath(configurationHelper.GitHubWorkspace)}", .. arguments],
            configurationHelper.GitHubWorkspace,
            scopedEnvironment,
            cancellationToken: cancellationToken);
    }

    private static string ParseStatusPath(string statusLine)
    {
        // The first two characters are fixed-width index/worktree status columns.
        // In particular, an unstaged modification starts with a significant space.
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
            throw GitFailure(operation, result, exitCode);
        }
    }

    private static ControlledFailureException GitFailure(string operation, CommandResult result, int exitCode = ExitCodes.GitFailure)
    {
        var detail = result.Summary;
        var message = $"Failed to {operation} (git exited with code {result.ExitCode}).";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $" {detail}";
        }

        return new ControlledFailureException(message, exitCode);
    }

    [GeneratedRegex(@"HEAD branch:\s*(?<branch>\S+)")]
    private static partial Regex HeadBranchRegex();

    [GeneratedRegex(@"[^A-Za-z0-9._/-]+")]
    private static partial Regex UnsafeBranchChars();
}
