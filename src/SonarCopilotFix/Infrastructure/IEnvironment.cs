namespace SonarCopilotFix.Infrastructure;

public interface IEnvironment
{
    string? Get(string name);
}

public sealed class SystemEnvironment : IEnvironment
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}
