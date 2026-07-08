namespace SonarCopilotFix.Infrastructure;

public static class SecretMasker
{
    public static void MaskKnownSecrets(IConfigurationHelper configurationHelper, ILogger logger)
    {
        string?[] secrets = new[]
        {
            configurationHelper.SonarToken,
            configurationHelper.CopilotCliToken,
            configurationHelper.CopilotProviderApiKey,
            configurationHelper.GhCliToken
        };

        foreach (string? value in System.Linq.Enumerable.Where(secrets, v => !string.IsNullOrWhiteSpace(v)))
        {
            Console.WriteLine($"::add-mask::{value}");
        }

        logger.Info("Configured log masking for known token secrets.");
    }
}
