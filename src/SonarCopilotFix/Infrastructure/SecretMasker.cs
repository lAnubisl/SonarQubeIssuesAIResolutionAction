namespace SonarCopilotFix.Infrastructure;

public static class SecretMasker
{
    public static void MaskKnownSecrets(IConfigurationHelper configurationHelper, ILogger logger)
    {
        var secrets = new[]
        {
            configurationHelper.SonarToken,
            configurationHelper.CopilotCliToken,
            configurationHelper.GhCliToken,
            configurationHelper.GitHubToken
        };
        foreach (var value in secrets)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Console.WriteLine($"::add-mask::{value}");
            }
        }

        logger.Info("Configured log masking for known token secrets.");
    }
}
