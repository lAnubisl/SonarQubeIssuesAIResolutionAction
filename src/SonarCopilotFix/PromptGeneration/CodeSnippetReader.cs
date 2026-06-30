using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.PromptGeneration;

public sealed class CodeSnippetReader(IConfigurationHelper configurationHelper)
{
    public IReadOnlyList<SonarIssue> AddSnippets(IReadOnlyList<SonarIssue> issues)
    {
        return issues.Select(issue => issue with { CodeSnippet = ReadSnippet(issue.FilePath, issue.Line) }).ToArray();
    }

    public CodeSnippet ReadSnippet(string relativePath, int? line)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new CodeSnippet(relativePath, FileFound: false, null, null, "No file path was provided by SonarQube.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(configurationHelper.GitHubWorkspace, relativePath));
        var root = Path.GetFullPath(configurationHelper.GitHubWorkspace);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return new CodeSnippet(relativePath, FileFound: false, null, null, "Resolved path is outside the workspace.");
        }

        if (!File.Exists(fullPath))
        {
            return new CodeSnippet(relativePath, FileFound: false, null, null, "File was not found in the checked-out repository.");
        }

        var lines = File.ReadAllLines(fullPath);
        if (lines.Length == 0)
        {
            return new CodeSnippet(relativePath, FileFound: true, 1, 1, "");
        }

        var targetLine = Math.Clamp(line ?? 1, 1, lines.Length);
        var start = Math.Max(1, targetLine - configurationHelper.InputCodeSnippetContextLines);
        var end = Math.Min(lines.Length, targetLine + configurationHelper.InputCodeSnippetContextLines);
        var content = string.Join(Environment.NewLine, Enumerable.Range(start, end - start + 1)
            .Select(number => $"{number,5}: {lines[number - 1]}"));

        return new CodeSnippet(relativePath, FileFound: true, start, end, content);
    }
}
