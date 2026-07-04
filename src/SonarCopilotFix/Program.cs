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
    var commandRunner = new CommandRunner(logger, configurationHelper);
    var prBodyBuilder = new PrBodyBuilder(configurationHelper);
    using var sonarQubeClient = new SonarQubeClient(
        configurationHelper,
        logger,
        new SonarQubeHttpClient(configurationHelper),
        new CodeSnippetReader(configurationHelper),
        disposeClient: true);

    var app = new SonarCopilotFixApp(
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
