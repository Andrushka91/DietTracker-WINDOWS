using DietTracker.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>
/// The log file is what a user or a tester reads after a run (README, "Test the app"), so its format, its
/// level filter, its exception handling and its rotation are asserted here.
/// </summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "DietTrackerLogTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temporary folder must never fail a test run.
        }
    }

    [Fact]
    public void Messages_are_appended_with_timestamp_level_and_category()
    {
        using var provider = new FileLoggerProvider(_directory);
        var logger = provider.CreateLogger("DietTracker.Infrastructure.Database.DatabaseInitializer");

        logger.LogInformation("Database ready at '{Path}'.", @"C:\data\diettracker.db");

        var line = Assert.Single(File.ReadAllLines(provider.LogFilePath));
        Assert.Contains("[INF]", line);
        Assert.Contains("DietTracker.Infrastructure.Database.DatabaseInitializer:", line);
        Assert.Contains(@"Database ready at 'C:\data\diettracker.db'.", line);
    }

    [Fact]
    public void Messages_below_the_minimum_level_are_not_written()
    {
        using var provider = new FileLoggerProvider(_directory, LogLevel.Warning);
        var logger = provider.CreateLogger("DietTracker.Tests");

        logger.LogInformation("hidden");
        logger.LogWarning("visible");

        var line = Assert.Single(File.ReadAllLines(provider.LogFilePath));
        Assert.Contains("[WRN]", line);
        Assert.Contains("visible", line);
        Assert.DoesNotContain("hidden", line);
    }

    [Fact]
    public void An_exception_is_written_after_the_message()
    {
        using var provider = new FileLoggerProvider(_directory);
        var logger = provider.CreateLogger("DietTracker.Tests");

        logger.LogError(new InvalidOperationException("boom"), "The reminder cycle failed.");

        var text = File.ReadAllText(provider.LogFilePath);
        Assert.Contains("[ERR]", text);
        Assert.Contains("The reminder cycle failed.", text);
        Assert.Contains("InvalidOperationException: boom", text);
    }

    [Fact]
    public void The_file_is_rotated_once_when_it_reaches_the_size_cap()
    {
        using var provider = new FileLoggerProvider(_directory, LogLevel.Information, "diettracker.log", maximumBytes: 256);
        var logger = provider.CreateLogger("DietTracker.Tests");

        for (var index = 0; index < 20; index++)
        {
            logger.LogInformation("line {Index} written by the file logger test", index);
        }

        Assert.True(File.Exists(provider.LogFilePath));
        Assert.True(File.Exists(provider.LogFilePath + ".1"));

        var active = File.ReadAllText(provider.LogFilePath);
        var rotated = File.ReadAllText(provider.LogFilePath + ".1");

        // The newest messages are in the active file, the previous ones in the single rotated file.
        Assert.Contains("line 19 written by the file logger test", active);
        Assert.Contains("written by the file logger test", rotated);
        Assert.DoesNotContain("line 19 written by the file logger test", rotated);

        // One message may exceed the cap, but the active file never keeps growing beyond two of them.
        Assert.True(new FileInfo(provider.LogFilePath).Length <= 2 * 256);
    }
}
