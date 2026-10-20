using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>Application preferences that follow the user across devices.</summary>
public sealed class AppPreferences
{
    public Guid LocalProfileId { get; set; }

    /// <summary>BCP-47 language tag chosen in settings, or empty to follow the Windows UI language.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>IANA time zone (empty = follow Windows).</summary>
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>Show the two snack slots (the underlying data is never removed).</summary>
    public bool ShowSnackCards { get; set; } = true;

    /// <summary>Start DietTracker when the user signs in to Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Keep running in the notification area when the window is closed.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Master switch for reminders.</summary>
    public bool RemindersEnabled { get; set; } = true;

    /// <summary>Use the 12-hour clock in the UI.</summary>
    public bool Use12HourClock { get; set; }

    /// <summary>Master switch for cloud synchronization.</summary>
    public bool SynchronizationEnabled { get; set; }

    /// <summary>Last provider selected by the user (mirrors the active cloud account).</summary>
    public ProviderType PreferredProvider { get; set; } = ProviderType.None;

    /// <summary>Set once the user explicitly consented to uploading personal data to the cloud.</summary>
    public bool CloudUploadConsentGranted { get; set; }

    /// <summary>True once the first-launch welcome screen has been completed.</summary>
    public bool OnboardingCompleted { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendingUpload;
}
