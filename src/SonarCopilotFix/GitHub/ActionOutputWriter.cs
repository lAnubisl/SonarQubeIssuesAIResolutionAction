using System.Text.Json;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.GitHub;

public sealed class ActionOutputWriter(IConfigurationHelper configurationHelper)
{
    public void Write(string name, string value)
    {
        var outputPath = configurationHelper.GitHubOutput;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        File.AppendAllText(outputPath, $"{name}={value}{Environment.NewLine}");
    }

    public void WriteCollectionOutputs(JobSummary summary)
    {

    }
}
