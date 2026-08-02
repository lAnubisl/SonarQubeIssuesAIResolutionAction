using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Interfaces;

public interface ICodeSnippetReader
{
    IReadOnlyList<SonarIssue> AddSnippets(IReadOnlyList<SonarIssue> issues);
    CodeSnippet ReadSnippet(string relativePath, int? line);
}
