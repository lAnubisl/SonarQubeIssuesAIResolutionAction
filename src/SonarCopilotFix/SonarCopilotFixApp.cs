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
        var issueGroups = GroupIssuesByRule(enrichedIssues);
        WriteOutput("selected_rule_group_count", issueGroups.Count.ToString());
        _logger.Info($"Grouped {enrichedIssues.Count} selected issue(s) into {issueGroups.Count} rule group(s).");

        await PrepareRepositoryAsync(baseBranch, cancellationToken);
        _logger.Info("Using GH_CLI_TOKEN for GitHub repository operations.");
        await _github.SetupGitAuthenticationAsync(cancellationToken);

        foreach (var issueGroup in issueGroups)
        {
            await ProcessIssueGroupAsync(issueGroup, baseBranch, summary, cancellationToken);
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
        WriteOutput("selected_rule_group_count", "0");
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

    private static IReadOnlyList<IssueGroup> GroupIssuesByRule(IReadOnlyList<SonarIssue> issues) =>
        issues
            .GroupBy(issue => issue.RuleKey, StringComparer.Ordinal)
            .Select(group => new IssueGroup(group.Key, group.ToArray()))
            .ToArray();

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

    private async Task ProcessIssueGroupAsync(
        IssueGroup issueGroup,
        string baseBranch,
        JobSummary summary,
        CancellationToken cancellationToken)
    {
        var branchName = _git.BuildBranchName(issueGroup.RuleKey, DateTimeOffset.UtcNow);
        _logger.Info($"Starting isolated fix attempt for rule {issueGroup.RuleKey} with {issueGroup.Issues.Count} issue(s) on branch {branchName}.");
        await _git.CreateBranchAsync(branchName, cancellationToken);

        try
        {
            var promptPath = await WritePromptAsync(issueGroup, branchName, baseBranch, cancellationToken);
            var headBeforeCopilot = await _git.GetHeadCommitAsync(cancellationToken);
            var sessionSummary = await RunCopilotAsync(promptPath, cancellationToken);
            var changes = await DetectCopilotChangesAsync(headBeforeCopilot, cancellationToken);

            if (!changes.HasRepositoryChanges)
            {
                _logger.Info($"Copilot completed rule group {issueGroup.RuleKey} without repository file changes.");
                summary.AddGroupResult(new GroupRunResult(
                    issueGroup.RuleKey,
                    issueGroup.Issues.Select(issue => issue.Key).ToArray(),
                    branchName,
                    promptPath,
                    [],
                    null,
                    sessionSummary,
                    "no changes"));
                return;
            }

            await CommitUncommittedChangesAsync(issueGroup, changes.UncommittedFiles, cancellationToken);
            var pullRequestUrl = await PublishPullRequestAsync(
                issueGroup,
                branchName,
                promptPath,
                changes.ChangedFiles,
                sessionSummary,
                baseBranch,
                cancellationToken);
            summary.AddGroupResult(new GroupRunResult(
                issueGroup.RuleKey,
                issueGroup.Issues.Select(issue => issue.Key).ToArray(),
                branchName,
                promptPath,
                changes.ChangedFiles,
                pullRequestUrl,
                sessionSummary,
                "pull request created"));
        }
        finally
        {
            _logger.Info($"Switching back to base branch {baseBranch} after rule group {issueGroup.RuleKey}.");
            await _git.SwitchBranchAsync(baseBranch, cancellationToken);
        }
    }

    private async Task<string> WritePromptAsync(
        IssueGroup issueGroup,
        string currentBranch,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        var promptPath = Path.Combine(
            _configurationHelper.GitHubWorkspace,
            ".sonar-copilot",
            $"rule-{SafeFileSegment(issueGroup.RuleKey)}-prompt.md");
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
        await File.WriteAllTextAsync(
            promptPath,
            _promptBuilder.Build(issueGroup.Issues, currentBranch, baseBranch),
            cancellationToken);
        return promptPath;
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
        IssueGroup issueGroup,
        IReadOnlyList<string> uncommittedFiles,
        CancellationToken cancellationToken)
    {
        if (uncommittedFiles.Count <= 0)
        {
            return;
        }

        await _git.ConfigureBotUserAsync(cancellationToken);
        await _git.StageFilesAsync(uncommittedFiles, cancellationToken);
        await _git.CommitAsync($"Fix SonarQube rule {issueGroup.RuleKey}", cancellationToken);
    }

    private async Task<string> PublishPullRequestAsync(
        IssueGroup issueGroup,
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
            CopilotSessionSummary = sessionSummary
        };
        issueSummary.SetSelectedIssues(issueGroup.Issues);
        var prBodyPath = Path.Combine(
            _configurationHelper.GitHubWorkspace,
            ".sonar-copilot",
            $"rule-{SafeFileSegment(issueGroup.RuleKey)}-pull-request-body.md");
        await File.WriteAllTextAsync(
            prBodyPath,
            _prBodyBuilder.Build(issueGroup.Issues, issueSummary),
            cancellationToken);
        var prUrl = await _github.CreatePullRequestAsync(
            $"Fix SonarQube rule {issueGroup.RuleKey} ({issueGroup.Issues.Count} issue(s))",
            prBodyPath,
            baseBranch,
            branchName,
            cancellationToken);

        return prUrl;
    }

    private void WriteCollectionOutputs(JobSummary summary)
    {
        var promptFiles = summary.GetPromptFiles();
        if (promptFiles.Count > 0)
        {
            WriteOutput("prompt_file", promptFiles[^1]);
            WriteOutput("prompt_files", System.Text.Json.JsonSerializer.Serialize(promptFiles));
        }

        var pullRequestUrls = summary.GetPullRequestUrls();
        if (pullRequestUrls.Count > 0)
        {
            WriteOutput("pull_request_url", pullRequestUrls[^1]);
            WriteOutput("pull_request_urls", System.Text.Json.JsonSerializer.Serialize(pullRequestUrls));
        }
    }

    private static string SafeFileSegment(string value)
    {
        // Replace any character that is not letter, digit, '-' or '_' with '-'.
        var characters = value
            .Select(character => (char.IsLetterOrDigit(character) || character == '-' || character == '_') ? character : '-')
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

    private sealed record IssueGroup(string RuleKey, IReadOnlyList<SonarIssue> Issues);
}
