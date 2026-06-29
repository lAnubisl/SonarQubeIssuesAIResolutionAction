using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix;

public sealed class SonarCopilotFixApp(
    ActionInputs options,
    IEnvironment environment,
    TextLogger logger,
    ISonarQubeClient sonarQube,
    CodeSnippetReader snippetReader,
    PromptBuilder promptBuilder,
    CommandRunner commandRunner,
    PrBodyBuilder prBodyBuilder)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var git = new GitService(commandRunner, options.Workspace);
        var github = new GitHubCliService(commandRunner, options.Workspace, logger);
        var copilot = new CopilotCliRunner(commandRunner, options.Workspace, logger);
        var summary = new JobSummary(options);

        logger.Info("Fetching SonarQube issues.");
        var issues = await sonarQube.GetIssuesAsync(cancellationToken);
        logger.Info($"Fetched {issues.Issues.Count} SonarQube issue(s) ({issues.TotalFound} total matching issue(s) reported by SonarQube).");
        foreach (var issue in issues.Issues)
        {
            logger.Info($"Fetched SonarQube issue: key={issue.Key}, severity={issue.Severity ?? "UNKNOWN"}, title={issue.Message}");
        }

        summary.IssuesFound = issues.TotalFound;
        summary.IssuesSelected = issues.Issues.Count;
        WriteOutput("selected_issue_count", issues.Issues.Count.ToString());

        if (issues.Issues.Count == 0)
        {
            summary.Write(environment);
            if (options.FailIfNoIssues)
            {
                throw new ControlledFailureException("No matching SonarQube issues were found.", ExitCodes.NoIssuesFound);
            }

            logger.Info("No matching SonarQube issues were found.");
            return ExitCodes.Success;
        }

        var baseBranch = string.IsNullOrWhiteSpace(options.BaseBranch)
            ? await git.DetectDefaultBranchAsync(cancellationToken)
            : options.BaseBranch;
        summary.BaseBranch = baseBranch;

        var currentBranch = await git.CurrentBranchAsync(cancellationToken);
        var enrichedIssues = options.IncludeCodeSnippets
            ? snippetReader.AddSnippets(options.Workspace, options.SonarProjectKey, issues.Issues, options.CodeSnippetContextLines)
            : issues.Issues;

        var promptPath = Path.Combine(options.Workspace, ".sonar-copilot", "issues-prompt.md");
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
        await File.WriteAllTextAsync(
            promptPath,
            promptBuilder.Build(options, enrichedIssues, currentBranch, baseBranch),
            cancellationToken);
        WriteOutput("prompt_file", promptPath);
        summary.PromptFile = promptPath;

        if (options.DryRun)
        {
            logger.Info("Dry-run mode enabled. Copilot, git push, and PR creation will be skipped.");
            summary.DryRun = true;
            summary.Write(environment);
            return ExitCodes.Success;
        }

        var initialChanges = await git.GetChangedFilesAsync(excludeGenerated: true, cancellationToken);
        if (initialChanges.Count > 0)
        {
            throw new ControlledFailureException("The worktree has pre-existing changes outside .sonar-copilot. Refusing to continue so unrelated files are not committed.", ExitCodes.GitFailure);
        }

        logger.Info("Running GitHub Copilot CLI.");
        summary.CopilotUsageReport = await copilot.RunAsync(options, promptPath, cancellationToken);
        summary.CopilotExecuted = true;

        var changedFiles = await git.GetChangedFilesAsync(excludeGenerated: true, cancellationToken);
        summary.ChangedFiles = changedFiles;
        if (changedFiles.Count == 0)
        {
            logger.Info("Copilot completed without repository file changes.");
            summary.Write(environment);
            return ExitCodes.Success;
        }

        var branchName = git.BuildBranchName(options.BranchPrefix, options.SonarProjectKey, DateTimeOffset.UtcNow);
        summary.GeneratedBranch = branchName;
        await git.CreateBranchAsync(branchName, cancellationToken);
        await git.ConfigureBotUserAsync(cancellationToken);
        await git.StageFilesAsync(changedFiles, cancellationToken);
        await git.CommitAsync($"Fix SonarQube issues for {options.SonarProjectKey}", cancellationToken);

        var githubTokenSource = string.IsNullOrWhiteSpace(options.GhCliToken) ? "GITHUB_TOKEN fallback" : "GH_CLI_TOKEN";
        logger.Info($"Using {githubTokenSource} for GitHub repository operations.");
        await github.SetupGitAuthenticationAsync(options.EffectiveGitHubToken, cancellationToken);
        await git.PushBranchAsync(branchName, options.EffectiveGitHubToken, cancellationToken);

        var prBodyPath = Path.Combine(options.Workspace, ".sonar-copilot", "pull-request-body.md");
        await File.WriteAllTextAsync(
            prBodyPath,
            prBodyBuilder.Build(options, enrichedIssues, summary),
            cancellationToken);
        var prUrl = await github.CreatePullRequestAsync(
            options.EffectiveGitHubToken,
            $"Fix SonarQube issues for {options.SonarProjectKey}",
            prBodyPath,
            baseBranch,
            branchName,
            options.PullRequestDraft,
            cancellationToken);

        summary.PullRequestUrl = prUrl;
        WriteOutput("pull_request_url", prUrl);
        summary.Write(environment);
        return ExitCodes.Success;
    }

    private void WriteOutput(string name, string value)
    {
        var outputPath = environment.Get("GITHUB_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        File.AppendAllText(outputPath, $"{name}={value}{Environment.NewLine}");
    }
}
