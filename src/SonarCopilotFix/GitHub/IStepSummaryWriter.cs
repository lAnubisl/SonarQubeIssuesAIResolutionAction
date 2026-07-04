namespace SonarCopilotFix.GitHub;

public interface IStepSummaryWriter
{
    void Write(ActionSummary actionSummary);
}
