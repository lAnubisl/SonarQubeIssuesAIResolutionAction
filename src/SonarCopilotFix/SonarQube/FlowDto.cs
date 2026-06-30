namespace SonarCopilotFix.SonarQube;

internal sealed record FlowDto(IReadOnlyList<LocationDto>? Locations);
