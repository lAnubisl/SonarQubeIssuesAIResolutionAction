using System.Text;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.GitHub;

public sealed class PrBodyBuilder(IConfigurationHelper configurationHelper)
{
    public string Build(IReadOnlyList<SonarIssue> issues, JobSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"## Fix SonarQube issues for `{configurationHelper.GetSonarProjectKey()}`");
        builder.AppendLine();
        builder.AppendLine("| Field | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| SonarQube project | `{configurationHelper.GetSonarProjectKey()}` |");
        builder.AppendLine($"| SonarQube branch | `{configurationHelper.InputSonarBranch ?? "not specified"}` |");
        builder.AppendLine($"| Base branch | `{summary.BaseBranch ?? configurationHelper.InputBaseBranch ?? "not detected"}` |");
        builder.AppendLine($"| Generated branch | `{summary.GeneratedBranch ?? "not created"}` |");
        builder.AppendLine($"| Issues selected | `{issues.Count}` |");
        builder.AppendLine($"| Issues attempted | `{issues.Count}` |");
        builder.AppendLine();
        builder.AppendLine("## Copilot Session Summary");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(string.IsNullOrWhiteSpace(summary.CopilotSessionSummary)
            ? "Copilot CLI did not write session information to stderr."
            : summary.CopilotSessionSummary);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Issue List");
        foreach (var issue in issues)
        {
            builder.AppendLine($"- [{issue.Key}]({issue.IssueUrl}) `{issue.RuleKey}` `{issue.FilePath}` line `{issue.Line?.ToString() ?? "not specified"}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Changed Files");
        if (summary.ChangedFiles.Count == 0)
        {
            builder.AppendLine("- No changed files were detected.");
        }
        else
        {
            foreach (var file in summary.ChangedFiles)
            {
                builder.AppendLine($"- `{file}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Review Notes");
        builder.AppendLine("- These changes were generated using GitHub Copilot CLI from selected SonarQube issue context.");
        builder.AppendLine("- Human review is required before merge.");
        builder.AppendLine("- Validation is delegated to the repository's pull request checks.");
        builder.AppendLine("- Verify that no unrelated behavior, formatting, generated files, or security-sensitive values were changed.");
        return builder.ToString();
    }
}
