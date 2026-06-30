using NUnit.Framework;

namespace SonarCopilotFix.Tests;

internal static class CollectionAssert
{
    public static void AreEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        NUnit.Framework.Assert.That(actual, Is.EqualTo(expected));
}
