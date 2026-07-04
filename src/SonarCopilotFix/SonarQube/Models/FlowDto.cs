namespace SonarCopilotFix.SonarQube.Models;

internal sealed record FlowDto(IReadOnlyList<LocationDto>? Locations);
