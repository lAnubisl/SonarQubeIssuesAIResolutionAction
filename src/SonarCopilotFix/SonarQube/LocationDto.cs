using System.Text.Json.Serialization;

namespace SonarCopilotFix.SonarQube;

internal sealed record LocationDto(
    string? Component,
    TextRangeDto? TextRange,
    [property: JsonPropertyName("msg")] string? Message);
