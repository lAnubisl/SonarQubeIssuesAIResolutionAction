namespace SonarCopilotFix.SonarQube.Models;

public sealed record SonarRule(IReadOnlyList<SonarRuleDescriptionSection> DescriptionSections);

public sealed record SonarRuleDescriptionSection(
    string? Key,
    string? Content);
