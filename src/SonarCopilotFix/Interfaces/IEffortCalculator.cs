using SonarCopilotFix.Models.SonarQube;

namespace SonarCopilotFix.Interfaces;

public interface IEffortCalculator
{
    string CalculateTotal(IReadOnlyList<SonarIssue> issues);
}
