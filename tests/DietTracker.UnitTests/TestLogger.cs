using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>
/// Minimal <see cref="ILogger{T}"/> used by the tests. Records the messages so that tests can assert
/// on diagnostics without pulling in a logging framework.
/// </summary>
public sealed class TestLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];
    private readonly ITestOutputHelper? _output;

    public TestLogger(ITestOutputHelper? output = null) => _output = output;

    /// <summary>All messages written so far (level prefixed).</summary>
    public IReadOnlyList<string> Messages => _messages;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var line = $"{logLevel}: {formatter(state, exception)}";
        _messages.Add(line);
        _output?.WriteLine(line);
    }
}

/// <summary>Test double for the user interface context (no window exists in a test run).</summary>
public sealed class TestUserInterfaceContext : global::DietTracker.Infrastructure.Authentication.IUserInterfaceContext
{
    public IntPtr GetWindowHandle() => IntPtr.Zero;
}
