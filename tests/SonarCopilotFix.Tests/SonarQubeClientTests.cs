using System.Net;
using System.Text;
using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.SonarQube;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class SonarQubeClientTests
{
    [Test]
    public static async Task Pagination()
    {
        var handler = new FakeHandler(request =>
        {
            var page = Query(request.RequestUri!, "p");
            var json = page == "1"
                ? """{"total":3,"issues":[{"key":"A","rule":"csharpsquid:S1","component":"proj:src/A.cs","line":1,"message":"one"},{"key":"B","rule":"csharpsquid:S2","component":"proj:src/B.cs","line":2,"message":"two"}]}"""
                : """{"total":3,"issues":[{"key":"C","rule":"csharpsquid:S3","component":"proj:src/C.cs","line":3,"message":"three"}]}""";
            return Json(json);
        });
        var client = NewClient(handler, maxIssues: 3);

        var result = await client.GetIssuesAsync(CancellationToken.None);

        Assert.Equal(3, result.Issues.Count);
        Assert.Equal(2, handler.Requests.Count(request => request.RequestUri!.AbsolutePath == "/api/issues/search"));
    }

    [Test]
    public static async Task AuthenticationError()
    {
        var client = NewClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var ex = await Assert.ThrowsAsync<ControlledFailureException>(() => client.GetIssuesAsync(CancellationToken.None));

        Assert.Contains("Invalid or missing SonarQube token", ex.Message);
    }

    [Test]
    public static async Task MalformedResponse()
    {
        var client = NewClient(new FakeHandler(_ => Json("{")));

        var ex = await Assert.ThrowsAsync<ControlledFailureException>(() => client.GetIssuesAsync(CancellationToken.None));

        Assert.Contains("malformed JSON", ex.Message);
    }

    [Test]
    public static async Task Filtering()
    {
        var handler = new FakeHandler(_ => Json("""{"total":0,"issues":[]}"""));
        var client = NewClient(
            handler,
            statuses: "OPEN,CONFIRMED",
            severities: "CRITICAL",
            impactSoftwareQualities: "RELIABILITY,SECURITY",
            impactSeverities: "HIGH",
            cleanCodeAttributeCategories: "INTENTIONAL",
            rules: "csharpsquid:S1234,csharpsquid:S5678",
            components: "proj:src/A.cs,proj:src/B.cs");

        await client.GetIssuesAsync(CancellationToken.None);

        var uri = handler.Requests.Single().RequestUri!;
        Assert.Equal("proj:src/A.cs,proj:src/B.cs", Query(uri, "components"));
        Assert.Equal("OPEN,CONFIRMED", Query(uri, "statuses"));
        Assert.Equal("CRITICAL", Query(uri, "severities"));
        Assert.Equal("RELIABILITY,SECURITY", Query(uri, "impactSoftwareQualities"));
        Assert.Equal("HIGH", Query(uri, "impactSeverities"));
        Assert.Equal("INTENTIONAL", Query(uri, "cleanCodeAttributeCategories"));
        Assert.Equal("csharpsquid:S1234,csharpsquid:S5678", Query(uri, "rules"));
    }

    [Test]
    public static async Task IssueSearchLogging()
    {
        const string responseBody = """{"total":0,"issues":[]}""";
        var logger = TestData.MockLogger();
        var client = NewClient(new FakeHandler(_ => Json(responseBody)), logger: logger.Object);

        await client.GetIssuesAsync(CancellationToken.None);

        logger.Verify(
            value => value.Info("SonarQube issue search request URL: https://sonar.example/api/issues/search?components=proj&p=1&ps=10&statuses=OPEN"),
            Times.Once);
        logger.Verify(
            value => value.Info($"SonarQube issue search response body: {responseBody}"),
            Times.Once);
    }

    [Test]
    public static async Task IssueResponseMapping()
    {
        const string responseBody = """
            {
              "total": 1,
              "issues": [{
                "key": "AZ8BZ2rc-1jWpY_LduWr",
                "rule": "external_roslyn:NUnit2045",
                "severity": "INFO",
                "component": "lAnubisl_LostFilmTorrentsFeed:LostFilmMonitoring.BLL.Tests/Commands/GetUserCommandTests.cs",
                "project": "lAnubisl_LostFilmTorrentsFeed",
                "hash": "fa48cd0d9a81b24cc78b6ab0b8efd12a",
                "textRange": { "startLine": 55, "endLine": 55, "startOffset": 8, "endOffset": 48 },
                "flows": [],
                "status": "OPEN",
                "message": "Call independent Assert statements from inside an Assert.EnterMultipleScope or Assert.Multiple",
                "effort": "0min",
                "debt": "0min",
                "tags": [],
                "creationDate": "2026-06-26T00:49:23+0000",
                "updateDate": "2026-06-29T14:28:23+0000",
                "type": "CODE_SMELL",
                "organization": "lanubisl",
                "externalRuleEngine": "roslyn",
                "cleanCodeAttribute": "CONVENTIONAL",
                "cleanCodeAttributeCategory": "CONSISTENT",
                "impacts": [{ "softwareQuality": "MAINTAINABILITY", "severity": "MEDIUM" }],
                "issueStatus": "OPEN",
                "projectName": "LostFilmTorrentsFeed",
                "internalTags": [],
                "lastChangeAnalysisUuid": "95ebd727-7dc2-4654-afca-d36ab6b23bed",
                "lastChangeSource": "ANALYSIS"
              }]
            }
            """;
        var client = NewClient(new FakeHandler(_ => Json(responseBody)));

        var result = await client.GetIssuesAsync(CancellationToken.None);
        var issue = result.Issues.Single();

        Assert.Equal("lAnubisl_LostFilmTorrentsFeed", issue.Project);
        Assert.Equal("fa48cd0d9a81b24cc78b6ab0b8efd12a", issue.Hash);
        Assert.Equal(55, issue.Line);
        Assert.Equal("CONVENTIONAL", issue.CleanCodeAttribute);
        Assert.Equal("CONSISTENT", issue.CleanCodeAttributeCategory);
        var impact = issue.Impacts!.Single();
        Assert.Equal("MAINTAINABILITY", impact.SoftwareQuality);
        Assert.Equal("MEDIUM", impact.Severity);
        Assert.Equal("OPEN", issue.IssueStatus);
        Assert.Equal("LostFilmTorrentsFeed", issue.ProjectName);
        Assert.Equal("roslyn", issue.ExternalRuleEngine);
        Assert.Equal("ANALYSIS", issue.LastChangeSource);
        Assert.Equal(new DateTimeOffset(2026, 6, 26, 0, 49, 23, TimeSpan.Zero), issue.CreationDate);
    }

    private static SonarQubeClient NewClient(
        FakeHandler handler,
        int maxIssues = 10,
        string? statuses = null,
        string? severities = null,
        string? impactSoftwareQualities = null,
        string? impactSeverities = null,
        string? cleanCodeAttributeCategories = null,
        string? rules = null,
        string? components = null,
        ILogger? logger = null)
    {
        var configurationHelper = TestData.MockConfigurationHelper(
            inputComponents: Csv(components),
            inputMaxIssues: maxIssues,
            inputStatuses: Csv(statuses, "OPEN"),
            inputSeverities: Csv(severities),
            inputImpactSoftwareQualities: Csv(impactSoftwareQualities),
            inputImpactSeverities: Csv(impactSeverities),
            inputCleanCodeAttributeCategories: Csv(cleanCodeAttributeCategories),
            inputRules: Csv(rules),
            inputIncludeRuleDetails: false);
        return new SonarQubeClient(
            configurationHelper.Object,
            logger ?? TestData.MockLogger().Object,
            new HttpClient(handler),
            disposeClient: true);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static IReadOnlyList<string> Csv(string? value, string? fallback = null) =>
        (value ?? fallback)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static string? Query(Uri uri, string name)
    {
        var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var split = pair.Split('=', 2);
            if (Uri.UnescapeDataString(split[0]) == name)
            {
                return split.Length == 2 ? Uri.UnescapeDataString(split[1]) : "";
            }
        }

        return null;
    }
}
