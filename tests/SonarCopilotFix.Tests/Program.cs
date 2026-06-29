using System.Net;
using System.Text;
using SonarCopilotFix;
using SonarCopilotFix.Git;
using SonarCopilotFix.GitHub;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube;

var tests = new (string Name, Func<Task> Run)[]
{
    ("SonarQube API pagination respects max_issues", Tests.Pagination),
    ("SonarQube authentication errors are mapped clearly", Tests.AuthenticationError),
    ("Malformed SonarQube responses fail", Tests.MalformedResponse),
    ("Issue filtering parameters are sent", Tests.Filtering),
    ("Code snippets are extracted around the issue line", Tests.SnippetExtraction),
    ("Prompt generation includes safety rules and issue details", Tests.PromptGeneration),
    ("PR body contains issue links and delegates validation to PR checks", Tests.PrBody),
    ("Job summary contains Copilot usage", Tests.JobSummaryUsage),
    ("Dry-run input does not require Copilot or GitHub tokens", Tests.DryRunInputValidation),
    ("Dry-run app behavior writes prompt without creating a branch", Tests.DryRunAppBehavior),
    ("App logs fetched SonarQube issue count and details", Tests.FetchedIssueLogging),
    ("Normal mode requires isolated Copilot and GitHub tokens", Tests.NormalModeTokenValidation),
    ("Copilot CLI uses the supported programmatic interface", Tests.CopilotCliArguments),
    ("TextLogger writes pipe-delimited timestamps with one-second precision", Tests.TextLoggerFormat),
    ("CommandRunner forwards process output while it runs", Tests.CommandOutputForwarding),
    ("CommandRunner logs full command details and output when enabled", Tests.CommandDetailLogging),
    ("CommandRunner safe environment excludes unrelated secrets", Tests.TokenIsolationEnvironment),
    ("GitService detects changed files with command-scoped safe directory", Tests.GitChangedFiles),
    ("GitHub CLI passes command-scoped safe directory to child Git processes", Tests.GitHubCliEnvironment)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failures == 0 ? 0 : 1;

internal static class Tests
{
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

    public static async Task AuthenticationError()
    {
        var client = NewClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var ex = await Assert.ThrowsAsync<ControlledFailureException>(() => client.GetIssuesAsync(CancellationToken.None));
        Assert.Contains("Invalid or missing SonarQube token", ex.Message);
    }

    public static async Task MalformedResponse()
    {
        var client = NewClient(new FakeHandler(_ => Json("{")));
        var ex = await Assert.ThrowsAsync<ControlledFailureException>(() => client.GetIssuesAsync(CancellationToken.None));
        Assert.Contains("malformed JSON", ex.Message);
    }

    public static async Task Filtering()
    {
        var handler = new FakeHandler(_ => Json("""{"total":0,"issues":[]}"""));
        var client = NewClient(handler, statuses: "OPEN,CONFIRMED", severities: "CRITICAL", cleanCode: "INTENTIONAL");
        await client.GetIssuesAsync(CancellationToken.None);
        var uri = handler.Requests.Single().RequestUri!;
        Assert.Equal("OPEN,CONFIRMED", Query(uri, "statuses"));
        Assert.Equal("CRITICAL", Query(uri, "severities"));
        Assert.Equal("INTENTIONAL", Query(uri, "cleanCodeAttributeCategories"));
    }

    public static Task SnippetExtraction()
    {
        var temp = Directory.CreateTempSubdirectory();
        Directory.CreateDirectory(Path.Combine(temp.FullName, "src"));
        File.WriteAllLines(Path.Combine(temp.FullName, "src", "A.cs"), ["one", "two", "three", "four", "five"]);
        var snippet = new CodeSnippetReader().ReadSnippet(temp.FullName, "src/A.cs", 3, 1);
        Assert.True(snippet.FileFound);
        Assert.Equal(2, snippet.StartLine);
        Assert.Contains("3: three", snippet.Content);
        return Task.CompletedTask;
    }

    public static Task PromptGeneration()
    {
        var issue = SampleIssue() with { CodeSnippet = new CodeSnippet("src/A.cs", true, 1, 1, "    1: code") };
        var prompt = new PromptBuilder().Build(Options(), [issue], "feature", "main");
        Assert.Contains("Fix only the listed SonarQube issues", prompt);
        Assert.Contains("ISSUE-1", prompt);
        Assert.Contains("src/A.cs", prompt);
        return Task.CompletedTask;
    }

    public static Task PrBody()
    {
        var summary = new JobSummary(Options())
        {
            BaseBranch = "main",
            GeneratedBranch = "copilot/sonar/proj/20260101000000",
            ChangedFiles = ["src/A.cs"],
            CopilotUsageReport = "Tokens    ↑ 29.3k • ↓ 219 • 1.5k (cached) • 400 (written) • 92 (reasoning)\nAI Credits 8.3"
        };
        var body = new PrBodyBuilder().Build(Options(), [SampleIssue()], summary);
        Assert.Contains("Human review is required", body);
        Assert.Contains("ISSUE-1", body);
        Assert.Contains("src/A.cs", body);
        Assert.Contains("Validation is delegated to the repository's pull request checks", body);
        Assert.Contains("Copilot CLI `/usage`", body);
        Assert.Contains("29.3k", body);
        Assert.Contains("8.3", body);
        return Task.CompletedTask;
    }

    public static Task JobSummaryUsage()
    {
        var temp = Directory.CreateTempSubdirectory();
        var path = Path.Combine(temp.FullName, "summary.md");
        var summary = new JobSummary(Options())
        {
            CopilotExecuted = true,
            CopilotUsageReport = "Tokens    ↑ 1k • ↓ 200 • 700 (cached) • 50 (reasoning)\nAI Credits 1.25"
        };

        summary.Write(new DictionaryEnvironment(new Dictionary<string, string?>
        {
            ["GITHUB_STEP_SUMMARY"] = path
        }));
        var contents = File.ReadAllText(path);

        Assert.Contains("Copilot CLI `/usage`", contents);
        Assert.Contains("↑ 1k", contents);
        Assert.Contains("1.25", contents);
        return Task.CompletedTask;
    }

    public static Task DryRunInputValidation()
    {
        var env = new DictionaryEnvironment(new Dictionary<string, string?>
        {
            ["INPUT_SONAR_HOST_URL"] = "https://sonar.example",
            ["INPUT_SONAR_PROJECT_KEY"] = "proj",
            ["INPUT_DRY_RUN"] = "true",
            ["SONAR_TOKEN"] = "sonar"
        });
        var options = ActionInputs.FromEnvironment(env);
        Assert.True(options.DryRun);
        return Task.CompletedTask;
    }

    public static async Task DryRunAppBehavior()
    {
        var temp = Directory.CreateTempSubdirectory();
        var commandRunner = new CommandRunner(new TextLogger());
        var gitInit = await commandRunner.RunAsync("git", ["init"], temp.FullName);
        Assert.Equal(0, gitInit.ExitCode);
        var env = new DictionaryEnvironment(new Dictionary<string, string?>
        {
            ["INPUT_SONAR_HOST_URL"] = "https://sonar.example",
            ["INPUT_SONAR_PROJECT_KEY"] = "proj",
            ["INPUT_DRY_RUN"] = "true",
            ["SONAR_TOKEN"] = "sonar",
            ["GITHUB_WORKSPACE"] = temp.FullName
        });
        var options = ActionInputs.FromEnvironment(env);
        var app = new SonarCopilotFixApp(
            options,
            env,
            new TextLogger(),
            new FakeSonarQubeClient([SampleIssue()]),
            new CodeSnippetReader(),
            new PromptBuilder(),
            commandRunner,
            new PrBodyBuilder());

        var exitCode = await app.RunAsync();
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(temp.FullName, ".sonar-copilot", "issues-prompt.md")));
        Assert.False(Directory.Exists(Path.Combine(temp.FullName, ".git", "refs", "heads", "copilot")));
    }

    public static async Task FetchedIssueLogging()
    {
        var temp = Directory.CreateTempSubdirectory();
        var logger = new TextLogger();
        var commandRunner = new CommandRunner(logger);
        var gitInit = await commandRunner.RunAsync("git", ["init"], temp.FullName);
        Assert.Equal(0, gitInit.ExitCode);
        var env = new DictionaryEnvironment(new Dictionary<string, string?>
        {
            ["INPUT_SONAR_HOST_URL"] = "https://sonar.example",
            ["INPUT_SONAR_PROJECT_KEY"] = "proj",
            ["INPUT_DRY_RUN"] = "true",
            ["SONAR_TOKEN"] = "sonar",
            ["GITHUB_WORKSPACE"] = temp.FullName
        });
        var options = ActionInputs.FromEnvironment(env);
        var app = new SonarCopilotFixApp(
            options,
            env,
            logger,
            new FakeSonarQubeClient([SampleIssue()]),
            new CodeSnippetReader(),
            new PromptBuilder(),
            commandRunner,
            new PrBodyBuilder());

        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            await app.RunAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("Fetched 1 SonarQube issue(s) (1 total matching issue(s) reported by SonarQube).", output.ToString());
        Assert.Contains("Fetched SonarQube issue: key=ISSUE-1, severity=MAJOR, title=Fix this", output.ToString());
    }

    public static Task NormalModeTokenValidation()
    {
        var env = new DictionaryEnvironment(new Dictionary<string, string?>
        {
            ["INPUT_SONAR_HOST_URL"] = "https://sonar.example",
            ["INPUT_SONAR_PROJECT_KEY"] = "proj",
            ["SONAR_TOKEN"] = "sonar"
        });
        var ex = Assert.Throws<ControlledFailureException>(() => ActionInputs.FromEnvironment(env));
        Assert.Contains("COPILOT_CLI_TOKEN", ex.Message);
        return Task.CompletedTask;
    }

    public static Task TokenIsolationEnvironment()
    {
        Environment.SetEnvironmentVariable("SONAR_TOKEN", "sonar-secret");
        var safe = CommandRunner.BuildSafeEnvironment(new Dictionary<string, string?> { ["GH_TOKEN"] = "github-secret" });
        Assert.True(safe.ContainsKey("GH_TOKEN"));
        Assert.False(safe.ContainsKey("SONAR_TOKEN"));
        Assert.False(safe.ContainsKey("COPILOT_CLI_TOKEN"));
        return Task.CompletedTask;
    }

    public static Task CopilotCliArguments()
    {
        var restricted = CopilotCliRunner.BuildArguments(
            Options() with { CopilotModel = "gpt-5.2" },
            "Fix the selected issue.");
        Assert.SequenceEqual(
            ["--prompt", "Fix the selected issue.", "--no-ask-user", "--no-color", "--model", "gpt-5.2", "--allow-tool=write"],
            restricted);

        var unrestricted = CopilotCliRunner.BuildArguments(
            Options() with { CopilotAllowAllTools = true },
            "Fix it.");
        Assert.True(unrestricted.Contains("--allow-all-tools"));
        Assert.False(unrestricted.Contains("--allow-tool=write"));
        Assert.SequenceEqual(
            ["--session-id", "session-id", "--prompt", "/usage", "--no-ask-user", "--no-color"],
            CopilotCliRunner.BuildUsageArguments("session-id"));
        return Task.CompletedTask;
    }

    public static Task TextLoggerFormat()
    {
        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            new TextLogger().Info("hello");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(
            output.ToString().Trim(),
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z \| info \| hello$"));
        return Task.CompletedTask;
    }

    public static async Task CommandOutputForwarding()
    {
        var received = new List<string>();
        var commandRunner = new CommandRunner(new TextLogger());
        var result = await commandRunner.RunAsync(
            "dotnet",
            ["--version"],
            Directory.GetCurrentDirectory(),
            cancellationToken: CancellationToken.None,
            standardOutputReceived: line => received.Add(line));

        Assert.Equal(0, result.ExitCode);
        Assert.True(received.Count > 0);
        Assert.Equal(result.StandardOutput.Trim(), string.Join(Environment.NewLine, received));
    }

    public static async Task CommandDetailLogging()
    {
        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var commandRunner = new CommandRunner(new TextLogger());
            var result = await commandRunner.RunAsync(
                "dotnet",
                ["--version"],
                Directory.GetCurrentDirectory(),
                cancellationToken: CancellationToken.None,
                logCommandDetails: true);

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("Starting command: dotnet --version", output.ToString());
        Assert.Contains("[dotnet stdout]", output.ToString());
        Assert.Contains("[dotnet stderr]", output.ToString());
        Assert.Contains("exited with code 0.", output.ToString());
    }

    public static async Task GitChangedFiles()
    {
        var temp = Directory.CreateTempSubdirectory();
        var commandRunner = new CommandRunner(new TextLogger());
        Assert.Equal(0, (await commandRunner.RunAsync("git", ["init"], temp.FullName)).ExitCode);
        var trackedFile = Path.Combine(temp.FullName, "HostFilmMonitoring.cs");
        await File.WriteAllTextAsync(trackedFile, "original");
        Assert.Equal(0, (await commandRunner.RunAsync("git", ["add", "--", "HostFilmMonitoring.cs"], temp.FullName)).ExitCode);
        Assert.Equal(
            0,
            (await commandRunner.RunAsync(
                "git",
                ["-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-m", "initial"],
                temp.FullName)).ExitCode);

        await File.WriteAllTextAsync(trackedFile, "changed");
        await File.WriteAllTextAsync(Path.Combine(temp.FullName, "untracked.txt"), "new");

        var git = new GitService(commandRunner, temp.FullName);
        var changedFiles = await git.GetChangedFilesAsync(excludeGenerated: true, CancellationToken.None);

        Assert.Equal(2, changedFiles.Count);
        Assert.SequenceEqual(["HostFilmMonitoring.cs", "untracked.txt"], changedFiles);
    }

    public static Task GitHubCliEnvironment()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "github-workspace");
        var environment = GitHubCliService.BuildEnvironment("github-secret", workspace);

        Assert.Equal("github-secret", environment["GH_TOKEN"]);
        Assert.Equal("1", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("safe.directory", environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal(Path.GetFullPath(workspace), environment["GIT_CONFIG_VALUE_0"]);
        return Task.CompletedTask;
    }

    private static SonarQubeClient NewClient(FakeHandler handler, int maxIssues = 10, string? statuses = null, string? severities = null, string? cleanCode = null)
    {
        var env = new DictionaryEnvironment(new Dictionary<string, string?>
        {
            ["INPUT_SONAR_HOST_URL"] = "https://sonar.example",
            ["INPUT_SONAR_PROJECT_KEY"] = "proj",
            ["INPUT_MAX_ISSUES"] = maxIssues.ToString(),
            ["INPUT_ISSUE_STATUSES"] = statuses,
            ["INPUT_SEVERITIES"] = severities,
            ["INPUT_CLEAN_CODE_ATTRIBUTE_CATEGORIES"] = cleanCode,
            ["INPUT_DRY_RUN"] = "true",
            ["INPUT_INCLUDE_RULE_DETAILS"] = "false",
            ["SONAR_TOKEN"] = "sonar"
        });
        return new SonarQubeClient(ActionInputs.FromEnvironment(env), new TextLogger(), new HttpClient(handler), disposeClient: true);
    }

    private static ActionInputs Options() => ActionInputs.FromEnvironment(new DictionaryEnvironment(new Dictionary<string, string?>
    {
        ["INPUT_SONAR_HOST_URL"] = "https://sonar.example",
        ["INPUT_SONAR_PROJECT_KEY"] = "proj",
        ["INPUT_DRY_RUN"] = "true",
        ["SONAR_TOKEN"] = "sonar",
        ["GITHUB_REPOSITORY"] = "owner/repo"
    }));

    private static SonarIssue SampleIssue() => new(
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

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

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

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}

internal sealed class DictionaryEnvironment(IReadOnlyDictionary<string, string?> values) : IEnvironment
{
    public string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;
}

internal sealed class FakeSonarQubeClient(IReadOnlyList<SonarIssue> issues) : ISonarQubeClient
{
    public Task<SonarIssueSearchResult> GetIssuesAsync(CancellationToken cancellationToken) => Task.FromResult(new SonarIssueSearchResult(issues.Count, issues));
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }

    public static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    public static void False(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected text to contain '{expected}'.");
        }
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected exception {typeof(T).Name}.");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected exception {typeof(T).Name}.");
    }
}
