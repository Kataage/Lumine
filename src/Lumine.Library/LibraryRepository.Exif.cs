using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LibraryRepository
{
    public async Task<AssetExifMetadata?> GetExifMetadataAsync(
        long libraryId,
        long assetId,
        long expectedSourceRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedSourceRevision);

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                em.source_revision,
                em.camera_model,
                em.lens_model,
                em.focal_length,
                em.aperture,
                em.shutter_speed,
                em.iso,
                em.captured_at,
                em.gps_latitude,
                em.gps_longitude
            FROM assets AS a
            INNER JOIN asset_exif_metadata AS em
              ON em.asset_id = a.id
             AND em.source_revision = a.source_revision
            WHERE a.library_id = $library_id
              AND a.id = $asset_id
              AND a.source_revision = $source_revision;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue(
            "$source_revision",
            expectedSourceRevision);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        return new AssetExifMetadata(
            assetId,
            reader.GetInt64(0),
            ReadOptionalString(reader, 1),
            ReadOptionalString(reader, 2),
            ReadOptionalString(reader, 3),
            ReadOptionalString(reader, 4),
            ReadOptionalString(reader, 5),
            reader.IsDBNull(6)
                ? null
                : reader.GetInt32(6),
            ReadOptionalString(reader, 7),
            ReadOptionalString(reader, 8),
            ReadOptionalString(reader, 9));
    }

    public async Task<bool> UpsertExifMetadataAsync(
        long libraryId,
        long assetId,
        long expectedSourceRevision,
        long expectedFileSize,
        long expectedModifiedAtUtcTicks,
        AssetExifMetadataUpdate metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedSourceRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedFileSize);
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.Iso is < 0 or > 10_000_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metadata),
                "EXIF ISO must be between 0 and 10,000,000 when present.");
        }

        var cameraModel =
            NormalizeOptional(metadata.CameraModel, 512, nameof(metadata.CameraModel));
        var lensModel =
            NormalizeOptional(metadata.LensModel, 512, nameof(metadata.LensModel));
        var focalLength =
            NormalizeOptional(metadata.FocalLength, 128, nameof(metadata.FocalLength));
        var aperture =
            NormalizeOptional(metadata.Aperture, 128, nameof(metadata.Aperture));
        var shutterSpeed =
            NormalizeOptional(metadata.ShutterSpeed, 128, nameof(metadata.ShutterSpeed));
        var capturedAt =
            NormalizeOptional(metadata.CapturedAt, 128, nameof(metadata.CapturedAt));
        var gpsLatitude =
            NormalizeOptional(metadata.GpsLatitude, 256, nameof(metadata.GpsLatitude));
        var gpsLongitude =
            NormalizeOptional(metadata.GpsLongitude, 256, nameof(metadata.GpsLongitude));
        var nowTicks =
            DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO asset_exif_metadata(
                asset_id,
                source_revision,
                camera_model,
                lens_model,
                focal_length,
                aperture,
                shutter_speed,
                iso,
                captured_at,
                gps_latitude,
                gps_longitude,
                probed_at_utc_ticks)
            SELECT
                a.id,
                a.source_revision,
                $camera_model,
                $lens_model,
                $focal_length,
                $aperture,
                $shutter_speed,
                $iso,
                $captured_at,
                $gps_latitude,
                $gps_longitude,
                $probed
            FROM assets AS a
            WHERE a.library_id = $library_id
              AND a.id = $asset_id
              AND a.source_revision = $source_revision
              AND a.file_size = $file_size
              AND a.modified_at_utc_ticks = $modified
            ON CONFLICT(asset_id) DO UPDATE SET
                source_revision = excluded.source_revision,
                camera_model = excluded.camera_model,
                lens_model = excluded.lens_model,
                focal_length = excluded.focal_length,
                aperture = excluded.aperture,
                shutter_speed = excluded.shutter_speed,
                iso = excluded.iso,
                captured_at = excluded.captured_at,
                gps_latitude = excluded.gps_latitude,
                gps_longitude = excluded.gps_longitude,
                probed_at_utc_ticks = excluded.probed_at_utc_ticks;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue(
            "$source_revision",
            expectedSourceRevision);
        command.Parameters.AddWithValue("$file_size", expectedFileSize);
        command.Parameters.AddWithValue(
            "$modified",
            expectedModifiedAtUtcTicks);
        command.Parameters.AddWithValue(
            "$camera_model",
            (object?)cameraModel ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$lens_model",
            (object?)lensModel ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$focal_length",
            (object?)focalLength ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$aperture",
            (object?)aperture ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$shutter_speed",
            (object?)shutterSpeed ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$iso",
            metadata.Iso.HasValue
                ? metadata.Iso.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$captured_at",
            (object?)capturedAt ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$gps_latitude",
            (object?)gpsLatitude ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$gps_longitude",
            (object?)gpsLongitude ?? DBNull.Value);
        command.Parameters.AddWithValue("$probed", nowTicks);

        return await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false) == 1;
    }

    private static string? NormalizeOptional(
        string? value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    private static string? ReadOptionalString(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
}
