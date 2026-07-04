using SonarCopilotFix;
using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;

TextLogger logger = new();

try
{
    ConfigurationHelper configurationHelper = new();
    ConfigurationValidator.Validate(configurationHelper);
    SecretMasker.MaskKnownSecrets(configurationHelper, logger);
    CommandRunner commandRunner = new(logger, configurationHelper);
    PrBodyBuilder prBodyBuilder = new(configurationHelper);
    using SonarQubeClient sonarQubeClient = new(
        configurationHelper,
        logger,
        new SonarQubeHttpClient(configurationHelper),
        new CodeSnippetReader(configurationHelper),
        disposeClient: true);

    SonarCopilotFixApp app = new(
        configurationHelper,
        logger,
        sonarQubeClient,
        new PromptBuilder(configurationHelper),
        new StepSummaryWriter(configurationHelper),
        new GitService(commandRunner, configurationHelper),
        new GitHubCliService(commandRunner, configurationHelper, logger, prBodyBuilder),
        new CopilotCliRunner(commandRunner, configurationHelper, logger));

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
