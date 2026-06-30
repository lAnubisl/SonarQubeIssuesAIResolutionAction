namespace SonarCopilotFix.SonarQube;

public sealed record CodeSnippet(
    string FilePath,
    bool FileFound,
    int? StartLine,
    int? EndLine,
    string Content);
