namespace DietTracker.Core.Enums;

/// <summary>
/// How often the application synchronizes with the cloud on its own. Local edits are always saved
/// immediately and journaled; the frequency only decides when that journal is pushed and pulled.
/// </summary>
public enum SyncFrequency
{
    /// <summary>Never automatically - only the explicit "Synchronize now" action runs a cycle.</summary>
    Manual = 0,

    /// <summary>Once every seven days (the default).</summary>
    Weekly = 1,

    /// <summary>Once every thirty days.</summary>
    Monthly = 2,
}