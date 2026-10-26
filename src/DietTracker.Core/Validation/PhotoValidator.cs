using DietTracker.Core.BusinessRules;

namespace DietTracker.Core.Validation;

/// <summary>Validates a photograph before it is copied into the local photo store.</summary>
public static class PhotoValidator
{
    /// <summary>
    /// Validates extension, declared size and the file signature.
    /// </summary>
    /// <param name="fileName">Original file name (only the extension is used).</param>
    /// <param name="sizeBytes">Length of the file in bytes.</param>
    /// <param name="header">At least the first 12 bytes of the file.</param>
    public static ValidationResult Validate(string fileName, long sizeBytes, ReadOnlySpan<byte> header)
    {
        var failures = new List<ValidationFailure>();
        var extension = Path.GetExtension(fileName) ?? string.Empty;
        var mime = PhotoRules.MimeTypeFromExtension(extension);

        if (mime is null)
        {
            failures.Add(new ValidationFailure("photo.format.unsupported", nameof(fileName), "Only JPEG, PNG and WEBP images are supported."));
            return ValidationResult.Failure(failures);
        }

        if (sizeBytes <= 0)
        {
            failures.Add(new ValidationFailure("photo.empty", nameof(sizeBytes), "The selected file is empty."));
        }
        else if (sizeBytes > PhotoRules.MaxPhotoBytes)
        {
            failures.Add(new ValidationFailure("photo.tooLarge", nameof(sizeBytes), "The selected file exceeds the 10 MB limit."));
        }

        if (!PhotoRules.HasValidSignature(mime, header))
        {
            failures.Add(new ValidationFailure("photo.content.invalid", nameof(header), "The file content does not match its image format."));
        }

        return failures.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(failures);
    }

    /// <summary>Validates that another photograph may be attached to a meal that already has some.</summary>
    public static ValidationResult ValidateSlotAvailability(int currentPhotoCount)
        => PhotoRules.CanAddPhoto(currentPhotoCount)
            ? ValidationResult.Success()
            : ValidationResult.Failure(new ValidationFailure(
                "photo.limit.reached",
                nameof(currentPhotoCount),
                $"A meal can hold at most {PhotoRules.MaxPhotosPerMeal} photographs."));

    /// <summary>
    /// Validates that the bytes being attached are not already attached to the same meal. Identical
    /// photographs are never stored twice: the second upload is rejected so the user can be told the
    /// photo is already there instead of watching a thumbnail appear and disappear.
    /// </summary>
    /// <param name="alreadyAttached">True when a photograph with the same SHA-256 exists for this meal.</param>
    public static ValidationResult ValidateNoDuplicate(bool alreadyAttached)
        => alreadyAttached
            ? ValidationResult.Failure(new ValidationFailure(
                "photo.duplicate",
                nameof(alreadyAttached),
                "The same photograph is already attached to this meal."))
            : ValidationResult.Success();
}
