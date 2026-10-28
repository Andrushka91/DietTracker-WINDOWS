namespace DietTracker.Core.Interfaces;

/// <summary>The account transition a cloud-only lifecycle operation belongs to.</summary>
public enum AccountTransition
{
    /// <summary>The user is about to connect a cloud account.</summary>
    SignIn = 0,

    /// <summary>The user leaves the account; the local cache is emptied.</summary>
    SignOut = 1,

    /// <summary>The application started and has to make sure no cache of an earlier session survived.</summary>
    Startup = 2,
}

/// <summary>
/// Stage of a cloud-only transition. The interface reports it so the busy overlay can explain what is
/// happening instead of showing an anonymous spinner for several seconds.
/// </summary>
public enum AccountTransitionStage
{
    /// <summary>Pending changes of the connected account are being uploaded.</summary>
    Uploading = 0,

    /// <summary>The local cache is being emptied (records, photographs, journal and cursor).</summary>
    ClearingLocalData = 1,

    /// <summary>The dataset of the connected account is being downloaded and applied.</summary>
    Downloading = 2,

    /// <summary>The transition finished.</summary>
    Completed = 3,
}

/// <summary>Outcome of a cloud-only lifecycle operation.</summary>
public sealed record AccountTransitionResult
{
    /// <summary>Transition this result belongs to.</summary>
    public required AccountTransition Transition { get; init; }

    /// <summary>True when the local cache (records, photographs, journal, cursor) was removed.</summary>
    public bool LocalDataCleared { get; init; }

    /// <summary>
    /// True when local records that have never been uploaded were adopted by the account that was just
    /// connected (the first sign-in of an installation works offline for as long as the user likes).
    /// </summary>
    public bool LocalDataAdopted { get; init; }

    /// <summary>True when the account dataset was downloaded into the (empty) local cache.</summary>
    public bool CloudDataLoaded { get; init; }

    /// <summary>Records the provider confirmed during the upload that preceded the transition.</summary>
    public int Uploaded { get; init; }

    /// <summary>Records that were downloaded from the account.</summary>
    public int Downloaded { get; init; }

    /// <summary>Changes that were still waiting for the cloud when the transition stopped.</summary>
    public int PendingChanges { get; init; }

    /// <summary>Stable, provider independent reason code of a refused transition.</summary>
    public string? FailureReason { get; init; }

    /// <summary>True when the transition ran to completion.</summary>
    public bool Succeeded => FailureReason is null;

    /// <summary>
    /// The transition was refused because changes would have been lost: they could not be uploaded
    /// (offline provider, uploads switched off) and deleting them would contradict the policy that the
    /// cloud copy is the only copy.
    /// </summary>
    public const string PendingChangesReason = "session.pending-changes";

    /// <summary>No cloud account is connected, so there is nothing to prepare, download or upload.</summary>
    public const string NotConnectedReason = "session.not-connected";
}

/// <summary>
/// Cloud-only data lifecycle. The local database is a cache of exactly one connected account:
/// nothing of it may survive the session, and switching accounts must never show the records of the
/// previous one.
/// </summary>
/// <remarks>
/// <para>
/// The policy is deliberately independent of the synchronization engine: the engine keeps working
/// offline during a session, while this service decides when the cache starts and ends.
/// </para>
/// <para>
/// The order of the operations matters. Credentials are replaced by an interactive sign-in, so the
/// pending changes of the previous account are uploaded before the sign-in starts. Local records are
/// only deleted after the provider confirmed the upload, and the dataset of the new account is
/// downloaded immediately afterwards, so the user does not have to press a restore button.
/// </para>
/// </remarks>
public interface ICloudDataSession
{
    /// <summary>
    /// Runs before an interactive sign-in: uploads everything the current account still has pending and
    /// refuses the transition while a change could not be uploaded. The cache is deliberately not
    /// touched here - a cancelled sign-in must not cost the user the data of the account that is still
    /// connected. When no account has ever been used, the local records are kept and reported as
    /// adopted by the account that is about to sign in.
    /// </summary>
    Task<AccountTransitionResult> PrepareForSignInAsync(
        IProgress<AccountTransitionStage>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs after a successful sign-in: adopts the local records of a first sign-in (they are uploaded),
    /// or empties the cache and rebuilds it from the connected account. Either way only the signed-in
    /// account's data is visible immediately afterwards.
    /// </summary>
    Task<AccountTransitionResult> CompleteSignInAsync(
        bool adoptLocalData,
        IProgress<AccountTransitionStage>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs out and removes the local cache. Changes that are still pending make the operation refuse
    /// unless <paramref name="discardPendingChanges"/> is set - the cloud copy is the only copy.
    /// </summary>
    Task<AccountTransitionResult> SignOutAsync(
        bool discardPendingChanges = false,
        IProgress<AccountTransitionStage>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Start-up guard: removes a cache that survived without a connected account (a crash between the
    /// sign-out and the deletion, for example). Returns <see langword="null"/> when there is nothing to do.
    /// </summary>
    Task<AccountTransitionResult?> EnforceOnStartupAsync(CancellationToken cancellationToken = default);
}
