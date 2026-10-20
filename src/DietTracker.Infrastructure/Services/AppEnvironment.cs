using System.Reflection;
using System.Runtime.InteropServices;
using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.Services;

/// <summary>Build, packaging and device information used by the About and diagnostics screens.</summary>
public sealed class AppEnvironment : IAppEnvironment
{
    private readonly Lazy<string> _deviceId;

    /// <summary>
    /// Creates the environment description. The device identifier is stored inside the application data
    /// directory, so a second installation (another PC, or an isolated test environment) always gets its
    /// own identifier and never mistakes another device's uploads for its own.
    /// </summary>
    public AppEnvironment(IAppDataPaths paths, string? deviceIdOverride = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _deviceId = new Lazy<string>(() => deviceIdOverride ?? DeviceIdProvider.GetOrCreate(paths));
    }

    public string ApplicationVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0.0";

    public string PackageFamilyName
    {
        get
        {
            try
            {
                return Windows.ApplicationModel.Package.Current.Id.FamilyName;
            }
            catch (Exception)
            {
                // Unpackaged process: there is no package identity.
                return string.Empty;
            }
        }
    }

    public bool IsPackaged => !string.IsNullOrEmpty(PackageFamilyName);

    /// <summary>
    /// Stable identifier of this installation. It is not a hardware identifier: it only lets the
    /// synchronization engine recognize echoes of its own uploads and label conflicts.
    /// </summary>
    public string DeviceId => _deviceId.Value;

    public string OperatingSystemDescription =>
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
}

/// <summary>Reads (or creates once) the stable device identifier stored next to the local data.</summary>
public static class DeviceIdProvider
{
    private const string FileName = "device.id";

    /// <summary>Returns the device identifier, creating it on first use.</summary>
    public static string GetOrCreate(IAppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureCreated();

        var path = Path.Combine(paths.RootDirectory, FileName);
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(existing))
                {
                    return existing;
                }
            }

            var created = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, created);
            return created;
        }
        catch (IOException)
        {
            // Read-only location: fall back to a per-session identifier instead of failing startup.
            return Guid.NewGuid().ToString("N");
        }
    }
}
