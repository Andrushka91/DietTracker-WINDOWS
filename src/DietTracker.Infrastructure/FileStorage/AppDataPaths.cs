using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.FileStorage;

/// <summary>
/// Resolves every location Diet Tracker is allowed to write to.
/// </summary>
/// <remarks>
/// In a packaged (MSIX) build the root is the application's private <c>LocalState</c> folder, which
/// Windows preserves across Microsoft Store updates and removes on uninstall. In an unpackaged
/// build (developer runs, unit tests) <c>%LOCALAPPDATA%\DietTracker</c> is used instead. User data is
/// never written next to the executable, so updates cannot erase it.
/// </remarks>
public sealed class AppDataPaths : IAppDataPaths
{
    private const string AppFolderName = "DietTracker";

    public AppDataPaths(string? rootOverride = null)
    {
        RootDirectory = rootOverride ?? ResolveDefaultRoot();
        DatabaseDirectory = Path.Combine(RootDirectory, "database");
        PhotoDirectory = Path.Combine(RootDirectory, "photos");
        ThumbnailDirectory = Path.Combine(RootDirectory, "photos", "thumbnails");
        BackupDirectory = Path.Combine(RootDirectory, "backups");
        LogDirectory = Path.Combine(RootDirectory, "logs");
        SecureDirectory = Path.Combine(RootDirectory, "secure");
        DatabaseFilePath = Path.Combine(DatabaseDirectory, "diettracker.db");
    }

    public string RootDirectory { get; }

    public string DatabaseDirectory { get; }

    public string PhotoDirectory { get; }

    public string ThumbnailDirectory { get; }

    public string BackupDirectory { get; }

    public string LogDirectory { get; }

    public string SecureDirectory { get; }

    public string DatabaseFilePath { get; }

    public void EnsureCreated()
    {
        foreach (var directory in new[]
                 {
                     RootDirectory, DatabaseDirectory, PhotoDirectory, ThumbnailDirectory,
                     BackupDirectory, LogDirectory, SecureDirectory,
                 })
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string ResolveDefaultRoot()
    {
        // Packaged: ApplicationData.Current.LocalFolder is the MSIX LocalState folder.
        try
        {
            var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            if (!string.IsNullOrEmpty(localFolder?.Path))
            {
                return Path.Combine(localFolder.Path, AppFolderName);
            }
        }
        catch (Exception)
        {
            // Unpackaged process (or ApplicationData unavailable): fall through to LOCALAPPDATA.
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppFolderName);
    }
}
