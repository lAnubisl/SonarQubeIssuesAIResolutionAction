using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.Models;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix;

public sealed class SonarCopilotFixApp
{
    private readonly IConfigurationHelper _configurationHelper;
    private readonly ILogger _logger;
    private readonly ISonarQubeClient _sonarQube;
    private readonly PromptBuilder _promptBuilder;
    private readonly PrBodyBuilder _prBodyBuilder;
    private readonly GitService _git;
    private readonly GitHubCliService _github;
    private readonly CopilotCliRunner _copilot;

    public SonarCopilotFixApp(
        IConfigurationHelper configurationHelper,
        ILogger logger,
        ISonarQubeClient sonarQube,
        PromptBuilder promptBuilder,
        ICommandRunner commandRunner,
        PrBodyBuilder prBodyBuilder)
    {
        _configurationHelper = configurationHelper;
        _logger = logger;
        _sonarQube = sonarQube;
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

        var baseBranch = await _git.ResolveBaseBranchAsync(cancellationToken);
        summary.BaseBranch = baseBranch;
        var enrichedIssues = _sonarQube.EnrichIssues(issues.Issues);
        var issueGroups = _sonarQube.GroupIssuesByRule(enrichedIssues);
        _logger.Info($"Grouped {enrichedIssues.Count} selected issue(s) into {issueGroups.Count} rule group(s).");

        await PrepareRepositoryAsync(baseBranch, cancellationToken);
        _logger.Info("Using GH_CLI_TOKEN for GitHub repository operations.");
        await _github.SetupGitAuthenticationAsync(cancellationToken);

        foreach (var issueGroup in issueGroups)
        {
            await ProcessIssueGroupAsync(issueGroup, baseBranch, summary, cancellationToken);
        }

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
            var prompt = _promptBuilder.Build(issueGroup.Issues, branchName, baseBranch);
            var headBeforeCopilot = await _git.GetHeadCommitAsync(cancellationToken);
            var sessionSummary = await RunCopilotAsync(prompt, cancellationToken);
            var changes = await DetectCopilotChangesAsync(headBeforeCopilot, cancellationToken);

            if (!changes.HasRepositoryChanges)
            {
                _logger.Info($"Copilot completed rule group {issueGroup.RuleKey} without repository file changes.");
                summary.AddGroupResult(new GroupRunResult(
                    issueGroup.RuleKey,
                    issueGroup.Issues.Select(issue => issue.Key).ToArray(),
                    branchName,
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
                changes.ChangedFiles,
                sessionSummary,
                baseBranch,
                cancellationToken);
            summary.AddGroupResult(new GroupRunResult(
                issueGroup.RuleKey,
                issueGroup.Issues.Select(issue => issue.Key).ToArray(),
                branchName,
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

    private async Task<string> RunCopilotAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        _logger.Info("Running GitHub Copilot CLI.");
        return await _copilot.RunAsync(prompt, cancellationToken);
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
            ChangedFiles = changedFiles,
            CopilotSessionSummary = sessionSummary
        };
        issueSummary.SetSelectedIssues(issueGroup.Issues);
        var prBody = _prBodyBuilder.Build(issueGroup.Issues, issueSummary);
        var prUrl = await _github.CreatePullRequestAsync(
            $"Fix SonarQube rule {issueGroup.RuleKey} ({issueGroup.Issues.Count} issue(s))",
            prBody,
            baseBranch,
            branchName,
            cancellationToken);

        return prUrl;
    }

    private sealed record CopilotChanges(
        IReadOnlyList<string> UncommittedFiles,
        IReadOnlyList<string> ChangedFiles,
        bool CopilotCreatedCommits)
    {
        public bool HasRepositoryChanges => ChangedFiles.Count > 0 || CopilotCreatedCommits;
    }

}
