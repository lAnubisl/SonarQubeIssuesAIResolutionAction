namespace SonarCopilotFix.GitHub;

public interface IPrBodyBuilder
{
    string Build(PullRequestSummary summary);
}
