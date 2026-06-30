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
    var options = ActionInputs.FromEnvironment(configurationHelper);
    SecretMasker.MaskKnownSecrets(configurationHelper, logger);

    var app = new SonarCopilotFixApp(
        options,
        configurationHelper,
        logger,
        new SonarQubeClient(options, logger),
        new CodeSnippetReader(),
        new PromptBuilder(),
        new CommandRunner(logger, configurationHelper),
        new PrBodyBuilder());

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
