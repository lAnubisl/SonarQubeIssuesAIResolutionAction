using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.PromptGeneration;

public interface IPromptBuilder
{
    string Build(IssueGroup issueGroup, string currentBranch, string baseBranch);
}
