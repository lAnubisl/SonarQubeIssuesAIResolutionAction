using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.PromptGeneration;

public sealed class CodeSnippetReader
{
    public IReadOnlyList<SonarIssue> AddSnippets(string workspace, string projectKey, IReadOnlyList<SonarIssue> issues, int contextLines)
    {
        return issues.Select(issue => issue with { CodeSnippet = ReadSnippet(workspace, issue.FilePath, issue.Line, contextLines) }).ToArray();
    }

    public CodeSnippet ReadSnippet(string workspace, string relativePath, int? line, int contextLines)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new CodeSnippet(relativePath, FileFound: false, null, null, "No file path was provided by SonarQube.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(workspace, relativePath));
        var root = Path.GetFullPath(workspace);
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
        var start = Math.Max(1, targetLine - contextLines);
        var end = Math.Min(lines.Length, targetLine + contextLines);
        var content = string.Join(Environment.NewLine, Enumerable.Range(start, end - start + 1)
            .Select(number => $"{number,5}: {lines[number - 1]}"));

        return new CodeSnippet(relativePath, FileFound: true, start, end, content);
    }
}
