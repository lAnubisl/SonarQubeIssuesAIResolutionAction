namespace SonarCopilotFix.SonarQube.Models;

internal sealed record RuleDto(
    IReadOnlyList<RuleDescriptionSectionDto>? DescriptionSections);

internal sealed record RuleDescriptionSectionDto(
    string? Key,
    string? Content);
