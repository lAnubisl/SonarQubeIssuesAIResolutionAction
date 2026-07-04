namespace SonarCopilotFix.SonarQube.Models;

public sealed record TextRange(int StartLine, int EndLine, int StartOffset, int EndOffset);
