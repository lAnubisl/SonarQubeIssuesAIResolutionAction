using NUnit.Framework;

namespace SonarCopilotFix.Tests;

internal static class Assert
{
    public static void Equal<T>(T expected, T actual) =>
        NUnit.Framework.Assert.That(actual, Is.EqualTo(expected));

    public static void True(bool value) =>
        NUnit.Framework.Assert.That(value, Is.True);

    public static void False(bool value) =>
        NUnit.Framework.Assert.That(value, Is.False);

    public static void Contains(string expected, string actual) =>
        NUnit.Framework.Assert.That(actual, Does.Contain(expected));

    public static T Throws<T>(Action action) where T : Exception =>
        NUnit.Framework.Assert.Throws<T>(action)!;

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T exception)
        {
            return exception;
        }

        NUnit.Framework.Assert.Fail($"Expected exception of type {typeof(T).Name}.");
        throw new InvalidOperationException("NUnit assertion did not throw.");
    }
}
