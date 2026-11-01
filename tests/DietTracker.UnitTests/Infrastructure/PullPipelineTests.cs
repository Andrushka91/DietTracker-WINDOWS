using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>
/// Speed of the pull phase: the documents of a synchronization cycle are downloaded through a bounded
/// parallel window (OrderedPrefetcher) while they are applied in order, and the cycle reports real
/// counts ("12 of 40") so the busy overlay can paint a determinate bar.
/// </summary>
/// <remarks>
/// Before this change the pull was strictly sequential - one round trip per document, one document at
/// a time - which is what made a first sign-in of a full account crawl from 0 to 100.
/// </remarks>
public sealed class PullPipelineTests
{
    private static readonly DateOnly TestDate = new(2026, 11, 1);

    /// <summary>Collects the progress frames a cycle reports (thread safe: they arrive off the test thread).</summary>
    private sealed class RecordingProgress : IProgress<RestoreProgress>
    {
        private readonly List<RestoreProgress> _frames = [];
        private readonly object _gate = new();

        public IReadOnlyList<RestoreProgress> Frames
        {
            get
            {
                lock (_gate)
                {
                    return [.. _frames];
                }
            }
        }

        public void Report(RestoreProgress value)
        {
            lock (_gate)
            {
                _frames.Add(value);
            }
        }
    }

    private static ICloudPayloadSerializer Serializer(TestEnvironment environment)
        => environment.Services.GetRequiredService<ICloudPayloadSerializer>();

    /// <summary>One meal document of another installation, as the mobile/desktop app publishes it.</summary>
    private static CloudEnvelope<MealEntry> RemoteMeal(Guid id, MealCategoryKey key, string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new CloudEnvelope<MealEntry>
        {
            EntityType = EntityType.MealEntry,
            EntityId = id,
            LocalProfileId = Guid.NewGuid(),
            Revision = 1,
            UpdatedAt = now,
            DeviceId = "the-other-device",
            ContentHash = $"hash-{id:N}",
            Payload = new MealEntry
            {
                Id = id,
                LocalProfileId = Guid.NewGuid(),
                Date = TestDate,
                MealCategoryId = Guid.NewGuid(),
                MealCategoryKey = key,
                Title = title,
                Ingredients = "from the other device",
                Revision = 1,
                Status = MealStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                SyncStatus = SyncStatus.PendingUpload,
            },
        };
    }


    [Fact]
    public async Task The_pull_downloads_documents_in_a_bounded_parallel_window()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var serializer = Serializer(environment);

        // The provider streams the payloads instead of returning them inline, so the read path - and
        // therefore the window - is actually exercised.
        environment.Cloud.SendChangesInline = false;
        environment.Cloud.DownloadDelay = TimeSpan.FromMilliseconds(15);

        var keys = new[]
        {
            MealCategoryKey.Breakfast,
            MealCategoryKey.MorningSnack,
            MealCategoryKey.Lunch,
            MealCategoryKey.AfternoonSnack,
            MealCategoryKey.Dinner,
        };
        var ids = new List<Guid>();
        foreach (var key in keys)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            environment.Cloud.WriteFromAnotherDevice(
                serializer.GetObjectPath(EntityType.MealEntry, id),
                serializer.Serialize(RemoteMeal(id, key, $"Remote {key}")));
        }

        var result = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        Assert.True(result.Succeeded);
        Assert.Equal(ids.Count, result.Downloaded);

        // Overlapped (no longer one round trip per document) and bounded (not one request per record).
        Assert.True(
            environment.Cloud.MaxConcurrentDownloads > 1,
            $"the pull read {environment.Cloud.MaxConcurrentDownloads} document(s) at a time");
        Assert.True(
            environment.Cloud.MaxConcurrentDownloads <= 8,
            $"the pull opened {environment.Cloud.MaxConcurrentDownloads} requests at once");

        // Complete: every document of the other device is local now.
        foreach (var id in ids)
        {
            Assert.NotNull(await environment.Entries.GetAsync(id));
        }
    }

    [Fact]
    public async Task The_cycle_reports_how_many_documents_it_settled()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var serializer = Serializer(environment);
        var progress = new RecordingProgress();

        var remoteId = Guid.NewGuid();
        environment.Cloud.WriteFromAnotherDevice(
            serializer.GetObjectPath(EntityType.MealEntry, remoteId),
            serializer.Serialize(RemoteMeal(remoteId, MealCategoryKey.Lunch, "Remote lunch")));

        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual, progress);

        var downloadFrames = progress.Frames
            .Where(frame => string.Equals(frame.Stage, "download", StringComparison.Ordinal))
            .ToArray();

        // "0 of 1" first, then the document itself: measured, not a spinner.
        Assert.NotEmpty(downloadFrames);
        Assert.Equal(0, downloadFrames[0].Processed);
        Assert.Equal(1, downloadFrames[0].Total);
        Assert.Equal(1, downloadFrames[^1].Processed);
        Assert.Equal(1, downloadFrames[^1].Total);
        Assert.Equal(100d, downloadFrames[^1].Percentage, 3);
    }
}
