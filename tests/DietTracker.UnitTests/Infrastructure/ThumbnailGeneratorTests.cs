using DietTracker.Infrastructure.FileStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Graphics.Imaging;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>
/// Tests of the thumbnail pipeline, focused on photographs that carry a 90 or 270 degree EXIF
/// orientation: their pixel buffer comes back rotated (width and height swapped) while keeping the
/// same byte count, so the encoder must be told the oriented size. Declaring the unrotated size does
/// not throw - it makes the encoder read every row with the wrong stride and the tile comes out
/// sheared, doubled and banded.
/// </summary>
public sealed class ThumbnailGeneratorTests
{
    private const int MaxSize = 256;

    [Fact]
    public async Task A_photo_rotated_by_exif_orientation_produces_an_upright_thumbnail_of_the_rotated_size()
    {
        var directory = CreateWorkDirectory();
        try
        {
            var source = await CreateTestJpegAsync(directory, 40, 20); // left half red, right half blue
            InjectExifOrientation(source, 6);                          // rotate 90 degrees clockwise

            var thumbnail = Path.Combine(directory, "thumb.png");
            Assert.True(await ThumbnailGenerator.TryCreateAsync(source, thumbnail, MaxSize));

            var (width, height, pixels) = await ReadImageAsync(thumbnail);

            // 40x20 rotated by 90 degrees is 20x40. The damaged build wrote 40x20 and sheared every
            // row of the buffer, which is what produced the doubled, banded tiles.
            Assert.Equal(20, width);
            Assert.Equal(40, height);

            // Rotating clockwise puts the red (left) half on top and the blue half at the bottom.
            AssertRed(PixelAt(pixels, width, 4, 4));
            AssertBlue(PixelAt(pixels, width, 4, 36));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_photo_without_a_rotation_keeps_its_dimensions()
    {
        var directory = CreateWorkDirectory();
        try
        {
            var source = await CreateTestJpegAsync(directory, 40, 20);
            InjectExifOrientation(source, 1); // upright already

            var thumbnail = Path.Combine(directory, "thumb.png");
            Assert.True(await ThumbnailGenerator.TryCreateAsync(source, thumbnail, MaxSize));

            var (width, height, pixels) = await ReadImageAsync(thumbnail);

            Assert.Equal(40, width);
            Assert.Equal(20, height);
            AssertRed(PixelAt(pixels, width, 4, 10));
            AssertBlue(PixelAt(pixels, width, 36, 10));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_repair_pass_detects_a_transposed_thumbnail_and_rewrites_it_in_place()
    {
        var directory = CreateWorkDirectory();
        try
        {
            var store = new PhotoStore(
                new AppDataPaths(Path.Combine(directory, "store")),
                NullLogger<PhotoStore>.Instance);

            var source = await CreateTestJpegAsync(directory, 40, 20);
            InjectExifOrientation(source, 6);

            var import = await store.ImportAsync(Guid.NewGuid(), source);
            Assert.True(import.Succeeded);
            var photo = import.Photo!;
            Assert.NotNull(photo);

            var sourcePath = store.GetFullSizePath(photo.LocalFileId);
            var thumbnailPath = store.GetThumbnailPath(photo.ThumbnailId);
            Assert.NotNull(sourcePath);
            Assert.NotNull(thumbnailPath);

            // A healthy import passes the check ...
            Assert.True(await ThumbnailGenerator.IsThumbnailValidAsync(sourcePath!, thumbnailPath!, MaxSize));

            // ... then the thumbnail is damaged the way the old build damaged it: width and height
            // transposed (40x20 written where 20x40 belongs).
            File.Copy(source, thumbnailPath!, overwrite: true);
            Assert.False(await ThumbnailGenerator.IsThumbnailValidAsync(sourcePath!, thumbnailPath!, MaxSize));

            Assert.Equal(1, await store.RepairThumbnailsAsync([photo]));

            var (width, height, pixels) = await ReadImageAsync(thumbnailPath!);
            Assert.Equal(20, width);
            Assert.Equal(40, height);
            AssertRed(PixelAt(pixels, width, 4, 4));
            AssertBlue(PixelAt(pixels, width, 4, 36));

            // Nothing left to do on the next run: the pass only rewrites what is broken.
            Assert.Equal(0, await store.RepairThumbnailsAsync([photo]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Creates a real JPEG of the requested size: the left half red, the right half blue.</summary>
    private static async Task<string> CreateTestJpegAsync(string directory, int width, int height)
    {
        var path = Path.Combine(directory, $"source-{Guid.NewGuid():N}.jpg");
        using var stream = File.Create(path);
        using var randomAccess = stream.AsRandomAccessStream();

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, randomAccess);
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var blue = x >= (width / 2);
                pixels[offset] = blue ? (byte)200 : (byte)0;     // B
                pixels[offset + 1] = 0;                          // G
                pixels[offset + 2] = blue ? (byte)0 : (byte)220; // R
                pixels[offset + 3] = 255;                        // A
            }
        }

        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        return path;
    }

    /// <summary>
    /// Inserts a minimal EXIF APP1 segment (little-endian TIFF, one IFD entry) carrying the orientation
    /// tag right after the SOI marker, exactly like a phone camera does.
    /// </summary>
    private static void InjectExifOrientation(string path, ushort orientation)
    {
        var jpeg = File.ReadAllBytes(path);
        Assert.True(jpeg.Length > 2 && jpeg[0] == 0xFF && jpeg[1] == 0xD8);

        var tiff = new byte[26];
        tiff[0] = (byte)'I';
        tiff[1] = (byte)'I';                   // little-endian byte order
        tiff[2] = 0x2A;
        tiff[3] = 0x00;                        // TIFF magic
        tiff[4] = 0x08;                        // IFD0 starts at offset 8
        tiff[8] = 0x01;
        tiff[9] = 0x00;                        // one entry
        tiff[10] = 0x12;
        tiff[11] = 0x01;                       // tag 0x0112 = orientation
        tiff[12] = 0x03;
        tiff[13] = 0x00;                       // type SHORT
        tiff[14] = 0x01;
        tiff[15] = 0x00;
        tiff[16] = 0x00;
        tiff[17] = 0x00;                       // count 1
        tiff[18] = (byte)(orientation & 0xFF);
        tiff[19] = (byte)(orientation >> 8);   // value; bytes 20..23 stay padding
                                                 // bytes 24..25 are the next-IFD offset (none)

        var payloadLength = 6 + tiff.Length;    // "Exif\0\0" + TIFF
        var segment = new byte[4 + payloadLength];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        segment[2] = (byte)((payloadLength + 2) >> 8);
        segment[3] = (byte)((payloadLength + 2) & 0xFF);
        System.Text.Encoding.ASCII.GetBytes("Exif\0\0").CopyTo(segment, 4);
        tiff.CopyTo(segment, 10);

        var result = new byte[2 + segment.Length + jpeg.Length - 2];
        jpeg.AsSpan(0, 2).CopyTo(result);       // SOI
        segment.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + segment.Length));
        File.WriteAllBytes(path, result);
    }

    /// <summary>Decodes an image and returns its stored size together with the BGRA pixels.</summary>
    private static async Task<(int Width, int Height, byte[] Pixels)> ReadImageAsync(string path)
    {
        using var stream = File.OpenRead(path);
        using var randomAccess = stream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccess);
        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData());
    }

    private static byte[] PixelAt(byte[] pixels, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return [pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]];
    }

    private static void AssertRed(byte[] bgra)
        => Assert.True(bgra[2] > 150 && bgra[1] < 120 && bgra[0] < 120, $"expected red, got BGRA {bgra[0]},{bgra[1]},{bgra[2]}");

    private static void AssertBlue(byte[] bgra)
        => Assert.True(bgra[0] > 150 && bgra[1] < 120 && bgra[2] < 120, $"expected blue, got BGRA {bgra[0]},{bgra[1]},{bgra[2]}");

    private static string CreateWorkDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DietTracker.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}