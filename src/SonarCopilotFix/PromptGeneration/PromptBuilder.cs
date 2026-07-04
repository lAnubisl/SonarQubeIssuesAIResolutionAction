using System.Net;
using System.Text;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.PromptGeneration;

public sealed class PromptBuilder(IConfigurationHelper configurationHelper) : IPromptBuilder
{
    private const string NotSpecified = "not specified";

    public string Build(IssueGroup issueGroup, string currentBranch, string baseBranch)
    {
        IReadOnlyList<SonarIssue> issues = issueGroup.Issues;
        StringBuilder builder = new();
        builder.AppendLine("# SonarQube Issue Fix Request");
        builder.AppendLine();
        builder.AppendLine("You are a expert software enginner with deep knowledge of code maintenance and code quality.");
        builder.AppendLine("Your job is to fix the listed SonarQube issues in the repository.");
        builder.AppendLine("You are not allowed to switch git branches, create commits, amend commits, or bypass Git hooks. The fix branch is already checked out. Leave all file changes uncommitted; the external process will commit and push them.");
        builder.AppendLine();
        builder.AppendLine("## Repository Context");
        builder.AppendLine($"- Repository: `{configurationHelper.GitHubRepository}`");
        builder.AppendLine($"- Current branch: `{currentBranch}`");
        builder.AppendLine($"- Base branch: `{baseBranch}`");
        builder.AppendLine($"- SonarQube project key: `{configurationHelper.GetSonarProjectKey()}`");
        builder.AppendLine($"- SonarQube branch: `{configurationHelper.InputSonarBranch ?? NotSpecified}`");
        builder.AppendLine($"- Selected issue count: `{issues.Count}`");
        builder.AppendLine();

        AppendRuleDetails(builder, issueGroup);

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
        foreach (SonarIssue issue in issues)
        {
            builder.AppendLine($"- `{issue.Key}` `{issue.RuleKey}` `{issue.FilePath}` line `{issue.Line?.ToString() ?? NotSpecified}`: {issue.Message}");
        }

        builder.AppendLine();
        builder.AppendLine("## Issue Details");
        foreach ((SonarIssue? issue, int index) in issues.Select((issue, index) => (issue, index + 1)))
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

    private static void AppendRuleDetails(StringBuilder builder, IssueGroup issueGroup)
    {
        if (issueGroup.Rule is null)
        {
            return;
        }

        builder.AppendLine("## Rule Details");
        builder.AppendLine($"- Key: `{issueGroup.Rule.Key}`");
        builder.AppendLine($"- Name: {issueGroup.Rule.Name ?? NotSpecified}");
        builder.AppendLine($"- Severity: `{issueGroup.Rule.Severity ?? NotSpecified}`");
        builder.AppendLine($"- Description: {issueGroup.Rule.MarkdownDescription ?? issueGroup.Rule.HtmlDescription ?? NotSpecified}");
        builder.AppendLine();
    }

    private static void AppendIssue(StringBuilder builder, SonarIssue issue, int index)
    {
        builder.AppendLine($"### {index}. {issue.Key}");
        builder.AppendLine($"- SonarQube URL: {issue.IssueUrl}");
        builder.AppendLine($"- File path: `{issue.FilePath}`");
        builder.AppendLine($"- Line: `{issue.Line?.ToString() ?? NotSpecified}`");
        if (issue.TextRange is not null)
        {
            builder.AppendLine($"- Text range: `{issue.TextRange.StartLine}:{issue.TextRange.StartOffset}-{issue.TextRange.EndLine}:{issue.TextRange.EndOffset}`");
        }

        builder.AppendLine($"- Message: {issue.Message}");
        builder.AppendLine($"- Rule key: `{issue.RuleKey}`");
        builder.AppendLine($"- Severity or impact: `{issue.Severity ?? NotSpecified}`");
        builder.AppendLine($"- Type or category: `{issue.Type ?? issue.CleanCodeAttributeCategory ?? NotSpecified}`");
        builder.AppendLine($"- Effort: `{issue.Effort ?? NotSpecified}`");
        builder.AppendLine("```text");
        string codeSnippetText = GetCodeSnippetText(issue);
        builder.AppendLine(codeSnippetText);
        builder.AppendLine("```");
        builder.AppendLine();
    }

    private static string GetCodeSnippetText(SonarIssue issue)
    {
        if (issue.CodeSnippet is null)
        {
            return "Code snippet was not requested.";
        }

        return issue.CodeSnippet.FileFound
            ? issue.CodeSnippet.Content
            : $"Local file not found: {issue.CodeSnippet.Content}";
    }
}
