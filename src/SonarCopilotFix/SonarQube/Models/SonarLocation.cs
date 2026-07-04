namespace SonarCopilotFix.SonarQube.Models;

public sealed record SonarLocation(string? Component, TextRange? TextRange, string? Message);
