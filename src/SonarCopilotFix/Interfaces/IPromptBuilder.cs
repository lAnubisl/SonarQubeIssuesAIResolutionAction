using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Interfaces;

public interface IPromptBuilder
{
    string Build(IssueGroup issueGroup, string currentBranch, string baseBranch);
}
