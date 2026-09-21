using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace TenantForge.Modules.Shop.Features.Media;

internal sealed record SanitizedShopImage(MemoryStream Content, long ByteLength, int Width, int Height);

internal sealed class ShopImageValidator
{
    private const long MaxBytes = 5L * 1024L * 1024L;
    private const int MaxDimension = 4096;
    private const long MaxPixels = 16_000_000L;

    public async Task<SanitizedShopImage> ValidateAndReencodeAsync(Stream input, CancellationToken ct)
    {
        await using var bytes = new MemoryStream();
        await CopyWithLimitAsync(input, bytes, MaxBytes, ct);
        bytes.Position = 0;

        IImageFormat? format;
        try
        {
            format = await Image.DetectFormatAsync(bytes, ct);
        }
        catch (UnknownImageFormatException)
        {
            // Content that is not any recognizable image format at all (plain
            // text, SVG markup, etc.) — DetectFormatAsync throws rather than
            // returning null for this case. A clean 415, never a 500.
            throw new ShopImageValidationException(ShopImageValidationFailure.UnsupportedMedia, "Only JPEG, PNG and WebP images are supported.");
        }

        if (!IsAllowedFormat(format))
        {
            throw new ShopImageValidationException(ShopImageValidationFailure.UnsupportedMedia, "Only JPEG, PNG and WebP images are supported.");
        }

        bytes.Position = 0;
        Image image;
        try
        {
            image = await Image.LoadAsync(bytes, ct);
        }
        catch (ImageFormatException)
        {
            // The bytes announced a supported format (DetectFormatAsync above)
            // but the pixel data itself is corrupt/truncated — a clean 415,
            // never an unhandled 500.
            throw new ShopImageValidationException(ShopImageValidationFailure.UnsupportedMedia, "The image file is corrupt or unreadable.");
        }

        using (image)
        {
            if (image.Frames.Count != 1)
            {
                throw new ShopImageValidationException(ShopImageValidationFailure.UnsupportedMedia, "Animated or multi-frame images are not supported.");
            }

            if (image.Width <= 0 || image.Height <= 0 || image.Width > MaxDimension || image.Height > MaxDimension || (long)image.Width * image.Height > MaxPixels)
            {
                throw new ShopImageValidationException(ShopImageValidationFailure.Oversized, "Image dimensions exceed the allowed limits.");
            }

            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;

            var sanitized = new MemoryStream();
            await image.SaveAsWebpAsync(sanitized, new WebpEncoder { Quality = 82 }, ct);
            sanitized.Position = 0;
            return new SanitizedShopImage(sanitized, sanitized.Length, image.Width, image.Height);
        }
    }

    private static async Task CopyWithLimitAsync(Stream input, Stream output, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
            {
                throw new ShopImageValidationException(ShopImageValidationFailure.Oversized, "Images must be 5 MiB or smaller.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static bool IsAllowedFormat(IImageFormat? format) =>
        format is not null && (format == JpegFormat.Instance || format == PngFormat.Instance || format == WebpFormat.Instance);
}

internal enum ShopImageValidationFailure
{
    Oversized,
    UnsupportedMedia
}

internal sealed class ShopImageValidationException(ShopImageValidationFailure failure, string message) : Exception(message)
{
    public ShopImageValidationFailure Failure { get; } = failure;
}
