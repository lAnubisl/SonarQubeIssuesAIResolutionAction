namespace SonarCopilotFix;

public static class ExitCodes
{
    public const int Success = 0;
    public const int ConfigurationError = 2;
    public const int SonarQubeError = 10;
    public const int NoIssuesFound = 20;
    public const int CopilotFailure = 30;
    public const int GitFailure = 50;
    public const int GitHubCliFailure = 60;
    public const int UnhandledFailure = 99;
}
