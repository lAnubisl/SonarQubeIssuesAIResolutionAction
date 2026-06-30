using System.Globalization;

namespace SonarCopilotFix.Infrastructure;

public sealed class TextLogger : ILogger
{
    public void Info(string message) => Write("info", message);
    public void Warn(string message) => Write("warning", message);
    public void Error(string message, Exception? exception = null) => Write("error", message, exception);

    private static void Write(string level, string message, Exception? exception = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var exceptionPart = exception is null
            ? ""
            : $" | {exception.GetType().Name}: {exception.Message}";
        Console.WriteLine($"{timestamp} | {level} | {message}{exceptionPart}");
    }
}
