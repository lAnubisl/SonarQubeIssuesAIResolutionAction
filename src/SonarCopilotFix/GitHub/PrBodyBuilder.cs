using System.Text;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.GitHub;

public sealed class PrBodyBuilder(IConfigurationHelper configurationHelper) : IPrBodyBuilder
{
    private const string NotSpecified = "not specified";

    public string Build(PullRequestSummary summary)
    {
        StringBuilder builder = new();
        builder.AppendLine($"## Fix SonarQube issues for `{configurationHelper.GetSonarProjectKey()}`");
        builder.AppendLine();
        builder.AppendLine("| Field | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| SonarQube project | `{configurationHelper.GetSonarProjectKey()}` |");
        builder.AppendLine($"| SonarQube branch | `{configurationHelper.InputSonarBranch ?? "not specified"}` |");
        builder.AppendLine($"| Base branch | `{summary.BaseBranch ?? configurationHelper.InputBaseBranch ?? "not detected"}` |");
        builder.AppendLine($"| Generated branch | `{summary.GeneratedBranch ?? "not created"}` |");
        builder.AppendLine($"| Issues selected | `{summary.IssueGroup.Issues.Count}` |");
        builder.AppendLine($"| Issues attempted | `{summary.IssueGroup.Issues.Count}` |");
        builder.AppendLine($"| Total effort saved | `{summary.TotalEffortSaved}` |");
        builder.AppendLine();
        AppendProblemDescription(builder, summary.IssueGroup);

        builder.AppendLine("## Copilot Session Summary");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(string.IsNullOrWhiteSpace(summary.CopilotSessionSummary)
            ? "Copilot CLI did not write session information to stderr."
            : summary.CopilotSessionSummary);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Issue List");
        builder.AppendLine();
        builder.AppendLine("| Issue | Title | Location |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (SonarIssue issue in summary.IssueGroup.Issues)
        {
            string location = $"{issue.FilePath}:{issue.Line?.ToString() ?? NotSpecified}";
            builder.AppendLine($"| [{EscapeTableCell(issue.Key)}]({issue.IssueUrl}) | {EscapeTableCell(issue.Message)} | `{EscapeTableCell(location)}` |");
        }

        return builder.ToString();
    }

    private static void AppendProblemDescription(StringBuilder builder, IssueGroup issueGroup)
    {
        builder.AppendLine("## Problem Description");
        builder.AppendLine();
        builder.AppendLine($"SonarQube reported {issueGroup.Issues.Count} occurrence(s) of rule `{issueGroup.RuleKey}`.");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(issueGroup.Rule?.Name))
        {
            builder.AppendLine($"**{issueGroup.Rule.Name}**");
            builder.AppendLine();
        }

        if (issueGroup.Rule is null)
        {
            builder.AppendLine("Rule information was not requested or could not be retrieved from SonarQube.");
            builder.AppendLine();
            return;
        }

        if (issueGroup.Rule.DescriptionSections.Count == 0)
        {
            builder.AppendLine("SonarQube did not return a description for this rule.");
            builder.AppendLine();
            return;
        }

        foreach (SonarRuleDescriptionSection section in issueGroup.Rule.DescriptionSections)
        {
            builder.AppendLine($"### {FormatSectionTitle(section.Key)}");
            builder.AppendLine();
            builder.AppendLine(string.IsNullOrWhiteSpace(section.Content) ? NotSpecified : section.Content);
            builder.AppendLine();
        }
    }

    private static string FormatSectionTitle(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Rule description";
        }

        string title = key.Replace('_', ' ').Replace('-', ' ');
        return char.ToUpperInvariant(title[0]) + title[1..];
    }

    private static string EscapeTableCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
