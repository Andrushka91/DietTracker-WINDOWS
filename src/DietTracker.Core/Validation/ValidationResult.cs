namespace DietTracker.Core.Validation;

/// <summary>A single validation failure. <see cref="Code"/> is stable and localizable by the UI.</summary>
/// <param name="Code">Stable reason code, for example "meal.title.required".</param>
/// <param name="Field">Name of the offending field.</param>
/// <param name="Message">Developer friendly message (English) used in logs and tests.</param>
public sealed record ValidationFailure(string Code, string Field, string Message);

/// <summary>Result of validating a domain object before it is persisted.</summary>
public sealed class ValidationResult
{
    private static readonly ValidationResult SuccessInstance = new([]);

    private ValidationResult(IReadOnlyList<ValidationFailure> failures) => Failures = failures;

    public IReadOnlyList<ValidationFailure> Failures { get; }

    public bool IsValid => Failures.Count == 0;

    public static ValidationResult Success() => SuccessInstance;

    public static ValidationResult Failure(params ValidationFailure[] failures) => new(failures);

    public static ValidationResult Failure(IEnumerable<ValidationFailure> failures) => new(failures.ToArray());

    /// <summary>Combines several results, keeping every failure.</summary>
    public static ValidationResult Combine(params ValidationResult[] results)
        => new(results.SelectMany(r => r.Failures).ToArray());

    public string ToLogString() => string.Join("; ", Failures.Select(f => $"{f.Code} ({f.Field})"));
}
