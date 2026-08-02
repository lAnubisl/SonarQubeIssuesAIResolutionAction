namespace SonarCopilotFix.Models.SonarQube;

public sealed record SonarLocation(string? Component, TextRange? TextRange, string? Message);
