using System.Text.Json;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Core.Serialization;

namespace DietTracker.Infrastructure.Cloud;

/// <summary>
/// Serializes entities into the versioned cloud payload format used by both providers.
/// </summary>
/// <remarks>
/// Layout inside the application folder:
/// <code>
///   entities/{EntityType}/{entityId}.json   one document per record (envelope + payload)
///   photos/{photoId}.bin                    photograph bytes (never inside SQLite)
///   manifest.json                           dataset description (schema version, device, counts)
/// </code>
/// Per-record documents keep uploads incremental: only changed records are transferred, and two
/// devices never have to exchange the whole history.
/// </remarks>
public sealed class CloudPayloadSerializer : ICloudPayloadSerializer
{
    /// <inheritdoc />
    public string ManifestPath => "manifest.json";

    /// <inheritdoc />
    public string GetObjectPath(EntityType entityType, Guid entityId) => $"entities/{entityType}/{entityId:D}.json";

    /// <inheritdoc />
    public string GetPhotoObjectPath(Guid photoId) => $"photos/{photoId:D}.bin";

    /// <inheritdoc />
    public string Serialize<T>(CloudEnvelope<T> envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, DietTrackerJson.Indented);
    }

    /// <inheritdoc />
    public CloudEnvelope<T>? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CloudEnvelope<T>>(json, DietTrackerJson.Compact);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public (int SchemaVersion, EntityType EntityType, Guid EntityId, long Revision, DateTimeOffset UpdatedAt, string DeviceId, bool IsDeleted, string ContentHash)? ReadHeader(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("entityId", out var entityIdElement) || !Guid.TryParse(entityIdElement.GetString(), out var entityId))
            {
                return null;
            }

            var entityType = EntityType.MealEntry;
            if (root.TryGetProperty("entityType", out var entityTypeElement))
            {
                if (entityTypeElement.ValueKind == JsonValueKind.Number && entityTypeElement.TryGetInt32(out var numeric))
                {
                    entityType = (EntityType)numeric;
                }
                else if (Enum.TryParse<EntityType>(entityTypeElement.GetString(), ignoreCase: true, out var parsed))
                {
                    entityType = parsed;
                }
            }

            var schemaVersion = root.TryGetProperty("schemaVersion", out var schema) && schema.TryGetInt32(out var schemaValue)
                ? schemaValue
                : CloudSchema.CurrentVersion;

            var revision = root.TryGetProperty("revision", out var revisionElement) && revisionElement.TryGetInt64(out var revisionValue)
                ? revisionValue
                : 0L;

            var updatedAt = root.TryGetProperty("updatedAt", out var updatedElement)
                           && DateTimeOffset.TryParse(updatedElement.GetString(), out var updatedValue)
                ? updatedValue
                : DateTimeOffset.MinValue;

            var deviceId = root.TryGetProperty("deviceId", out var deviceElement) ? deviceElement.GetString() ?? string.Empty : string.Empty;
            var isDeleted = root.TryGetProperty("isDeleted", out var deletedElement) && deletedElement.ValueKind == JsonValueKind.True;
            var contentHash = root.TryGetProperty("contentHash", out var hashElement) ? hashElement.GetString() ?? string.Empty : string.Empty;

            return (schemaVersion, entityType, entityId, revision, updatedAt, deviceId, isDeleted, contentHash);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
