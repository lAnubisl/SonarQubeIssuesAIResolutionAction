namespace SonarCopilotFix;

public sealed record CopilotChanges(
    IReadOnlyList<string> UncommittedFiles,
    IReadOnlyList<string> ChangedFiles,
    bool CopilotCreatedCommits)
{
    public bool HasRepositoryChanges => ChangedFiles.Count > 0 || CopilotCreatedCommits;
}
