namespace DietTracker.Core.Interfaces;

/// <summary>How the user wants to move between cloud providers / accounts.</summary>
public enum AccountSwitchStrategy
{
    /// <summary>Keep local data and upload it to the newly selected account.</summary>
    UploadLocalToNewAccount = 0,

    /// <summary>Keep local data and pull the remote dataset of the new account into this device.</summary>
    RestoreFromNewAccount = 1,

    /// <summary>Merge local and remote records with explicit conflict handling.</summary>
    Merge = 2,

    /// <summary>Cancel; nothing is changed.</summary>
    Cancel = 3,
}

/// <summary>Result of an account switch operation.</summary>
public sealed record AccountSwitchResult
{
    public required bool Succeeded { get; init; }

    public AccountSwitchStrategy Strategy { get; init; }

    public int RecordsUploaded { get; init; }

    public int RecordsDownloaded { get; init; }

    public int Conflicts { get; init; }

    public string? ReasonCode { get; init; }

    public string? Diagnostic { get; init; }
}

/// <summary>Information shown to the user before an account switch is executed.</summary>
public sealed record AccountSwitchPreview
{
    public Enums.ProviderType CurrentProvider { get; init; }

    public string CurrentAccountEmail { get; init; } = string.Empty;

    public Enums.ProviderType TargetProvider { get; init; }

    public string TargetAccountEmail { get; init; } = string.Empty;

    /// <summary>Number of local meal records that would need to be handled.</summary>
    public int LocalRecords { get; init; }

    /// <summary>Local changes that have not reached any cloud account yet.</summary>
    public int PendingChanges { get; init; }

    /// <summary>Records discovered in the target cloud account.</summary>
    public int CloudRecords { get; init; }

    public int CloudPhotos { get; init; }

    public bool HasLocalDataOnDevice => LocalRecords > 0;

    public bool CloudAccountHasData => CloudRecords > 0;

    /// <summary>Always true: switching providers always requires explicit confirmation.</summary>
    public bool RequiresConfirmation => true;
}

/// <summary>
/// Guards provider/account switching. Switching never merges, transfers or overwrites cloud data
/// without explicit user confirmation, and never treats a Microsoft and a Google account as the
/// same identity just because the e-mail addresses match.
/// </summary>
public interface IAccountSwitchCoordinator
{
    /// <summary>Builds the preview shown to the user (current account, target account, unsynced changes).</summary>
    Task<AccountSwitchPreview> PreviewAsync(Enums.ProviderType targetProvider, CancellationToken cancellationToken = default);

    /// <summary>Performs the confirmed switch.</summary>
    Task<AccountSwitchResult> ExecuteAsync(
        Enums.ProviderType targetProvider,
        AccountSwitchStrategy strategy,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
