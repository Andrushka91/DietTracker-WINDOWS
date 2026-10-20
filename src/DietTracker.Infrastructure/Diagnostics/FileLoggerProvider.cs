using System.Text;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.Diagnostics;

/// <summary>
/// Appends the application log to a plain text file inside the private application data folder.
/// </summary>
/// <remarks>
/// The built-in Debug provider is only visible to a debugger, so a text file is what a user or a tester
/// can read after a run (see the "Test the app" section of the README). The file is capped and rotated
/// once - <c>diettracker.log</c> and <c>diettracker.log.1</c> - so it cannot grow without limit. Every
/// message is appended with its own short lived file handle, which means the file stays readable with any
/// tool while the application is running. Messages carry technical detail only; tokens, authorization
/// codes and personal data are never logged (see <c>docs/privacy/privacy-policy.md</c>). Every write
/// failure is swallowed on purpose: logging must never be able to break the application.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const string DefaultFileName = "diettracker.log";
    private const long DefaultMaximumBytes = 1024 * 1024;

    private static readonly Encoding LogEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _filePath;
    private readonly long _maximumBytes;
    private readonly object _gate = new();

    private bool _unavailable;

    /// <summary>Creates a provider that writes into <paramref name="logDirectory"/>.</summary>
    /// <param name="logDirectory">Folder that receives the log file.</param>
    /// <param name="minimumLevel">Lowest level that is written to the file.</param>
    /// <param name="fileName">Name of the log file.</param>
    /// <param name="maximumBytes">Size after which the file is rotated to <c>*.1</c>.</param>
    public FileLoggerProvider(
        string logDirectory,
        LogLevel minimumLevel = LogLevel.Information,
        string fileName = DefaultFileName,
        long maximumBytes = DefaultMaximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        LogDirectory = logDirectory;
        LogFilePath = Path.Combine(logDirectory, fileName);
        MinimumLevel = minimumLevel;
        _filePath = LogFilePath;
        _maximumBytes = maximumBytes > 0 ? maximumBytes : DefaultMaximumBytes;
    }

    /// <summary>Folder that receives the log file.</summary>
    public string LogDirectory { get; }

    /// <summary>Full path of the log file that is appended to.</summary>
    public string LogFilePath { get; }

    /// <summary>Lowest level that is written to the file.</summary>
    public LogLevel MinimumLevel { get; }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to release: every message is written and flushed by its own short lived file handle, so
        // the log can be opened with any viewer while the application keeps running.
    }

    /// <summary>Writes one message (and the exception, when there is one) to the log file.</summary>
    internal void Write(LogLevel level, string? category, string message, Exception? exception)
    {
        var line = new StringBuilder(message.Length + 128)
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(" [")
            .Append(Abbreviate(level))
            .Append("] ");

        if (!string.IsNullOrEmpty(category))
        {
            line.Append(category).Append(": ");
        }

        line.Append(message);

        if (exception is not null)
        {
            line.Append(Environment.NewLine).Append(exception);
        }

        lock (_gate)
        {
            if (_unavailable)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(LogDirectory);

                // Rotate first, then append with a short lived handle (no lock is held between messages).
                var existing = new FileInfo(_filePath);
                if (existing.Exists && existing.Length >= _maximumBytes)
                {
                    Rotate();
                }

                File.AppendAllText(_filePath, line.ToString() + Environment.NewLine, LogEncoding);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // A read-only disk or a file locked by another tool must not break the application.
                _unavailable = true;
            }
        }
    }

    private void Rotate()
    {
        try
        {
            File.Move(_filePath, _filePath + ".1", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The previous log stays in place (for example it is open in a viewer); appending continues.
        }
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "---",
    };

    /// <summary>Logger handed out for one category name; filters by the provider's minimum level.</summary>
    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= _provider.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }
}
