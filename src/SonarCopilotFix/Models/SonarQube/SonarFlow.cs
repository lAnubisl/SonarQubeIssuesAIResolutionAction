namespace SonarCopilotFix.Models.SonarQube;

public sealed record SonarFlow(IReadOnlyList<SonarLocation> Locations);
