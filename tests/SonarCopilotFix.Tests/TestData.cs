using Moq;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube;
using SonarCopilotFix.SonarQube.Models;

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
        string? inputType = null,
        IReadOnlyList<string>? inputSeverities = null,
        IReadOnlyList<string>? inputImpactSoftwareQualities = null,
        IReadOnlyList<string>? inputImpactSeverities = null,
        IReadOnlyList<string>? inputCleanCodeAttributeCategories = null,
        IReadOnlyList<string>? inputRules = null,
        bool inputIncludeRuleDetails = true,
        bool inputIncludeCodeSnippets = true,
        int inputCodeSnippetContextLines = 20,
        string? inputCopilotModel = null,
        string? inputCopilotProviderType = null,
        string? inputCopilotProviderBaseUrl = null,
        bool inputCopilotOffline = false,
        string? inputCopilotExtraInstructions = null,
        IReadOnlyList<string>? inputCopilotAllowedTools = null,
        bool inputCopilotAllowAllTools = false,
        string inputBranchPrefix = "copilot/sonar-fixes",
        string? inputBaseBranch = null,
        bool inputPullRequestDraft = true,
        bool inputFailIfNoIssues = false,
        string? sonarToken = "sonar",
        string? copilotGitHubToken = "copilot",
        string? copilotProviderApiKey = null,
        string? ghCliToken = "github",
        string? gitHubWorkspace = null,
        string gitHubRepository = "owner/repo",
        string? gitHubOutput = null,
        string? gitHubStepSummary = null,
        IReadOnlyDictionary<string, string?>? safeEnvironmentVariables = null)
    {
        ConfigurationHelper systemConfiguration = new();
        Mock<IConfigurationHelper> configurationHelper = new(MockBehavior.Strict);
        configurationHelper.SetupGet(value => value.InputSonarHostUrl).Returns(inputSonarHostUrl);
        configurationHelper.SetupGet(value => value.InputSonarProjectKey).Returns(inputSonarProjectKey);
        configurationHelper.SetupGet(value => value.InputComponents).Returns(inputComponents ?? []);
        configurationHelper.SetupGet(value => value.InputSonarBranch).Returns(inputSonarBranch);
        configurationHelper.SetupGet(value => value.InputSonarOrganization).Returns(inputSonarOrganization);
        configurationHelper.SetupGet(value => value.InputMaxIssues).Returns(inputMaxIssues);
        configurationHelper.SetupGet(value => value.InputStatuses).Returns(inputStatuses ?? ["OPEN"]);
        configurationHelper.SetupGet(value => value.InputType).Returns(inputType);
        configurationHelper.SetupGet(value => value.InputSeverities).Returns(inputSeverities ?? []);
        configurationHelper.SetupGet(value => value.InputImpactSoftwareQualities).Returns(inputImpactSoftwareQualities ?? []);
        configurationHelper.SetupGet(value => value.InputImpactSeverities).Returns(inputImpactSeverities ?? []);
        configurationHelper.SetupGet(value => value.InputCleanCodeAttributeCategories).Returns(inputCleanCodeAttributeCategories ?? []);
        configurationHelper.SetupGet(value => value.InputRules).Returns(inputRules ?? []);
        configurationHelper.SetupGet(value => value.InputIncludeRuleDetails).Returns(inputIncludeRuleDetails);
        configurationHelper.SetupGet(value => value.InputIncludeCodeSnippets).Returns(inputIncludeCodeSnippets);
        configurationHelper.SetupGet(value => value.InputCodeSnippetContextLines).Returns(inputCodeSnippetContextLines);
        configurationHelper.SetupGet(value => value.InputCopilotModel).Returns(inputCopilotModel);
        configurationHelper.SetupGet(value => value.InputCopilotProviderType).Returns(inputCopilotProviderType);
        configurationHelper.SetupGet(value => value.InputCopilotProviderBaseUrl).Returns(inputCopilotProviderBaseUrl);
        configurationHelper.SetupGet(value => value.InputCopilotOffline).Returns(inputCopilotOffline);
        configurationHelper.SetupGet(value => value.InputCopilotExtraInstructions).Returns(inputCopilotExtraInstructions);
        configurationHelper.SetupGet(value => value.InputBranchPrefix).Returns(inputBranchPrefix);
        configurationHelper.SetupGet(value => value.InputBaseBranch).Returns(inputBaseBranch);
        configurationHelper.SetupGet(value => value.InputPullRequestDraft).Returns(inputPullRequestDraft);
        configurationHelper.SetupGet(value => value.InputFailIfNoIssues).Returns(inputFailIfNoIssues);
        configurationHelper.SetupGet(value => value.InputCopilotAllowedTools).Returns(inputCopilotAllowedTools ?? []);
        configurationHelper.SetupGet(value => value.InputCopilotAllowAllTools).Returns(inputCopilotAllowAllTools);
        configurationHelper.SetupGet(value => value.SonarToken).Returns(sonarToken);
        configurationHelper.SetupGet(value => value.CopilotGitHubToken).Returns(copilotGitHubToken);
        configurationHelper.SetupGet(value => value.CopilotProviderApiKey).Returns(copilotProviderApiKey);
        configurationHelper.SetupGet(value => value.GhCliToken).Returns(ghCliToken);
        configurationHelper.SetupGet(value => value.GitHubWorkspace).Returns(gitHubWorkspace ?? Directory.GetCurrentDirectory());
        configurationHelper.SetupGet(value => value.GitHubRepository).Returns(gitHubRepository);
        configurationHelper.SetupGet(value => value.GitHubOutput).Returns(gitHubOutput);
        configurationHelper.SetupGet(value => value.GitHubStepSummary).Returns(gitHubStepSummary);
        configurationHelper
            .SetupGet(value => value.SafeEnvironmentVariables)
            .Returns(safeEnvironmentVariables ?? systemConfiguration.SafeEnvironmentVariables);
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
        null);

    public static ISonarQubeClient MockSonarQubeClient(IReadOnlyList<SonarIssue> issues)
    {
        Mock<ISonarQubeClient> client = new(MockBehavior.Strict);
        client
            .Setup(value => value.GetIssuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SonarIssueSearchResult(issues.Count, issues));
        client
            .Setup(value => value.EnrichIssues(It.IsAny<IReadOnlyList<SonarIssue>>()))
            .Returns((IReadOnlyList<SonarIssue> value) => value);
        client
            .Setup(value => value.GroupIssuesByRuleAsync(
                It.IsAny<IReadOnlyList<SonarIssue>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SonarIssue> value, CancellationToken _) => value
                .GroupBy(issue => issue.RuleKey, StringComparer.Ordinal)
                .Select(group => new IssueGroup(group.Key, group.ToArray()))
                .ToArray());
        return client.Object;
    }
}
