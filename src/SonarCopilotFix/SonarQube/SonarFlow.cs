namespace SonarCopilotFix.SonarQube;

public sealed record SonarFlow(IReadOnlyList<SonarLocation> Locations);
