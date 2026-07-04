using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class StepSummaryWriter(IConfigurationHelper configurationHelper)
{
    public void Write(JobSummary summary)
    {
        var path = configurationHelper.GitHubStepSummary;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var lines = new List<string>
        {
            "# SonarQube Copilot Fix",
            "",
            $"* SonarQube project: `{configurationHelper.GetSonarProjectKey()}`",
            $"* SonarQube branch: `{configurationHelper.InputSonarBranch ?? "not specified"}`",
            $"* Issues found: `{summary.IssuesFound}`",
            $"* Issues selected: `{summary.IssuesSelected}`",
            $"* Rule groups selected: `{summary.RuleGroupsSelected}`",
            $"* Total effort saved: `{summary.TotalEffortSaved}`",
            "",
            "## Rule Group Results",
            "",
            "| Rule | Issues | Branch | Outcome | Pull request |",
            "| --- | --- | --- | --- | --- |"
        };
        if (summary.GroupResults.Count == 0)
        {
            lines.Add("| n/a | n/a | n/a | no rule groups processed | n/a |");
        }
        else
        {
            lines.AddRange(summary.GroupResults.Select(result =>
                $"| `{result.RuleKey}` | {string.Join(", ", result.IssueKeys.Select(key => $"`{key}`"))} | `{result.BranchName ?? "not created"}` | {result.Outcome} | {result.PullRequestUrl ?? "not created"} |"));
        }

        lines.AddRange(
        [
            "",
            "## Copilot Session Summary",
            ""
        ]);
        var sessions = summary.GroupResults
            .Where(result => !string.IsNullOrWhiteSpace(result.CopilotSessionSummary))
            .ToArray();
        if (sessions.Length == 0)
        {
            lines.Add("```text");
            lines.Add(string.IsNullOrWhiteSpace(summary.CopilotSessionSummary)
                ? "Not available because Copilot CLI did not write session information to stderr."
                : summary.CopilotSessionSummary);
            lines.Add("```");
        }
        else
        {
            foreach (var session in sessions)
            {
                lines.Add($"### {session.RuleKey}");
                lines.Add("");
                lines.Add("```text");
                lines.Add(session.CopilotSessionSummary!);
                lines.Add("```");
                lines.Add("");
            }
        }

        lines.AddRange(
        [
            "",
            "## Result",
            "",
            $"* Files changed: `{summary.ChangedFiles.Count}`",
            $"* Pull requests created: `{summary.GetPullRequestUrls().Count}`"
        ]);

        File.AppendAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }
}
