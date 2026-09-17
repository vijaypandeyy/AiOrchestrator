namespace AiOrchestrator.Tests.Testing;

/// <summary>
/// This solution has zero external NuGet dependencies (see the technical document's
/// "why no xUnit/NUnit" note - restoring one was not possible in this build environment,
/// and the design goal of a dependency-free build was worth keeping regardless). This is a
/// deliberately tiny stand-in for [Fact]/Assert: enough to express and run real unit tests
/// with a pass/fail summary and a non-zero exit code on failure, wired into `dotnet run`.
/// Swap in xUnit/NUnit later by adding the PackageReference and replacing this attribute +
/// Assert class - the test bodies below would barely need to change.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute
{
}

public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message)
    {
    }
}

public static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new AssertionException(message ?? "Expected condition to be true.");
        }
    }

    public static void False(bool condition, string? message = null) =>
        True(!condition, message ?? "Expected condition to be false.");

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException(message ?? $"Expected '{expected}' but got '{actual}'.");
        }
    }

    public static void NotNull(object? value, string? message = null) =>
        True(value is not null, message ?? "Expected a non-null value.");

    public static void Contains(string expectedSubstring, string actual, string? message = null) =>
        True(actual.Contains(expectedSubstring, StringComparison.Ordinal),
            message ?? $"Expected '{actual}' to contain '{expectedSubstring}'.");
}
