using System.Text.Json.Serialization;

namespace SonarCopilotFix.Models.SonarQube;

internal sealed record IssueSearchResponse(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("issues")] List<IssueDto> Issues);
