using Windows.Graphics.Imaging;

namespace DietTracker.Infrastructure.FileStorage;

/// <summary>
/// Generates thumbnails with the Windows Imaging Component (through the WinRT imaging API), which
/// supports JPEG, PNG and WEBP without adding an image library to the application.
/// </summary>
public static class ThumbnailGenerator
{
    /// <summary>
    /// Creates a downscaled PNG thumbnail honouring the EXIF orientation.
    /// Returns false when the image cannot be decoded (the caller then simply has no thumbnail).
    /// </summary>
    public static async Task<bool> TryCreateAsync(string sourcePath, string targetPath, int maxSize, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var sourceStream = File.OpenRead(sourcePath);
            using var sourceRandomAccess = sourceStream.AsRandomAccessStream();

            var decoder = await BitmapDecoder.CreateAsync(sourceRandomAccess).AsTask(cancellationToken).ConfigureAwait(false);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
            {
                return false;
            }

            var longestEdge = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            var scale = Math.Min(1d, (double)maxSize / longestEdge);

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            var pixelData = await decoder
                .GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            using var targetStream = File.Create(targetPath);
            using var targetRandomAccess = targetStream.AsRandomAccessStream();

            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, targetRandomAccess).AsTask(cancellationToken).ConfigureAwait(false);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform.ScaledWidth,
                transform.ScaledHeight,
                96,
                96,
                pixelData.DetachPixelData());

            await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing thumbnail must never block attaching a photograph.
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            return false;
        }
    }
}
