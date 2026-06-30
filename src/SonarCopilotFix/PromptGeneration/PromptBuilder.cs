using System.Net;
using System.Text;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.PromptGeneration;

public sealed class PromptBuilder(IConfigurationHelper configurationHelper)
{
    public string Build(IReadOnlyList<SonarIssue> issues, string currentBranch, string baseBranch)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# SonarQube Issue Fix Request");
        builder.AppendLine();
        builder.AppendLine("## Repository Context");
        builder.AppendLine($"- Repository: `{configurationHelper.GitHubRepository}`");
        builder.AppendLine($"- Current branch: `{currentBranch}`");
        builder.AppendLine($"- Base branch: `{baseBranch}`");
        builder.AppendLine($"- SonarQube project key: `{configurationHelper.GetSonarProjectKey()}`");
        builder.AppendLine($"- SonarQube branch: `{configurationHelper.InputSonarBranch ?? "not specified"}`");
        builder.AppendLine($"- Selected issue count: `{issues.Count}`");
        builder.AppendLine();
        builder.AppendLine("## Safety Rules");
        builder.AppendLine("- Fix only the listed SonarQube issues.");
        builder.AppendLine("- Prefer minimal, targeted changes.");
        builder.AppendLine("- Preserve public behavior unless a behavior change is required to fix the issue.");
        builder.AppendLine("- Avoid unrelated refactoring, unrelated formatting changes, and generated files unless unavoidable.");
        builder.AppendLine("- Add or update tests when appropriate.");
        builder.AppendLine("- Do not suppress SonarQube rules unless there is a strong justification.");
        builder.AppendLine("- Document suspected false positives instead of blindly changing code.");
        builder.AppendLine("- Keep changes reviewable.");
        builder.AppendLine("- Do not read, print, or write token values or authentication headers.");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(configurationHelper.InputCopilotExtraInstructions))
        {
            builder.AppendLine("## Extra Instructions");
            builder.AppendLine(configurationHelper.InputCopilotExtraInstructions);
            builder.AppendLine();
        }

        builder.AppendLine("## Prioritized Issues");
        foreach (var issue in issues)
        {
            builder.AppendLine($"- `{issue.Key}` `{issue.RuleKey}` `{issue.FilePath}` line `{issue.Line?.ToString() ?? "not specified"}`: {issue.Message}");
        }

        builder.AppendLine();
        builder.AppendLine("## Issue Details");
        foreach (var (issue, index) in issues.Select((issue, index) => (issue, index + 1)))
        {
            AppendIssue(builder, issue, index);
        }

        builder.AppendLine("## Expected Output");
        builder.AppendLine("- Apply targeted code changes in the repository.");
        builder.AppendLine("- Update or add tests when the fix changes behavior or risk warrants coverage.");
        builder.AppendLine("- Leave a concise summary of changed files and issue outcomes in your command output.");
        builder.AppendLine("- If an issue cannot be fixed safely, explain why and avoid unrelated edits.");
        return builder.ToString();
    }

    private static void AppendIssue(StringBuilder builder, SonarIssue issue, int index)
    {
        builder.AppendLine($"### {index}. {issue.Key}");
        builder.AppendLine($"- SonarQube URL: {issue.IssueUrl}");
        builder.AppendLine($"- File path: `{issue.FilePath}`");
        builder.AppendLine($"- Line: `{issue.Line?.ToString() ?? "not specified"}`");
        if (issue.TextRange is not null)
        {
            builder.AppendLine($"- Text range: `{issue.TextRange.StartLine}:{issue.TextRange.StartOffset}-{issue.TextRange.EndLine}:{issue.TextRange.EndOffset}`");
        }

        builder.AppendLine($"- Message: {issue.Message}");
        builder.AppendLine($"- Rule key: `{issue.RuleKey}`");
        builder.AppendLine($"- Severity or impact: `{issue.Severity ?? "not specified"}`");
        // builder.AppendLine($"- Status: `{issue.IssueStatus ?? issue.Status ?? "not specified"}`");
        builder.AppendLine($"- Type or category: `{issue.Type ?? issue.CleanCodeAttributeCategory ?? "not specified"}`");
        builder.AppendLine($"- Effort: `{issue.Effort ?? "not specified"}`");
        // builder.AppendLine($"- Tags: `{(issue.Tags.Count == 0 ? "none" : string.Join(", ", issue.Tags))}`");
        // if (issue.Rule is not null)
        // {
        //     builder.AppendLine($"- Rule name: {issue.Rule.Name ?? "not specified"}");
        //     builder.AppendLine($"- Rule description: {CleanDescription(issue.Rule.MarkdownDescription ?? issue.Rule.HtmlDescription)}");
        // }
        // else
        // {
        //     builder.AppendLine("- Rule description: not available");
        // }

        // builder.AppendLine("- Suggested fix strategy: inspect the local snippet, apply the smallest rule-compliant change, and add focused tests when useful.");
        // builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(issue.CodeSnippet is null
            ? "Code snippet was not requested."
            : issue.CodeSnippet.FileFound
                ? issue.CodeSnippet.Content
                : $"Local file not found: {issue.CodeSnippet.Content}");
        builder.AppendLine("```");
        builder.AppendLine();
    }

    private static string CleanDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "not available";
        }

        var decoded = WebUtility.HtmlDecode(description.Replace("<p>", " ", StringComparison.OrdinalIgnoreCase).Replace("</p>", " ", StringComparison.OrdinalIgnoreCase));
        return decoded.Length > 1200 ? decoded[..1200] + "..." : decoded.Trim();
    }
}
