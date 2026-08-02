namespace SonarCopilotFix.Tests;

internal sealed class EnvironmentScope : IDisposable
{
    private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

    public EnvironmentScope(params (string Name, string? Value)[] values)
    {
        foreach ((string name, string? value) in values)
        {
            _original[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach ((string name, string? value) in _original)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
