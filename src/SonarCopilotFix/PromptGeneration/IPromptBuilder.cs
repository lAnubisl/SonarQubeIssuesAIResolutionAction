using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.PromptGeneration;

public interface IPromptBuilder
{
    string Build(IReadOnlyList<SonarIssue> issues, string currentBranch, string baseBranch);
}
