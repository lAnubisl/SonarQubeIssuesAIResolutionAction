using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed partial class TextLoggerTests
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z \| info \| hello$")]
    private static partial Regex LogPattern();

    [Test]
    public static void TextLoggerFormat()
    {
        TextWriter originalOut = Console.Out;
        using StringWriter output = new();
        try
        {
            Console.SetOut(output);
            new TextLogger().Info("hello");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.True(LogPattern().IsMatch(output.ToString().Trim()));
    }
}
