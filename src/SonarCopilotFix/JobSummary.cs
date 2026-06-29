namespace SonarCopilotFix;

public sealed class JobSummary(ActionInputs options)
{
    public int IssuesFound { get; set; }
    public int IssuesSelected { get; set; }
    public bool DryRun { get; set; }
    public bool CopilotExecuted { get; set; }
    public string? PromptFile { get; set; }
    public string? BaseBranch { get; set; }
    public string? GeneratedBranch { get; set; }
    public IReadOnlyList<string> ChangedFiles { get; set; } = [];
    public string? PullRequestUrl { get; set; }

    public void Write(Infrastructure.IEnvironment environment)
    {
        var path = environment.Get("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var lines = new List<string>
        {
            "# SonarQube Copilot Fix",
            "",
            $"* SonarQube project: `{options.SonarProjectKey}`",
            $"* SonarQube branch: `{options.SonarBranch ?? "not specified"}`",
            $"* Issues found: `{IssuesFound}`",
            $"* Issues selected: `{IssuesSelected}`",
            $"* Dry run: `{DryRun || options.DryRun}`",
            $"* Copilot CLI executed: `{CopilotExecuted}`",
            $"* Files changed: `{ChangedFiles.Count}`",
            $"* Pull request: `{PullRequestUrl ?? "not created"}`",
            $"* Prompt file: `{PromptFile ?? "not generated"}`"
        };

        File.AppendAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }
}
