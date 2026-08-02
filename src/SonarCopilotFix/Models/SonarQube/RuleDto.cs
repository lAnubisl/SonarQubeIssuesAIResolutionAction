namespace SonarCopilotFix.Models.SonarQube;

internal sealed record RuleDto(
    string? Name,
    string? HtmlDesc,
    string? MdDesc,
    IReadOnlyList<RuleDescriptionSectionDto>? DescriptionSections);
