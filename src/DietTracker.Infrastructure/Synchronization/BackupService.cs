using System.IO.Compression;
using System.Text.Json;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Core.Serialization;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Portable local backup, independent of any cloud provider.
/// </summary>
/// <remarks>
/// The archive is a ZIP file containing <c>backup.json</c> (all records in the versioned payload
/// format) plus a <c>photos/</c> folder with the original images. Restoring merges by identifier and
/// never deletes existing data, so a partial or failed restore cannot destroy history.
/// </remarks>
public sealed partial class BackupService : IBackupService
{
    private const string ManifestEntry = "backup.json";
    private const string PhotoFolder = "photos/";

    private readonly IMealEntryRepository _entries;
    private readonly IMealCategoryRepository _categories;
    private readonly IPhotoRepository _photos;
    private readonly IPhotoStore _photoStore;
    private readonly IMealTemplateRepository _templates;
    private readonly IReminderRepository _reminders;
    private readonly IProfileService _profiles;
    private readonly IAppDataPaths _paths;
    private readonly IClock _clock;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        IMealEntryRepository entries,
        IMealCategoryRepository categories,
        IPhotoRepository photos,
        IPhotoStore photoStore,
        IMealTemplateRepository templates,
        IReminderRepository reminders,
        IProfileService profiles,
        IAppDataPaths paths,
        IClock clock,
        ILogger<BackupService> logger)
    {
        _entries = entries;
        _categories = categories;
        _photos = photos;
        _photoStore = photoStore;
        _templates = templates;
        _reminders = reminders;
        _profiles = profiles;
        _paths = paths;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BackupSummary> ExportAsync(string targetFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);

        var range = await _entries
            .GetForRangeAsync(profile.Id, new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1), cancellationToken)
            .ConfigureAwait(false);

        var meals = range.Values.SelectMany(v => v).ToArray();
        var templates = await _templates.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var reminders = await _reminders.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var photos = new List<MealPhoto>();
        var photoCount = 0;

        await using (var stream = File.Create(targetFilePath))
        await using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var meal in meals)
            {
                var mealPhotos = await _photos.GetForMealAsync(meal.Id, cancellationToken).ConfigureAwait(false);
                photos.AddRange(mealPhotos);

                foreach (var photo in mealPhotos)
                {
                    var path = _photoStore.GetFullSizePath(photo.LocalFileId);
                    if (path is null || !File.Exists(path))
                    {
                        continue;
                    }

                    var photoEntry = archive.CreateEntry($"{PhotoFolder}{photo.Id:D}", CompressionLevel.NoCompression);
                    await using var photoStream = photoEntry.Open();
                    await using var source = File.OpenRead(path);
                    await source.CopyToAsync(photoStream, cancellationToken).ConfigureAwait(false);
                    photoCount++;
                }
            }

            var manifest = new BackupManifest
            {
                CreatedAt = _clock.UtcNow,
                DeviceId = Environment.MachineName,
                Meals = meals,
                Templates = templates,
                Reminders = reminders,
                Photos = photos,
            };

            var entry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await JsonSerializer.SerializeAsync(entryStream, manifest, DietTrackerJson.Indented, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Backup with {Meals} meals and {Photos} photographs written to '{Path}'.", meals.Length, photoCount, targetFilePath);

        return new BackupSummary
        {
            FilePath = targetFilePath,
            MealEntries = meals.Length,
            Photos = photoCount,
            Templates = templates.Count,
            Reminders = reminders.Count,
            CreatedAt = _clock.UtcNow,
        };
    }
}
