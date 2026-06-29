namespace SonarCopilotFix.Infrastructure;

public static class SecretMasker
{
    private static readonly string[] SecretNames = ["SONAR_TOKEN", "COPILOT_CLI_TOKEN", "GH_CLI_TOKEN", "GITHUB_TOKEN"];

    public static void MaskKnownSecrets(IEnvironment environment, TextLogger logger)
    {
        foreach (var name in SecretNames)
        {
            var value = environment.Get(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                Console.WriteLine($"::add-mask::{value}");
            }
        }

        logger.Info("Configured log masking for known token secrets.");
    }
}
