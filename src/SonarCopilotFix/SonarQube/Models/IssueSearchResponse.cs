using System.Text.Json.Serialization;

namespace SonarCopilotFix.SonarQube.Models;

internal sealed record IssueSearchResponse(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("issues")] List<IssueDto> Issues);
