using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix;

public sealed class SonarCopilotFixApp(
    IConfigurationHelper configurationHelper,
    ILogger logger,
    ISonarQubeClient sonarQube,
    CodeSnippetReader snippetReader,
    PromptBuilder promptBuilder,
    CommandRunner commandRunner,
    PrBodyBuilder prBodyBuilder)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.Validate(configurationHelper);
        var git = new GitService(commandRunner, configurationHelper);
        var github = new GitHubCliService(commandRunner, configurationHelper, logger);
        var copilot = new CopilotCliRunner(commandRunner, configurationHelper, logger);
        var summary = new JobSummary(configurationHelper);

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
            summary.Write();
            if (configurationHelper.InputFailIfNoIssues)
            {
                throw new ControlledFailureException("No matching SonarQube issues were found.", ExitCodes.NoIssuesFound);
            }

            logger.Info("No matching SonarQube issues were found.");
            return ExitCodes.Success;
        }

        var baseBranch = string.IsNullOrWhiteSpace(configurationHelper.InputBaseBranch)
            ? await git.DetectDefaultBranchAsync(cancellationToken)
            : configurationHelper.InputBaseBranch;
        summary.BaseBranch = baseBranch;

        var currentBranch = await git.CurrentBranchAsync(cancellationToken);
        var enrichedIssues = configurationHelper.InputIncludeCodeSnippets
            ? snippetReader.AddSnippets(issues.Issues)
            : issues.Issues;

        var promptPath = Path.Combine(configurationHelper.GitHubWorkspace, ".sonar-copilot", "issues-prompt.md");
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
        await File.WriteAllTextAsync(
            promptPath,
            promptBuilder.Build(enrichedIssues, currentBranch, baseBranch),
            cancellationToken);
        WriteOutput("prompt_file", promptPath);
        summary.PromptFile = promptPath;

        if (configurationHelper.InputDryRun)
        {
            logger.Info("Dry-run mode enabled. Copilot, git push, and PR creation will be skipped.");
            summary.Write();
            return ExitCodes.Success;
        }

        var initialChanges = await git.GetChangedFilesAsync(excludeGenerated: true, cancellationToken);
        if (initialChanges.Count > 0)
        {
            throw new ControlledFailureException("The worktree has pre-existing changes outside .sonar-copilot. Refusing to continue so unrelated files are not committed.", ExitCodes.GitFailure);
        }

        logger.Info("Running GitHub Copilot CLI.");
        summary.CopilotSessionSummary = await copilot.RunAsync(promptPath, cancellationToken);
        summary.CopilotExecuted = true;

        var changedFiles = await git.GetChangedFilesAsync(excludeGenerated: true, cancellationToken);
        summary.ChangedFiles = changedFiles;
        if (changedFiles.Count == 0)
        {
            logger.Info("Copilot completed without repository file changes.");
            summary.Write();
            return ExitCodes.Success;
        }

        var branchName = git.BuildBranchName(DateTimeOffset.UtcNow);
        summary.GeneratedBranch = branchName;
        await git.CreateBranchAsync(branchName, cancellationToken);
        await git.ConfigureBotUserAsync(cancellationToken);
        await git.StageFilesAsync(changedFiles, cancellationToken);
        await git.CommitAsync($"Fix SonarQube issues for {configurationHelper.GetSonarProjectKey()}", cancellationToken);

        var githubTokenSource = string.IsNullOrWhiteSpace(configurationHelper.GhCliToken) ? "GITHUB_TOKEN fallback" : "GH_CLI_TOKEN";
        logger.Info($"Using {githubTokenSource} for GitHub repository operations.");
        await github.SetupGitAuthenticationAsync(cancellationToken);
        await git.PushBranchAsync(branchName, cancellationToken);

        var prBodyPath = Path.Combine(configurationHelper.GitHubWorkspace, ".sonar-copilot", "pull-request-body.md");
        await File.WriteAllTextAsync(
            prBodyPath,
            prBodyBuilder.Build(enrichedIssues, summary),
            cancellationToken);
        var prUrl = await github.CreatePullRequestAsync(
            $"Fix SonarQube issues for {configurationHelper.GetSonarProjectKey()}",
            prBodyPath,
            baseBranch,
            branchName,
            cancellationToken);

        summary.PullRequestUrl = prUrl;
        WriteOutput("pull_request_url", prUrl);
        summary.Write();
        return ExitCodes.Success;
    }

    private void WriteOutput(string name, string value)
    {
        var outputPath = configurationHelper.GitHubOutput;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        File.AppendAllText(outputPath, $"{name}={value}{Environment.NewLine}");
    }
}
