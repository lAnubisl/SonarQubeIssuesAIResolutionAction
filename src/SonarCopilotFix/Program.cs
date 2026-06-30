using SonarCopilotFix;
using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;

var logger = new TextLogger();

try
{
    var configurationHelper = new ConfigurationHelper();
    ConfigurationValidator.Validate(configurationHelper);
    SecretMasker.MaskKnownSecrets(configurationHelper, logger);

    var app = new SonarCopilotFixApp(
        configurationHelper,
        logger,
        new SonarQubeClient(configurationHelper, logger),
        new CodeSnippetReader(configurationHelper),
        new PromptBuilder(configurationHelper),
        new CommandRunner(logger, configurationHelper),
        new PrBodyBuilder(configurationHelper));

    return await app.RunAsync();
}
catch (ControlledFailureException ex)
{
    logger.Error(ex.Message);
    return ex.ExitCode;
}
catch (Exception ex)
{
    logger.Error("Unhandled failure.", ex);
    return ExitCodes.UnhandledFailure;
}
