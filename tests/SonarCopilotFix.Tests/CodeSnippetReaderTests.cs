using Moq;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;
using SonarCopilotFix.PromptGeneration;
using SonarCopilotFix.SonarQube.Models;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CodeSnippetReaderTests
{
    [Test]
    public static void SnippetExtraction()
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory();
        Directory.CreateDirectory(Path.Combine(temp.FullName, "src"));
        File.WriteAllLines(Path.Combine(temp.FullName, "src", "A.cs"), ["one", "two", "three", "four", "five"]);

        Mock<IConfigurationHelper> configurationHelper = TestData.MockConfigurationHelper(
            inputCodeSnippetContextLines: 1,
            gitHubWorkspace: temp.FullName);
        CodeSnippet snippet = new CodeSnippetReader(configurationHelper.Object).ReadSnippet("src/A.cs", 3);

        Assert.True(snippet.FileFound);
        Assert.Equal(2, snippet.StartLine);
        Assert.Contains("3: three", snippet.Content);
    }

    [Test]
    public static void AddSnippetsReturnsCopiesWithSnippetData()
    {
        using TempDirectory temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "A.cs"), "content");
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(gitHubWorkspace: temp.Path);
        SonarIssue issue = TestData.SampleIssue() with { FilePath = "A.cs", Line = 1 };

        IReadOnlyList<SonarIssue> result = new CodeSnippetReader(configuration.Object).AddSnippets([issue]);

        Assert.True(result.Single().CodeSnippet!.FileFound);
        Assert.False(ReferenceEquals(issue, result.Single()));
    }

    [TestCase("", "No file path was provided")]
    [TestCase("missing.cs", "File was not found")]
    [TestCase("../outside.cs", "outside the workspace")]
    public static void InvalidOrMissingPathsReturnDiagnostic(string relativePath, string expectedMessage)
    {
        using TempDirectory temp = new();
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(gitHubWorkspace: temp.Path);

        CodeSnippet snippet = new CodeSnippetReader(configuration.Object).ReadSnippet(relativePath, 1);

        Assert.False(snippet.FileFound);
        Assert.Contains(expectedMessage, snippet.Content);
    }

    [Test]
    public static void EmptyFileReturnsAnEmptyOneLineSnippet()
    {
        using TempDirectory temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "empty.cs"), "");
        Mock<IConfigurationHelper> configuration = TestData.MockConfigurationHelper(gitHubWorkspace: temp.Path);

        CodeSnippet snippet = new CodeSnippetReader(configuration.Object).ReadSnippet("empty.cs", 99);

        Assert.True(snippet.FileFound);
        Assert.Equal(1, snippet.StartLine);
        Assert.Equal(1, snippet.EndLine);
        Assert.Equal("", snippet.Content);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Path = Directory.CreateTempSubdirectory().FullName;
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
