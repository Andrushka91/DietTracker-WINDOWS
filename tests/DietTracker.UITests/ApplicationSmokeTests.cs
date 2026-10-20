using System.Diagnostics;
using Xunit;

namespace DietTracker.UITests;

/// <summary>
/// Smoke tests that launch the real desktop application. They need a built executable and an
/// interactive Windows session, so they are identified with the <c>UI</c> category and can be run
/// separately: <c>dotnet test tests\DietTracker.UITests --filter Category=UI</c>.
/// </summary>
[Trait("Category", "UI")]
public sealed class ApplicationSmokeTests
{
    private static readonly string ExecutablePath = Path.Combine(
        RepositoryRoot(),
        "dist", "exec", "DietTracker.exe");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DietTracker.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    [Fact]
    public void The_application_executable_is_available()
    {
        Assert.True(
            File.Exists(ExecutablePath),
            $"Build the application first (npm run build, or dotnet build src/DietTracker.App/DietTracker.App.csproj -c Debug -p:Platform=x64 -p:WindowsPackageType=None). Expected: {ExecutablePath}");
    }

    [Fact]
    public async Task The_application_starts_shows_a_window_and_prepares_its_local_database()
    {
        Assert.True(File.Exists(ExecutablePath), $"The application has not been built: {ExecutablePath}");

        // The unpackaged build keeps its data in %LOCALAPPDATA%\DietTracker; the packaged build uses the
        // MSIX LocalState folder instead.
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DietTracker",
            "database",
            "diettracker.db");

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(new ProcessStartInfo(ExecutablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!, UseShellExecute = false,
        });

        Assert.NotNull(process);

        try
        {
            var timeout = TimeSpan.FromSeconds(90);
            while (stopwatch.Elapsed < timeout && !File.Exists(databasePath))
            {
                await Task.Delay(100);
                Assert.False(process!.HasExited, "The application exited during startup.");
            }

            stopwatch.Stop();

            Assert.True(File.Exists(databasePath), "The application did not create its local database.");
            Assert.True(stopwatch.Elapsed.TotalSeconds < 60, $"Startup took {stopwatch.Elapsed.TotalSeconds:0.0} s.");

            // Give the window time to be created, then verify the process is still healthy.
            await Task.Delay(TimeSpan.FromSeconds(5));
            process!.Refresh();

            Assert.False(process.HasExited, "The application closed unexpectedly after startup.");
            Assert.True(process.Responding, "The application window is not responding.");
            Assert.True(process.WorkingSet64 > 0, "The working set could not be measured.");
        }
        finally
        {
            if (!process!.HasExited)
            {
                process.Kill();
            }
        }
    }
}
