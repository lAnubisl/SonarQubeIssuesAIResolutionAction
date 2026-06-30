namespace SonarCopilotFix.SonarQube;

internal sealed record TextRangeDto(int StartLine, int EndLine, int StartOffset, int EndOffset);
