using NUnit.Framework;
using SonarCopilotFix.PromptGeneration;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CodeSnippetReaderTests
{
    [Test]
    public static void SnippetExtraction()
    {
        var temp = Directory.CreateTempSubdirectory();
        Directory.CreateDirectory(Path.Combine(temp.FullName, "src"));
        File.WriteAllLines(Path.Combine(temp.FullName, "src", "A.cs"), ["one", "two", "three", "four", "five"]);

        var snippet = new CodeSnippetReader().ReadSnippet(temp.FullName, "src/A.cs", 3, 1);

        Assert.True(snippet.FileFound);
        Assert.Equal(2, snippet.StartLine);
        Assert.Contains("3: three", snippet.Content);
    }
}
