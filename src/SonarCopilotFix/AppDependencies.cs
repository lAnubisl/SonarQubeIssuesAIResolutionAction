using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;

namespace SonarCopilotFix;

public sealed record AppDependencies(
    IGitService Git,
    IGitHubCliService Github,
    ICopilotCliRunner Copilot);
