using DietTracker.Core.Interfaces;
using Microsoft.Win32;

namespace DietTracker.Infrastructure.Shell;

/// <summary>
/// Registers Diet Tracker as a Windows startup task.
/// </summary>
/// <remarks>
/// A packaged application uses the officially supported <c>StartupTask</c> API, which the user can
/// also manage from Task Manager. An unpackaged build (developer runs, tests) falls back to the
/// per-user <c>Run</c> key, and both paths are always expressed as an explicit user preference.
/// </remarks>
public sealed class StartupTaskService : IStartupTaskService
{
    private const string TaskId = "DietTrackerStartup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DietTracker";

    private readonly IAppEnvironment _environment;
    private readonly ILogger<StartupTaskService> _logger;

    public StartupTaskService(IAppEnvironment environment, ILogger<StartupTaskService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (_environment.IsPackaged)
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId).AsTask(cancellationToken).ConfigureAwait(false);
                return task.State is Windows.ApplicationModel.StartupTaskState.Enabled
                    or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            catch (Exception ex)
            {
                _logger.LogInformation("The packaged startup task could not be read ({Reason}).", ex.GetType().Name);
            }
        }

        return IsRunKeyEnabled();
    }

    /// <inheritdoc />
    public async Task<bool> TrySetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (_environment.IsPackaged)
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId).AsTask(cancellationToken).ConfigureAwait(false);

                if (enabled)
                {
                    var state = await task.RequestEnableAsync().AsTask(cancellationToken).ConfigureAwait(false);
                    return state is Windows.ApplicationModel.StartupTaskState.Enabled
                        or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
                }

                task.Disable();
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The packaged startup task could not be changed; falling back to the Run key.");
            }
        }

        return TrySetRunKey(enabled);
    }

    private static bool IsRunKeyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is string value && !string.IsNullOrEmpty(value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool TrySetRunKey(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executable))
                {
                    return false;
                }

                key.SetValue(RunValueName, $"\"{executable}\" --minimized");
                return true;
            }

            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The startup entry could not be written.");
            return false;
        }
    }
}
