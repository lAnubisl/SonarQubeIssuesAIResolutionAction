using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.PromptGeneration;

public interface ICodeSnippetReader
{
    IReadOnlyList<SonarIssue> AddSnippets(IReadOnlyList<SonarIssue> issues);
    CodeSnippet ReadSnippet(string relativePath, int? line);
}
