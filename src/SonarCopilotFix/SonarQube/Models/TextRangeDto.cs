namespace SonarCopilotFix.SonarQube.Models;

internal sealed record TextRangeDto(int StartLine, int EndLine, int StartOffset, int EndOffset);
