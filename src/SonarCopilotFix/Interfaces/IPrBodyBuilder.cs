namespace SonarCopilotFix.Interfaces;

public interface IPrBodyBuilder
{
    string Build(IPullRequestResult result);
}
