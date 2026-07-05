namespace SonarCopilotFix.SonarQube.Models;

internal sealed record RuleDto(
    string? Name,
    string? HtmlDesc,
    string? MdDesc,
    IReadOnlyList<RuleDescriptionSectionDto>? DescriptionSections);

internal sealed record RuleDescriptionSectionDto(
    string? Key,
    string? Content);
