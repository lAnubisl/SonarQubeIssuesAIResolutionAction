namespace SonarCopilotFix.SonarQube.Models;

public sealed record SonarFlow(IReadOnlyList<SonarLocation> Locations);
