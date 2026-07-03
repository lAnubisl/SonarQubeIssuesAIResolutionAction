using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix;

public sealed class SonarCopilotFixApp
{
    private readonly IConfigurationHelper _configurationHelper;
    private readonly ILogger _logger;
    private readonly ISonarQubeClient _sonarQube;
    private readonly CodeSnippetReader _snippetReader;
    private readonly PromptBuilder _promptBuilder;
    private readonly PrBodyBuilder _prBodyBuilder;
    private readonly GitService _git;
    private readonly GitHubCliService _github;
    private readonly CopilotCliRunner _copilot;

    public SonarCopilotFixApp(
        IConfigurationHelper configurationHelper,
        ILogger logger,
        ISonarQubeClient sonarQube,
        CodeSnippetReader snippetReader,
        PromptBuilder promptBuilder,
        ICommandRunner commandRunner,
        PrBodyBuilder prBodyBuilder)
    {
        _configurationHelper = configurationHelper;
        _logger = logger;
        _sonarQube = sonarQube;
        _snippetReader = snippetReader;
        _promptBuilder = promptBuilder;
        _prBodyBuilder = prBodyBuilder;
        _git = new GitService(commandRunner, configurationHelper);
        _github = new GitHubCliService(commandRunner, configurationHelper, logger);
        _copilot = new CopilotCliRunner(commandRunner, configurationHelper, logger);
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.Validate(_configurationHelper);
        var summary = new JobSummary(_configurationHelper);

        var issues = await FetchIssuesAsync(summary, cancellationToken);
        if (issues.Issues.Count == 0)
        {
            return CompleteWithoutIssues(summary);
        }

        var baseBranch = await ResolveBaseBranchAsync(cancellationToken);
        summary.BaseBranch = baseBranch;
        var enrichedIssues = EnrichIssues(issues.Issues);

        if (_configurationHelper.InputDryRun)
        {
            await WriteDryRunPromptsAsync(enrichedIssues, baseBranch, summary, cancellationToken);
            return CompleteDryRun(summary);
        }

        await PrepareRepositoryAsync(baseBranch, cancellationToken);
        _logger.Info("Using GH_CLI_TOKEN for GitHub repository operations.");
        await _github.SetupGitAuthenticationAsync(cancellationToken);

        foreach (var issue in enrichedIssues)
        {
            await ProcessIssueAsync(issue, baseBranch, summary, cancellationToken);
        }

        WriteCollectionOutputs(summary);
        summary.Write();
        return ExitCodes.Success;
    }

    private async Task<SonarIssueSearchResult> FetchIssuesAsync(
        JobSummary summary,
        CancellationToken cancellationToken)
    {
        _logger.Info("Fetching SonarQube issues.");
        var issues = await _sonarQube.GetIssuesAsync(cancellationToken);
        _logger.Info($"Fetched {issues.Issues.Count} SonarQube issue(s) ({issues.TotalFound} total matching issue(s) reported by SonarQube).");
        foreach (var issue in issues.Issues)
        {
            _logger.Info($"Fetched SonarQube issue: key={issue.Key}, severity={issue.Severity ?? "UNKNOWN"}, title={issue.Message}");
        }

        summary.IssuesFound = issues.TotalFound;
        summary.SetSelectedIssues(issues.Issues);
        WriteOutput("selected_issue_count", issues.Issues.Count.ToString());
        return issues;
    }

    private int CompleteWithoutIssues(JobSummary summary)
    {
        summary.Write();
        if (_configurationHelper.InputFailIfNoIssues)
        {
            throw new ControlledFailureException("No matching SonarQube issues were found.", ExitCodes.NoIssuesFound);
        }

        _logger.Info("No matching SonarQube issues were found.");
        return ExitCodes.Success;
    }

    private async Task<string> ResolveBaseBranchAsync(CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(_configurationHelper.InputBaseBranch)
            ? await _git.DetectDefaultBranchAsync(cancellationToken)
            : _configurationHelper.InputBaseBranch;

    private IReadOnlyList<SonarIssue> EnrichIssues(IReadOnlyList<SonarIssue> issues) =>
        _configurationHelper.InputIncludeCodeSnippets
            ? _snippetReader.AddSnippets(issues)
            : issues;

    private async Task PrepareRepositoryAsync(
        string baseBranch,
        CancellationToken cancellationToken)
    {
        var initialChanges = await _git.GetChangedFilesAsync(excludeGenerated: true, cancellationToken);
        if (initialChanges.Count > 0)
        {
            throw new ControlledFailureException("The worktree has pre-existing changes outside .sonar-copilot. Refusing to continue so unrelated files are not committed.", ExitCodes.GitFailure);
        }

        await _git.SwitchBranchAsync(baseBranch, cancellationToken);
    }

    private async Task WriteDryRunPromptsAsync(
        IReadOnlyList<SonarIssue> issues,
        string baseBranch,
        JobSummary summary,
        CancellationToken cancellationToken)
    {
        var currentBranch = await _git.CurrentBranchAsync(cancellationToken);
        foreach (var issue in issues)
        {
            var promptPath = await WritePromptAsync(issue, currentBranch, baseBranch, cancellationToken);
            summary.AddIssueResult(new IssueRunResult(
                issue.Key,
                null,
                promptPath,
                [],
                null,
                null,
                "dry run"));
        }

        WriteCollectionOutputs(summary);
    }

    private async Task ProcessIssueAsync(
        SonarIssue issue,
        string baseBranch,
        JobSummary summary,
        CancellationToken cancellationToken)
    {
        var branchName = _git.BuildBranchName(issue.Key, DateTimeOffset.UtcNow);
        _logger.Info($"Starting isolated fix attempt for SonarQube issue {issue.Key} on branch {branchName}.");
        await _git.CreateBranchAsync(branchName, cancellationToken);

        try
        {
            var promptPath = await WritePromptAsync(issue, branchName, baseBranch, cancellationToken);
            var headBeforeCopilot = await _git.GetHeadCommitAsync(cancellationToken);
            var sessionSummary = await RunCopilotAsync(promptPath, cancellationToken);
            var changes = await DetectCopilotChangesAsync(headBeforeCopilot, cancellationToken);

            if (!changes.HasRepositoryChanges)
            {
                _logger.Info($"Copilot completed issue {issue.Key} without repository file changes.");
                summary.AddIssueResult(new IssueRunResult(
                    issue.Key,
                    branchName,
                    promptPath,
                    [],
                    null,
                    sessionSummary,
                    "no changes"));
                return;
            }

            await CommitUncommittedChangesAsync(issue, changes.UncommittedFiles, cancellationToken);
            var pullRequestUrl = await PublishPullRequestAsync(
                issue,
                branchName,
                promptPath,
                changes.ChangedFiles,
                sessionSummary,
                baseBranch,
                cancellationToken);
            summary.AddIssueResult(new IssueRunResult(
                issue.Key,
                branchName,
                promptPath,
                changes.ChangedFiles,
                pullRequestUrl,
                sessionSummary,
                "pull request created"));
        }
        finally
        {
            _logger.Info($"Switching back to base branch {baseBranch} after issue {issue.Key}.");
            await _git.SwitchBranchAsync(baseBranch, cancellationToken);
        }
    }

    private async Task<string> WritePromptAsync(
        SonarIssue issue,
        string currentBranch,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        var promptPath = Path.Combine(
            _configurationHelper.GitHubWorkspace,
            ".sonar-copilot",
            $"issue-{SafeFileSegment(issue.Key)}-prompt.md");
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
        await File.WriteAllTextAsync(
            promptPath,
            _promptBuilder.Build([issue], currentBranch, baseBranch),
            cancellationToken);
        return promptPath;
    }

    private int CompleteDryRun(JobSummary summary)
    {
        _logger.Info("Dry-run mode enabled. Copilot, git push, and PR creation will be skipped.");
        summary.Write();
        return ExitCodes.Success;
    }

    private async Task<string> RunCopilotAsync(
        string promptPath,
        CancellationToken cancellationToken)
    {
        _logger.Info("Running GitHub Copilot CLI.");
        return await _copilot.RunAsync(promptPath, cancellationToken);
    }

    private async Task<CopilotChanges> DetectCopilotChangesAsync(
        string headBeforeCopilot,
        CancellationToken cancellationToken)
    {
        var uncommittedFiles = await _git.GetChangedFilesAsync(excludeGenerated: true, cancellationToken);
        var headAfterCopilot = await _git.GetHeadCommitAsync(cancellationToken);
        var copilotCreatedCommits = !string.Equals(headBeforeCopilot, headAfterCopilot, StringComparison.Ordinal);
        var committedFiles = copilotCreatedCommits
            ? await _git.GetChangedFilesSinceAsync(headBeforeCopilot, excludeGenerated: true, cancellationToken)
            : [];
        var changedFiles = uncommittedFiles
            .Concat(committedFiles)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (copilotCreatedCommits)
        {
            _logger.Info("Copilot created one or more local commits. The outer workflow will preserve and push them on the generated branch.");
        }

        return new CopilotChanges(uncommittedFiles, changedFiles, copilotCreatedCommits);
    }

    private async Task CommitUncommittedChangesAsync(
        SonarIssue issue,
        IReadOnlyList<string> uncommittedFiles,
        CancellationToken cancellationToken)
    {
        if (uncommittedFiles.Count <= 0)
        {
            return;
        }

        await _git.ConfigureBotUserAsync(cancellationToken);
        await _git.StageFilesAsync(uncommittedFiles, cancellationToken);
        await _git.CommitAsync($"Fix SonarQube issue {issue.Key}", cancellationToken);
    }

    private async Task<string> PublishPullRequestAsync(
        SonarIssue issue,
        string branchName,
        string promptPath,
        IReadOnlyList<string> changedFiles,
        string sessionSummary,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        await _git.PushBranchAsync(branchName, cancellationToken);

        var issueSummary = new JobSummary(_configurationHelper)
        {
            BaseBranch = baseBranch,
            GeneratedBranch = branchName,
            PromptFile = promptPath,
            ChangedFiles = changedFiles,
            CopilotExecuted = true,
            CopilotSessionSummary = sessionSummary
        };
        issueSummary.SetSelectedIssues([issue]);
        var prBodyPath = Path.Combine(
            _configurationHelper.GitHubWorkspace,
            ".sonar-copilot",
            $"issue-{SafeFileSegment(issue.Key)}-pull-request-body.md");
        await File.WriteAllTextAsync(
            prBodyPath,
            _prBodyBuilder.Build([issue], issueSummary),
            cancellationToken);
        var prUrl = await _github.CreatePullRequestAsync(
            $"Fix SonarQube issue {issue.Key}",
            prBodyPath,
            baseBranch,
            branchName,
            cancellationToken);

        return prUrl;
    }

    private void WriteCollectionOutputs(JobSummary summary)
    {
        if (summary.PromptFiles.Count > 0)
        {
            WriteOutput("prompt_file", summary.PromptFiles[^1]);
            WriteOutput("prompt_files", System.Text.Json.JsonSerializer.Serialize(summary.PromptFiles));
        }

        if (summary.PullRequestUrls.Count > 0)
        {
            WriteOutput("pull_request_url", summary.PullRequestUrls[^1]);
            WriteOutput("pull_request_urls", System.Text.Json.JsonSerializer.Serialize(summary.PullRequestUrls));
        }
    }

    private static string SafeFileSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = value
            .Select(character => invalidCharacters.Contains(character) || character is '/' or '\\' ? '-' : character)
            .ToArray();
        return new string(characters);
    }

    private void WriteOutput(string name, string value)
    {
        var outputPath = _configurationHelper.GitHubOutput;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        File.AppendAllText(outputPath, $"{name}={value}{Environment.NewLine}");
    }

    private sealed record CopilotChanges(
        IReadOnlyList<string> UncommittedFiles,
        IReadOnlyList<string> ChangedFiles,
        bool CopilotCreatedCommits)
    {
        public bool HasRepositoryChanges => ChangedFiles.Count > 0 || CopilotCreatedCommits;
    }
}
