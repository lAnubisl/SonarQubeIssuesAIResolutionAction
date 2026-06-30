using System.Text.RegularExpressions;
using NUnit.Framework;
using SonarCopilotFix.Infrastructure;

namespace SonarCopilotFix.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class TextLoggerTests
{
    [Test]
    public static void TextLoggerFormat()
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

        Assert.True(Regex.IsMatch(
            output.ToString().Trim(),
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z \| info \| hello$"));
    }
}
