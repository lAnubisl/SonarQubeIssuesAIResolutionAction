namespace SonarCopilotFix.Interfaces;

public interface IStepSummaryWriter
{
    void Write(IRunSummary runSummary);
}
