using SonarCopilotFix;
using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;

var logger = new JsonLogger();

try
{
    var environment = new SystemEnvironment();
    var options = ActionInputs.FromEnvironment(environment);
    SecretMasker.MaskKnownSecrets(environment, logger);

    var app = new SonarCopilotFixApp(
        options,
        environment,
        logger,
        new SonarQubeClient(options, logger),
        new CodeSnippetReader(),
        new PromptBuilder(),
        new CommandRunner(logger),
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
