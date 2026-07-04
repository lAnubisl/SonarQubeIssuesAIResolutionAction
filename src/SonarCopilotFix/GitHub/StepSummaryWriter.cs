using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class StepSummaryWriter(IConfigurationHelper configurationHelper) : IStepSummaryWriter
{
    public void Write(ActionSummary actionSummary)
    {
        string? path = configurationHelper.GitHubStepSummary;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        List<string> lines =
        [
            "# SonarQube Copilot Fix",
            "",
            $"* SonarQube project: `{configurationHelper.GetSonarProjectKey()}`",
            $"* SonarQube branch: `{configurationHelper.InputSonarBranch ?? "not specified"}`",
            $"* Issues found: `{actionSummary.IssuesFound}`",
            $"* Issues selected: `{actionSummary.IssuesSelected}`",
            $"* Rule groups selected: `{actionSummary.RuleGroupsSelected}`",
            $"* Total effort saved: `{actionSummary.TotalEffortSaved}`",
            "",
            "## Rule Group Results",
            "",
            "| Rule | Issues | Branch | Pull request |",
            "| --- | --- | --- | --- |"
        ];
        if (actionSummary.PullRequestSummaries.Count == 0)
        {
            lines.Add("| n/a | n/a | n/a | no rule groups processed |");
        }
        else
        {
            foreach (PullRequestSummary result in actionSummary.PullRequestSummaries)
            {
                string issues = string.Join(", ", result.IssueGroup.Issues.Select(i => i.Key).Select(key => $"`{key}`"));
                string pullRequest = string.IsNullOrWhiteSpace(result.PullRequestUrl)
                    ? "not created"
                    : result.PullRequestUrl;
                lines.Add($"| `{result.IssueGroup.RuleKey}` | {issues} | `{result.GeneratedBranch}` | {pullRequest} |");
            }
        }

        lines.AddRange(
        [
            "",
            "## Copilot Session Summary",
            ""
        ]);
        PullRequestSummary[] sessions = actionSummary.PullRequestSummaries
            .Where(result => !string.IsNullOrWhiteSpace(result.CopilotSessionSummary))
            .ToArray();
        if (sessions.Length == 0)
        {
            lines.Add("```text");
            lines.Add("Not available because Copilot CLI did not write session information to stderr.");
            lines.Add("```");
        }
        else
        {
            foreach (PullRequestSummary? session in sessions)
            {
                lines.Add($"### {session.IssueGroup.RuleKey}");
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
            $"* Files changed: `{actionSummary.GetChangedFiles().Count}`",
            $"* Pull requests created: `{actionSummary.GetPullRequestUrls().Count}`"
        ]);

        File.AppendAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }
}
