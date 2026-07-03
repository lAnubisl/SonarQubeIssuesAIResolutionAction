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
        CommandRunner commandRunner,
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
        var repository = await PrepareRepositoryAsync(summary, cancellationToken);
        var promptPath = await WritePromptAsync(enrichedIssues, repository.CurrentBranch, baseBranch, summary, cancellationToken);

        if (_configurationHelper.InputDryRun)
        {
            return CompleteDryRun(summary);
        }

        var branchName = repository.BranchName
            ?? throw new InvalidOperationException("A fix branch must be created before Copilot runs.");
        var headBeforeCopilot = await _git.GetHeadCommitAsync(cancellationToken);
        await RunCopilotAsync(promptPath, summary, cancellationToken);
        var changes = await DetectCopilotChangesAsync(headBeforeCopilot, summary, cancellationToken);
        if (!changes.HasRepositoryChanges)
        {
            return CompleteWithoutChanges(summary);
        }

        await CommitUncommittedChangesAsync(changes.UncommittedFiles, cancellationToken);
        await PublishPullRequestAsync(
            enrichedIssues,
            summary,
            baseBranch,
            branchName,
            cancellationToken);
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
        summary.IssuesSelected = issues.Issues.Count;
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

    private async Task<RepositoryPreparation> PrepareRepositoryAsync(
        JobSummary summary,
        CancellationToken cancellationToken)
    {
        if (_configurationHelper.InputDryRun)
        {
            var currentBranch = await _git.CurrentBranchAsync(cancellationToken);
            return new RepositoryPreparation(currentBranch, null);
        }

        var initialChanges = await _git.GetChangedFilesAsync(excludeGenerated: true, cancellationToken);
        if (initialChanges.Count > 0)
        {
            throw new ControlledFailureException("The worktree has pre-existing changes outside .sonar-copilot. Refusing to continue so unrelated files are not committed.", ExitCodes.GitFailure);
        }

        var branchName = _git.BuildBranchName(DateTimeOffset.UtcNow);
        summary.GeneratedBranch = branchName;
        await _git.CreateBranchAsync(branchName, cancellationToken);
        return new RepositoryPreparation(branchName, branchName);
    }

    private async Task<string> WritePromptAsync(
        IReadOnlyList<SonarIssue> issues,
        string currentBranch,
        string baseBranch,
        JobSummary summary,
        CancellationToken cancellationToken)
    {
        var promptPath = Path.Combine(_configurationHelper.GitHubWorkspace, ".sonar-copilot", "issues-prompt.md");
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
        await File.WriteAllTextAsync(
            promptPath,
            _promptBuilder.Build(issues, currentBranch, baseBranch),
            cancellationToken);
        WriteOutput("prompt_file", promptPath);
        summary.PromptFile = promptPath;
        return promptPath;
    }

    private int CompleteDryRun(JobSummary summary)
    {
        _logger.Info("Dry-run mode enabled. Copilot, git push, and PR creation will be skipped.");
        summary.Write();
        return ExitCodes.Success;
    }

    private async Task RunCopilotAsync(
        string promptPath,
        JobSummary summary,
        CancellationToken cancellationToken)
    {
        _logger.Info("Running GitHub Copilot CLI.");
        summary.CopilotSessionSummary = await _copilot.RunAsync(promptPath, cancellationToken);
        summary.CopilotExecuted = true;
    }

    private async Task<CopilotChanges> DetectCopilotChangesAsync(
        string headBeforeCopilot,
        JobSummary summary,
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
        summary.ChangedFiles = changedFiles;

        if (copilotCreatedCommits)
        {
            _logger.Info("Copilot created one or more local commits. The outer workflow will preserve and push them on the generated branch.");
        }

        return new CopilotChanges(uncommittedFiles, changedFiles, copilotCreatedCommits);
    }

    private int CompleteWithoutChanges(JobSummary summary)
    {
        _logger.Info("Copilot completed without repository file changes.");
        summary.Write();
        return ExitCodes.Success;
    }

    private async Task CommitUncommittedChangesAsync(
        IReadOnlyList<string> uncommittedFiles,
        CancellationToken cancellationToken)
    {
        if (uncommittedFiles.Count <= 0)
        {
            return;
        }

        await _git.ConfigureBotUserAsync(cancellationToken);
        await _git.StageFilesAsync(uncommittedFiles, cancellationToken);
        await _git.CommitAsync($"Fix SonarQube issues for {_configurationHelper.GetSonarProjectKey()}", cancellationToken);
    }

    private async Task PublishPullRequestAsync(
        IReadOnlyList<SonarIssue> issues,
        JobSummary summary,
        string baseBranch,
        string branchName,
        CancellationToken cancellationToken)
    {
        _logger.Info("Using GH_CLI_TOKEN for GitHub repository operations.");
        await _github.SetupGitAuthenticationAsync(cancellationToken);
        await _git.PushBranchAsync(branchName, cancellationToken);

        var prBodyPath = Path.Combine(_configurationHelper.GitHubWorkspace, ".sonar-copilot", "pull-request-body.md");
        await File.WriteAllTextAsync(
            prBodyPath,
            _prBodyBuilder.Build(issues, summary),
            cancellationToken);
        var prUrl = await _github.CreatePullRequestAsync(
            $"Fix SonarQube issues for {_configurationHelper.GetSonarProjectKey()}",
            prBodyPath,
            baseBranch,
            branchName,
            cancellationToken);

        summary.PullRequestUrl = prUrl;
        WriteOutput("pull_request_url", prUrl);
        summary.Write();
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

    private sealed record RepositoryPreparation(string CurrentBranch, string? BranchName);

    private sealed record CopilotChanges(
        IReadOnlyList<string> UncommittedFiles,
        IReadOnlyList<string> ChangedFiles,
        bool CopilotCreatedCommits)
    {
        public bool HasRepositoryChanges => ChangedFiles.Count > 0 || CopilotCreatedCommits;
    }
}
