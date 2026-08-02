namespace SonarCopilotFix.Models.SonarQube;

internal sealed record FlowDto(IReadOnlyList<LocationDto>? Locations);
