using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Infrastructure.Security;
using DietTracker.Infrastructure.Synchronization;
using DietTracker.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>Conflict resolution policy: provider revisions and content hashes, never silent data loss.</summary>
public sealed class ConflictResolverTests
{
    private static IConflictResolver CreateResolver(TestEnvironment environment)
        => environment.Services.GetRequiredService<IConflictResolver>();

    [Fact]
    public async Task Identical_content_is_not_a_conflict()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var resolver = CreateResolver(environment);

        var decision = resolver.Decide(new ConflictContext
        {
            EntityType = EntityType.MealEntry,
            EntityId = Guid.NewGuid(),
            LocalContentHash = "hash-1",
            RemoteContentHash = "hash-1",
        });

        Assert.Equal(ConflictDecision.NoChange, decision);
    }

    [Fact]
    public async Task An_echo_of_our_own_upload_is_accepted()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var resolver = CreateResolver(environment);

        var decision = resolver.Decide(new ConflictContext
        {
            EntityType = EntityType.MealEntry,
            EntityId = Guid.NewGuid(),
            LocalContentHash = "local",
            RemoteContentHash = "remote",
            RemoteDeviceId = "device-a",
            LocalDeviceId = "device-a",
        });

        Assert.Equal(ConflictDecision.AcceptRemote, decision);
    }

    [Fact]
    public async Task An_unchanged_cloud_revision_keeps_the_local_version()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var resolver = CreateResolver(environment);

        var decision = resolver.Decide(new ConflictContext
        {
            EntityType = EntityType.MealEntry,
            EntityId = Guid.NewGuid(),
            LocalRevision = 4,
            RemoteRevision = 4,
            LocalContentHash = "local",
            RemoteContentHash = "remote",
            LocalBaseRevision = "rev-9",
            RemoteRevisionToken = "rev-9",
        });

        Assert.Equal(ConflictDecision.KeepLocal, decision);
    }

    [Fact]
    public async Task A_strictly_newer_remote_revision_wins()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var resolver = CreateResolver(environment);

        var decision = resolver.Decide(new ConflictContext
        {
            EntityType = EntityType.MealEntry,
            EntityId = Guid.NewGuid(),
            LocalRevision = 2,
            RemoteRevision = 5,
            LocalContentHash = "local",
            RemoteContentHash = "remote",
            LocalBaseRevision = "rev-1",
            RemoteRevisionToken = "rev-3",
        });

        Assert.Equal(ConflictDecision.AcceptRemote, decision);
    }

    [Fact]
    public async Task Concurrent_edits_without_revision_information_require_a_user_decision()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var resolver = CreateResolver(environment);

        var decision = resolver.Decide(new ConflictContext
        {
            EntityType = EntityType.MealEntry,
            EntityId = Guid.NewGuid(),
            LocalRevision = 3,
            RemoteRevision = 3,
            LocalContentHash = "local",
            RemoteContentHash = "remote",
            LocalBaseRevision = null,
            RemoteRevisionToken = null,
        });

        Assert.Equal(ConflictDecision.RequiresUserDecision, decision);
    }

    [Fact]
    public async Task Both_versions_are_preserved_when_a_conflict_is_recorded()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var resolver = CreateResolver(environment);

        var conflict = await resolver.RecordAsync(
            EntityType.MealEntry,
            Guid.NewGuid(),
            "{\"local\":true}",
            "{\"remote\":true}",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow,
            "rev-7",
            "device-b");

        var stored = await environment.Services.GetRequiredService<IConflictRepository>().GetOpenAsync();

        Assert.Single(stored);
        Assert.Equal(conflict.Id, stored[0].Id);
        Assert.Contains("local", stored[0].LocalPayload);
        Assert.Contains("remote", stored[0].RemotePayload);
    }
}

public sealed class PathSanitizerTests
{
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void A_file_name_can_never_escape_its_directory(string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "diettracker-tests", "photos");
        Assert.Null(PathSanitizer.CombineInsideDirectory(directory, fileName));
    }

    [Fact]
    public void A_legitimate_file_name_stays_inside_the_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "diettracker-tests", "photos");
        var combined = PathSanitizer.CombineInsideDirectory(directory, "abc123.jpg");

        Assert.NotNull(combined);
        Assert.StartsWith(directory, combined!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Opaque_file_names_keep_the_extension_and_are_unique()
    {
        var first = PathSanitizer.CreateOpaqueFileName(".JPG");
        var second = PathSanitizer.CreateOpaqueFileName(".JPG");

        Assert.EndsWith(".jpg", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Unsafe_characters_are_replaced()
    {
        var safe = PathSanitizer.ToSafeFileName("google:token/../x");
        Assert.DoesNotContain('/', safe);
        Assert.DoesNotContain(':', safe);
    }

    [Fact]
    public void Only_supported_image_extensions_are_accepted()
    {
        Assert.True(PathSanitizer.HasAllowedImageExtension("a.JPG"));
        Assert.True(PathSanitizer.HasAllowedImageExtension("a.webp"));
        Assert.False(PathSanitizer.HasAllowedImageExtension("a.exe"));
    }
}

/// <summary>Localization completeness: the three shipped languages must expose the same keys.</summary>
public sealed class ResourceParityTests
{
    private static readonly string[] Languages = ["ro-RO", "en-US", "es-ES"];

    private static IReadOnlyList<string> ReadKeys(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", language, "Resources.resw");
        Assert.True(File.Exists(path), $"Missing resource file: {path}");

        var document = System.Xml.Linq.XDocument.Load(path);
        return document.Root!
            .Elements("data")
            .Select(element => element.Attribute("name")!.Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void All_languages_declare_exactly_the_same_keys()
    {
        var reference = ReadKeys("ro-RO");

        foreach (var language in Languages.Skip(1))
        {
            var keys = ReadKeys(language);
            Assert.Equal(reference, keys);
        }
    }

    [Theory]
    [InlineData("Meal_Breakfast_Name")]
    [InlineData("Meal_MorningSnack_Name")]
    [InlineData("Meal_Lunch_Name")]
    [InlineData("Meal_AfternoonSnack_Name")]
    [InlineData("Meal_Dinner_Name")]
    [InlineData("Nav_Dashboard")]
    [InlineData("Sync_Offline")]
    [InlineData("Account_ConsentText")]
    [InlineData("Account_CloudSaveHint")]
    [InlineData("Settings_PrivacyHint")]
    [InlineData("Report_Title")]
    public void Required_keys_exist_in_every_language(string key)
    {
        foreach (var language in Languages)
        {
            Assert.Contains(key, ReadKeys(language));
        }
    }

    [Fact]
    public void No_language_contains_an_empty_value()
    {
        foreach (var language in Languages)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", language, "Resources.resw");
            var document = System.Xml.Linq.XDocument.Load(path);

            foreach (var element in document.Root!.Elements("data"))
            {
                Assert.False(string.IsNullOrWhiteSpace(element.Element("value")?.Value), $"Empty value for {element.Attribute("name")?.Value} in {language}");
            }
        }
    }
}
