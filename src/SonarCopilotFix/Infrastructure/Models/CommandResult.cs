namespace SonarCopilotFix.Infrastructure.Models;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Summary
    {
        get
        {
            string combined = string.Join(Environment.NewLine, StandardOutput, StandardError).Trim();
            return combined.Length <= 4000 ? combined : combined[^4000..];
        }
    }
}
