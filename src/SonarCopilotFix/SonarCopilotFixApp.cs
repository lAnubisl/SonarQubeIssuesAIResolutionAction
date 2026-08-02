using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix;

public sealed class SonarCopilotFixApp
{
    private readonly IConfigurationHelper _configurationHelper;
    private readonly ILogger _logger;
    private readonly ISonarQubeClient _sonarQube;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IStepSummaryWriter _stepSummaryWriter;
    private readonly IGitService _git;
    private readonly IGitHubCliService _github;
    private readonly ICopilotCliRunner _copilot;

    public SonarCopilotFixApp(
        IConfigurationHelper configurationHelper,
        ILogger logger,
        ISonarQubeClient sonarQube,
        IPromptBuilder promptBuilder,
        IStepSummaryWriter stepSummaryWriter,
        IGitService git,
        IGitHubCliService github,
        ICopilotCliRunner copilot)
    {
        _configurationHelper = configurationHelper;
        _logger = logger;
        _sonarQube = sonarQube;
        _promptBuilder = promptBuilder;
        _stepSummaryWriter = stepSummaryWriter;
        _git = git;
        _github = github;
        _copilot = copilot;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.Validate(_configurationHelper);
        ActionSummary summary = new();

        SonarIssueSearchResult issues = await FetchIssuesAsync(summary, cancellationToken);
        if (issues.Issues.Count == 0)
        {
            return CompleteWithoutIssues(summary);
        }

        string baseBranch = await _git.ResolveBaseBranchAsync(cancellationToken);
        IReadOnlyList<SonarIssue> enrichedIssues = _sonarQube.EnrichIssues(issues.Issues);
        IReadOnlyList<IssueGroup> issueGroups =
            await _sonarQube.GroupIssuesByRuleAsync(enrichedIssues, cancellationToken);
        _logger.Info($"Grouped {enrichedIssues.Count} selected issue(s) into {issueGroups.Count} rule group(s).");

        await PrepareRepositoryAsync(baseBranch, cancellationToken);
        _logger.Info("Using GH_TOKEN for GitHub repository operations.");
        await _github.SetupGitAuthenticationAsync(cancellationToken);

        foreach (IssueGroup issueGroup in issueGroups)
        {
            await ProcessIssueGroupAsync(issueGroup, baseBranch, summary, cancellationToken);
        }

        _stepSummaryWriter.Write(summary);
        return ExitCodes.Success;
    }

    private async Task<SonarIssueSearchResult> FetchIssuesAsync(
        ActionSummary summary,
        CancellationToken cancellationToken)
    {
        _logger.Info("Fetching SonarQube issues.");
        SonarIssueSearchResult issues = await _sonarQube.GetIssuesAsync(cancellationToken);
        _logger.Info($"Fetched {issues.Issues.Count} SonarQube issue(s) ({issues.TotalFound} total matching issue(s) reported by SonarQube).");
        foreach (SonarIssue issue in issues.Issues)
        {
            _logger.Info($"Fetched SonarQube issue: key={issue.Key}, severity={issue.Severity ?? "UNKNOWN"}, title={issue.Message}");
        }

        summary.IssuesFound = issues.TotalFound;
        summary.SetSelectedIssues(issues.Issues);
        return issues;
    }

    private int CompleteWithoutIssues(ActionSummary summary)
    {
        _stepSummaryWriter.Write(summary);
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
        IReadOnlyList<string> initialChanges = await _git.GetChangedFilesAsync(excludeGenerated: true, cancellationToken);
        if (initialChanges.Count > 0)
        {
            throw new ControlledFailureException("The worktree has pre-existing changes outside .sonar-copilot. Refusing to continue so unrelated files are not committed.", ExitCodes.GitFailure);
        }

        await _git.SwitchBranchAsync(baseBranch, cancellationToken);
    }

    private async Task ProcessIssueGroupAsync(
        IssueGroup issueGroup,
        string baseBranch,
        ActionSummary actionSummary,
        CancellationToken cancellationToken)
    {
        string generatedBranch = _git.BuildBranchName(issueGroup.RuleKey, DateTimeOffset.UtcNow);
        _logger.Info($"Starting isolated fix attempt for rule {issueGroup.RuleKey} with {issueGroup.Issues.Count} issue(s) on branch {generatedBranch}.");
        await _git.CreateBranchAsync(generatedBranch, cancellationToken);

        try
        {
            string prompt = _promptBuilder.Build(issueGroup, generatedBranch, baseBranch);
            string headBeforeCopilot = await _git.GetHeadCommitAsync(cancellationToken);
            string copilotSessionSummary = await RunCopilotAsync(prompt, cancellationToken);
            CopilotChanges changes = await DetectCopilotChangesAsync(headBeforeCopilot, cancellationToken);
            PullRequestSummary pullRequestSummary = new(
                issueGroup,
                baseBranch,
                generatedBranch,
                changes.ChangedFiles,
                copilotSessionSummary);
            if (!changes.HasRepositoryChanges)
            {
                _logger.Info($"Copilot completed rule group {issueGroup.RuleKey} without repository file changes.");
                actionSummary.Add(pullRequestSummary);
                return;
            }

            await CommitUncommittedChangesAsync(issueGroup.RuleKey, changes.UncommittedFiles, cancellationToken);
            await _git.PushBranchAsync(generatedBranch, cancellationToken);
            await _github.CreatePullRequestAsync(pullRequestSummary, cancellationToken);
            actionSummary.Add(pullRequestSummary);
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
        IReadOnlyList<string> uncommittedFiles = await _git.GetChangedFilesAsync(excludeGenerated: true, cancellationToken);
        string headAfterCopilot = await _git.GetHeadCommitAsync(cancellationToken);
        bool copilotCreatedCommits = !string.Equals(headBeforeCopilot, headAfterCopilot, StringComparison.Ordinal);
        IReadOnlyList<string> committedFiles = copilotCreatedCommits
            ? await _git.GetChangedFilesSinceAsync(headBeforeCopilot, excludeGenerated: true, cancellationToken)
            : [];
        string[] changedFiles = uncommittedFiles
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
        string ruleKey,
        IReadOnlyList<string> uncommittedFiles,
        CancellationToken cancellationToken)
    {
        if (uncommittedFiles.Count <= 0)
        {
            return;
        }

        await _git.ConfigureBotUserAsync(cancellationToken);
        await _git.StageFilesAsync(uncommittedFiles, cancellationToken);
        await _git.CommitAsync($"Fix SonarQube rule {ruleKey}", cancellationToken);
    }
}
