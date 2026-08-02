
ILogger logger = new TextLogger();

try
{
    IConfigurationHelper configurationHelper = new ConfigurationHelper();
    ISecretMasker secretMasker = new SecretMasker(configurationHelper, logger);
    secretMasker.MaskKnownSecrets();
    ICommandRunner commandRunner = new CommandRunner(logger, configurationHelper);
    IPrBodyBuilder prBodyBuilder = new PrBodyBuilder(configurationHelper);
    IEffortCalculator effortCalculator = new SonarIssueEffortCalculator();
    using ISonarQubeClient sonarQubeClient = new SonarQubeClient(
        configurationHelper,
        logger,
        new SonarQubeHttpClient(configurationHelper),
        new CodeSnippetReader(configurationHelper),
        disposeClient: true);

    IApplication app = new SonarCopilotFixApp(
        configurationHelper,
        logger,
        sonarQubeClient,
        new PromptBuilder(configurationHelper),
        new StepSummaryWriter(configurationHelper),
        new GitService(commandRunner, configurationHelper),
        new GitHubCliService(commandRunner, configurationHelper, logger, prBodyBuilder),
        new CopilotCliRunner(commandRunner, configurationHelper, logger),
        effortCalculator);

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
