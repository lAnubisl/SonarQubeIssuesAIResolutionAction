using Moq;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.Tests;

internal static class TestData
{
    public static Mock<ILogger> MockLogger() => new();

    public static IConfigurationHelper Configuration() => MockConfigurationHelper().Object;

    public static Mock<IConfigurationHelper> MockConfigurationHelper(
        string? inputSonarHostUrl = "https://sonar.example",
        string? inputSonarProjectKey = "proj",
        IReadOnlyList<string>? inputComponents = null,
        string? inputSonarBranch = null,
        string? inputSonarOrganization = null,
        int inputMaxIssues = 10,
        IReadOnlyList<string>? inputStatuses = null,
        IReadOnlyList<string>? inputSeverities = null,
        IReadOnlyList<string>? inputImpactSoftwareQualities = null,
        IReadOnlyList<string>? inputImpactSeverities = null,
        IReadOnlyList<string>? inputCleanCodeAttributeCategories = null,
        IReadOnlyList<string>? inputRules = null,
        bool inputIncludeRuleDetails = true,
        bool inputIncludeCodeSnippets = true,
        int inputCodeSnippetContextLines = 20,
        string? inputCopilotModel = null,
        bool inputCopilotAllowAllTools = false,
        bool inputDryRun = true,
        string? sonarToken = "sonar",
        string? copilotCliToken = null,
        string? ghCliToken = null,
        string? gitHubToken = null,
        string? gitHubWorkspace = null,
        string? gitHubOutput = null,
        string? gitHubStepSummary = null)
    {
        var systemConfiguration = new ConfigurationHelper();
        var configurationHelper = new Mock<IConfigurationHelper>(MockBehavior.Strict);
        configurationHelper.SetupGet(value => value.InputSonarHostUrl).Returns(inputSonarHostUrl);
        configurationHelper.SetupGet(value => value.InputSonarProjectKey).Returns(inputSonarProjectKey);
        configurationHelper.SetupGet(value => value.InputComponents).Returns(inputComponents ?? []);
        configurationHelper.SetupGet(value => value.InputSonarBranch).Returns(inputSonarBranch);
        configurationHelper.SetupGet(value => value.InputSonarOrganization).Returns(inputSonarOrganization);
        configurationHelper.SetupGet(value => value.InputMaxIssues).Returns(inputMaxIssues);
        configurationHelper.SetupGet(value => value.InputStatuses).Returns(inputStatuses ?? ["OPEN"]);
        configurationHelper.SetupGet(value => value.InputSeverities).Returns(inputSeverities ?? []);
        configurationHelper.SetupGet(value => value.InputImpactSoftwareQualities).Returns(inputImpactSoftwareQualities ?? []);
        configurationHelper.SetupGet(value => value.InputImpactSeverities).Returns(inputImpactSeverities ?? []);
        configurationHelper.SetupGet(value => value.InputCleanCodeAttributeCategories).Returns(inputCleanCodeAttributeCategories ?? []);
        configurationHelper.SetupGet(value => value.InputRules).Returns(inputRules ?? []);
        configurationHelper.SetupGet(value => value.InputIncludeRuleDetails).Returns(inputIncludeRuleDetails);
        configurationHelper.SetupGet(value => value.InputIncludeCodeSnippets).Returns(inputIncludeCodeSnippets);
        configurationHelper.SetupGet(value => value.InputCodeSnippetContextLines).Returns(inputCodeSnippetContextLines);
        configurationHelper.SetupGet(value => value.InputCopilotModel).Returns(inputCopilotModel);
        configurationHelper.SetupGet(value => value.InputCopilotExtraInstructions).Returns((string?)null);
        configurationHelper.SetupGet(value => value.InputBranchPrefix).Returns("copilot/sonar-fixes");
        configurationHelper.SetupGet(value => value.InputBaseBranch).Returns((string?)null);
        configurationHelper.SetupGet(value => value.InputPullRequestDraft).Returns(true);
        configurationHelper.SetupGet(value => value.InputDryRun).Returns(inputDryRun);
        configurationHelper.SetupGet(value => value.InputFailIfNoIssues).Returns(false);
        configurationHelper.SetupGet(value => value.InputAllowGitHubTokenFallback).Returns(false);
        configurationHelper.SetupGet(value => value.InputCopilotAllowAllTools).Returns(inputCopilotAllowAllTools);
        configurationHelper.SetupGet(value => value.SonarToken).Returns(sonarToken);
        configurationHelper.SetupGet(value => value.CopilotCliToken).Returns(copilotCliToken);
        configurationHelper.SetupGet(value => value.GhCliToken).Returns(ghCliToken);
        configurationHelper.SetupGet(value => value.GitHubToken).Returns(gitHubToken);
        configurationHelper.SetupGet(value => value.GitHubWorkspace).Returns(gitHubWorkspace ?? Directory.GetCurrentDirectory());
        configurationHelper.SetupGet(value => value.GitHubRepository).Returns("owner/repo");
        configurationHelper.SetupGet(value => value.GitHubOutput).Returns(gitHubOutput);
        configurationHelper.SetupGet(value => value.GitHubStepSummary).Returns(gitHubStepSummary);
        configurationHelper.SetupGet(value => value.Path).Returns(systemConfiguration.Path);
        configurationHelper.SetupGet(value => value.Home).Returns(systemConfiguration.Home);
        configurationHelper.SetupGet(value => value.User).Returns(systemConfiguration.User);
        configurationHelper.SetupGet(value => value.UserProfile).Returns(systemConfiguration.UserProfile);
        configurationHelper.SetupGet(value => value.TmpDir).Returns(systemConfiguration.TmpDir);
        configurationHelper.SetupGet(value => value.Temp).Returns(systemConfiguration.Temp);
        configurationHelper.SetupGet(value => value.Tmp).Returns(systemConfiguration.Tmp);
        configurationHelper.SetupGet(value => value.Ci).Returns(systemConfiguration.Ci);
        configurationHelper.SetupGet(value => value.GitHubActions).Returns(systemConfiguration.GitHubActions);
        configurationHelper.SetupGet(value => value.RunnerTemp).Returns(systemConfiguration.RunnerTemp);
        configurationHelper.SetupGet(value => value.DotNetRoot).Returns(systemConfiguration.DotNetRoot);
        configurationHelper.SetupGet(value => value.DotNetCliTelemetryOptOut).Returns(systemConfiguration.DotNetCliTelemetryOptOut);
        return configurationHelper;
    }

    public static Mock<IConfigurationHelper> MockSystemConfigurationHelper() => MockConfigurationHelper();

    public static SonarIssue SampleIssue() => new(
        "ISSUE-1",
        "csharpsquid:S1",
        "MAJOR",
        "OPEN",
        "CODE_SMELL",
        null,
        "proj:src/A.cs",
        "src/A.cs",
        4,
        null,
        "Fix this",
        "5min",
        ["bug"],
        null,
        new Uri("https://sonar.example/project/issues?id=proj&issues=ISSUE-1&open=ISSUE-1"),
        new SonarRule("csharpsquid:S1", "Rule", "Description", null, "MAJOR", []),
        null);

    public static ISonarQubeClient MockSonarQubeClient(IReadOnlyList<SonarIssue> issues)
    {
        var client = new Mock<ISonarQubeClient>(MockBehavior.Strict);
        client
            .Setup(value => value.GetIssuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SonarIssueSearchResult(issues.Count, issues));
        return client.Object;
    }
}
