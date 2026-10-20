using DietTracker.Core.Interfaces;

namespace DietTracker.App.Services;

/// <summary>
/// Maps stable domain reason codes to resource keys. Domain codes use dots (for example
/// <c>meal.title.tooLong</c>) while resource identifiers cannot, so every code is converted in one
/// place instead of being re-typed at each call site.
/// </summary>
public static class LocalizationKeys
{
    /// <summary>Resource key of an authentication or provider reason code (prefix <c>Auth_</c>).</summary>
    public static string ForReason(string? reasonCode) => "Auth_" + Sanitize(reasonCode);

    /// <summary>Resource key of a validation failure code (prefix <c>Error_</c>).</summary>
    public static string ForValidation(string? failureCode) => "Error_" + Sanitize(failureCode);

    /// <summary>Resource key of a synchronization reason code (prefix <c>Sync_</c>).</summary>
    public static string ForSync(string? reasonCode) => "Sync_" + Sanitize(reasonCode);

    private static string Sanitize(string? value)
        => string.IsNullOrEmpty(value)
            ? "Unknown"
            : new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
