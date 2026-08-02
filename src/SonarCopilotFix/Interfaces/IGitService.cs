namespace SonarCopilotFix.Interfaces;

public interface IGitService
{
    Task<string> ResolveBaseBranchAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetChangedFilesAsync(bool excludeGenerated, CancellationToken cancellationToken);
    Task<string> GetHeadCommitAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetChangedFilesSinceAsync(
        string baseCommit,
        bool excludeGenerated,
        CancellationToken cancellationToken);
    string BuildBranchName(string ruleKey, DateTimeOffset timestamp);
    Task CreateBranchAsync(string branchName, CancellationToken cancellationToken);
    Task SwitchBranchAsync(string branchName, CancellationToken cancellationToken);
    Task ConfigureBotUserAsync(CancellationToken cancellationToken);
    Task StageFilesAsync(IReadOnlyList<string> changedFiles, CancellationToken cancellationToken);
    Task CommitAsync(string message, CancellationToken cancellationToken);
    Task PushBranchAsync(string branchName, CancellationToken cancellationToken);
}
