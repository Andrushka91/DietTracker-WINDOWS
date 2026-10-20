using System.IO.Compression;
using System.Text.Json;
using DietTracker.Core.BusinessRules;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Core.Serialization;
using DietTracker.Core.Utilities;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Restore and safety-copy operations of <see cref="BackupService"/>.</summary>
public sealed partial class BackupService
{
    /// <inheritdoc />
    public async Task<BackupSummary> RestoreAsync(string sourceFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("The backup file does not exist.", sourceFilePath);
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);

        using var archive = ZipFile.OpenRead(sourceFilePath);
        var manifestEntry = archive.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException("The selected file is not a DietTracker backup (backup.json is missing).");

        BackupManifest manifest;
        await using (var stream = manifestEntry.Open())
        {
            manifest = await JsonSerializer
                .DeserializeAsync<BackupManifest>(stream, DietTrackerJson.Indented, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("The backup manifest could not be read.");
        }

        if (manifest.SchemaVersion > CloudSchema.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"The backup was created by a newer version of DietTracker (schema {manifest.SchemaVersion}).");
        }

        var photos = await RestorePhotosAsync(archive, manifest, cancellationToken).ConfigureAwait(false);
        var meals = await RestoreMealsAsync(manifest, profile, cancellationToken).ConfigureAwait(false);
        var templates = await RestoreTemplatesAsync(manifest, profile, cancellationToken).ConfigureAwait(false);
        var reminders = await RestoreRemindersAsync(manifest, profile, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Restored {Meals} meals, {Photos} photographs, {Templates} templates and {Reminders} reminders from '{Path}'.",
            meals, photos, templates, reminders, sourceFilePath);

        return new BackupSummary
        {
            FilePath = sourceFilePath,
            MealEntries = meals,
            Photos = photos,
            Templates = templates,
            Reminders = reminders,
            CreatedAt = manifest.CreatedAt,
        };
    }

    /// <inheritdoc />
    public async Task<string?> CreateSafetyCopyAsync(string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.Combine(_paths.BackupDirectory, "safety");
            Directory.CreateDirectory(directory);

            var fileName = $"diettracker-{Sanitize(reason)}-{_clock.UtcNow:yyyyMMddHHmmss}.dtbackup";
            var summary = await ExportAsync(Path.Combine(directory, fileName), cancellationToken).ConfigureAwait(false);
            return summary.FilePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A safety copy could not be created.");
            return null;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSafetyCopiesAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(_paths.BackupDirectory, "safety");
        if (!Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var files = Directory
            .EnumerateFiles(directory, "*.dtbackup")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    private static string Sanitize(string value)
        => new(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
}
