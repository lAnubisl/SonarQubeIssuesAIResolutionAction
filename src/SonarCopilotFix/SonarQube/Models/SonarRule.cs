namespace SonarCopilotFix.SonarQube.Models;

public sealed record SonarRule(
    SonarRuleDescriptionSection[] DescriptionSections,
    string? Name = null);

public sealed record SonarRuleDescriptionSection(
    string? Key,
    string? Content);
