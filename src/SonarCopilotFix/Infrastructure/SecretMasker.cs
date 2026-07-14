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
            /// GitHub Actions intercepts ::add-mask:: and registers that value for redaction in later logs.
            /// After that, if the same token appears in command output, GitHub should display it as ***.
            /// See https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands#masking-a-value-in-a-log
            Console.WriteLine($"::add-mask::{value}");
        }

        logger.Info("Configured log masking for known token secrets.");
    }
}
