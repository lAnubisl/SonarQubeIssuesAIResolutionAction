using System.Text;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.GitHub;

public sealed class PrBodyBuilder
{
    public string Build(ActionInputs options, IReadOnlyList<SonarIssue> issues, JobSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"## Fix SonarQube issues for `{options.SonarProjectKey}`");
        builder.AppendLine();
        builder.AppendLine("| Field | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| SonarQube project | `{options.SonarProjectKey}` |");
        builder.AppendLine($"| SonarQube branch | `{options.SonarBranch ?? "not specified"}` |");
        builder.AppendLine($"| Base branch | `{summary.BaseBranch ?? options.BaseBranch ?? "not detected"}` |");
        builder.AppendLine($"| Generated branch | `{summary.GeneratedBranch ?? "not created"}` |");
        builder.AppendLine($"| Issues selected | `{issues.Count}` |");
        builder.AppendLine($"| Issues attempted | `{issues.Count}` |");
        builder.AppendLine($"| Validation command | `{options.ValidationCommand ?? "not configured"}` |");
        builder.AppendLine($"| Validation result | `{(summary.ValidationSucceeded is null ? "not run" : summary.ValidationSucceeded.Value ? "passed" : "failed")}` |");
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
        builder.AppendLine("## Resolution Summary");
        foreach (var issue in issues)
        {
            builder.AppendLine($"- `{issue.Key}`: attempted by GitHub Copilot CLI. Review the diff to confirm the issue is fully resolved.");
        }

        builder.AppendLine();
        builder.AppendLine("## Skipped Or Not Fixed");
        builder.AppendLine("- The automation cannot conclusively verify SonarQube closure until SonarQube re-analyzes this branch.");
        builder.AppendLine();
        builder.AppendLine("## Review Notes");
        builder.AppendLine("- These changes were generated using GitHub Copilot CLI from selected SonarQube issue context.");
        builder.AppendLine("- Human review is required before merge.");
        builder.AppendLine("- Verify that no unrelated behavior, formatting, generated files, or security-sensitive values were changed.");
        return builder.ToString();
    }
}
