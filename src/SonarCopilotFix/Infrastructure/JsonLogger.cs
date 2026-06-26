using System.Text.Json;

namespace SonarCopilotFix.Infrastructure;

public sealed class JsonLogger
{
    public void Info(string message) => Write("info", message);
    public void Warn(string message) => Write("warning", message);
    public void Error(string message, Exception? exception = null) => Write("error", message, exception);

    private static void Write(string level, string message, Exception? exception = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["level"] = level,
            ["message"] = message
        };

        if (exception is not null)
        {
            payload["exception"] = exception.GetType().Name;
            payload["detail"] = exception.Message;
        }

        Console.WriteLine(JsonSerializer.Serialize(payload));
    }
}
