using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

internal sealed class CompanyImageProcessor : ICompanyImageProcessor
{
    public ManagedLogoImage? Prepare(byte[] raw, CancellationToken cancellationToken)
    {
        if (!TryFormat(raw, out var jpeg)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = new MemoryStream(raw, writable: false);
            var decoder = jpeg
                ? (BitmapDecoder)new JpegBitmapDecoder(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad)
                : new PngBitmapDecoder(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1) return null;
            var frame = decoder.Frames[0];
            if (!ValidDimensions(frame.PixelWidth, frame.PixelHeight)) return null;

            // Copy decoded pixels into a fresh bitmap to discard embedded metadata and paths.
            var decoded = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var stride = checked(frame.PixelWidth * 4);
            var buffer = new byte[checked(stride * frame.PixelHeight)];
            decoded.CopyPixels(buffer, stride, 0);
            var pixels = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Bgra32, null, buffer, stride);
            var full = Encode(pixels, jpeg ? new JpegBitmapEncoder { QualityLevel = 90 } : new PngBitmapEncoder());
            cancellationToken.ThrowIfCancellationRequested();
            if (full.Length > CompanyLogo.MaximumBytes) return null;
            var factor = Math.Min(1.0, 80.0 / Math.Max(pixels.PixelWidth, pixels.PixelHeight));
            var small = new TransformedBitmap(pixels, new ScaleTransform(
                Math.Max(factor, 1.0 / pixels.PixelWidth), Math.Max(factor, 1.0 / pixels.PixelHeight)));
            var thumbnail = Encode(small, new PngBitmapEncoder());
            return thumbnail.Length > 65536 ? null : new(jpeg ? "image/jpeg" : "image/png", full, thumbnail);
        }
        catch (Exception error) when (error is IOException or FormatException or ArgumentException or
            NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    internal static ImageSource? Preview(byte[]? content)
    {
        if (content is not { } bytes || !TryFormat(bytes, out _)) return null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 220;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or FormatException or ArgumentException or
            NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static byte[] Encode(BitmapSource pixels, BitmapEncoder encoder)
    {
        using var stream = new MemoryStream();
        encoder.Frames.Add(BitmapFrame.Create(pixels));
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static bool TryFormat(byte[] raw, out bool jpeg)
    {
        jpeg = false;
        if (raw.Length is < 45 or > CompanyLogo.MaximumBytes) return false;
        if (CompanyLogo.IsBoundedPng(raw)) return true;
        jpeg = raw[0] == 0xff && raw[1] == 0xd8;
        if (!jpeg) return false;

        // Inspect JPEG dimensions before passing any compressed pixels to the decoder.
        for (var offset = 2; offset < raw.Length;)
        {
            if (raw[offset++] != 0xff) return false;
            while (offset < raw.Length && raw[offset] == 0xff) offset++;
            if (offset + 3 > raw.Length) return false;
            var marker = raw[offset++];
            if (marker is 0x00 or 0xd8 or 0xd9 or 0xda) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(offset, 2));
            if (length < 2 || offset + length > raw.Length) return false;
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 8) return false;
                var height = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(offset + 5, 2));
                return ValidDimensions(width, height);
            }
            offset += length;
        }

        return false;
    }

    private static bool ValidDimensions(int width, int height) => width is > 0 and <= 2048 && height is > 0 and <= 2048;
}
