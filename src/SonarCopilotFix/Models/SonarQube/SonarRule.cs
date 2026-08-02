namespace SonarCopilotFix.Models.SonarQube;

public sealed record SonarRule(
    SonarRuleDescriptionSection[] DescriptionSections,
    string? Name = null);
