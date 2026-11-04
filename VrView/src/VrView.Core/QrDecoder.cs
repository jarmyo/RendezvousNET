using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace VrView.Core;

/// <summary>
/// Finds and decodes a QR code in a camera picture (used to read the Cardboard code printed on a
/// viewer). Uses the ZXing core classes directly (no bitmap bindings) so it stays platform
/// independent: the caller hands over ARGB pixels, e.g. from Android's <c>Bitmap.GetPixels</c>.
/// </summary>
public static class QrDecoder
{
    private static readonly Dictionary<DecodeHintType, object> Hints = new()
    {
        [DecodeHintType.TRY_HARDER] = true,
        [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
        [DecodeHintType.CHARACTER_SET] = "UTF-8",
    };

    /// <summary>
    /// Decodes the first QR code found in <paramref name="argbPixels"/> (row-major, 0xAARRGGBB,
    /// alpha ignored). Returns its text, or null when none is found. Tries, in order: the picture
    /// with a local-threshold binarizer, the inverted picture (light code on dark background), a
    /// global-histogram binarizer, and a half-size copy (helps with large, noisy or blurry photos).
    /// Works best with pictures of about 800–2000 px on the long side.
    /// </summary>
    public static string? Decode(int[] argbPixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(argbPixels);
        if (width <= 0 || height <= 0 || argbPixels.Length < (long)width * height) return null;

        byte[] lum = ToLuminance(argbPixels, width, height);
        string? text = TryAll(lum, width, height);
        if (text is null && Math.Min(width, height) >= 400)
        {
            var (half, hw, hh) = Downscale2x(lum, width, height);
            text = TryAll(half, hw, hh);
        }
        return text;
    }

    private static string? TryAll(byte[] lum, int width, int height)
    {
        LuminanceSource source = new RGBLuminanceSource(lum, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
        return TryDecode(new HybridBinarizer(source))
            ?? TryDecode(new HybridBinarizer(source.invert()))
            ?? TryDecode(new GlobalHistogramBinarizer(source))
            ?? TryDecode(new GlobalHistogramBinarizer(source.invert()));
    }

    private static string? TryDecode(Binarizer binarizer)
    {
        try
        {
            // A fresh reader per attempt: QRCodeReader keeps a little state between calls.
            var result = new QRCodeReader().decode(new BinaryBitmap(binarizer), Hints);
            return result?.Text;
        }
        catch (ReaderException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // ZXing can throw on degenerate geometry found in noisy pictures.
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Rec. 601 luma, integer approximation.</summary>
    private static byte[] ToLuminance(int[] argb, int width, int height)
    {
        int n = width * height;
        var lum = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int c = argb[i];
            int r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
            lum[i] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
        }
        return lum;
    }

    /// <summary>2×2 box filter: halves the size and averages out sensor noise.</summary>
    private static (byte[] Lum, int Width, int Height) Downscale2x(byte[] lum, int width, int height)
    {
        int w = width / 2, h = height / 2;
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int row0 = 2 * y * width, row1 = row0 + width;
            for (int x = 0; x < w; x++)
            {
                int sx = 2 * x;
                dst[y * w + x] = (byte)((lum[row0 + sx] + lum[row0 + sx + 1] + lum[row1 + sx] + lum[row1 + sx + 1] + 2) >> 2);
            }
        }
        return (dst, w, h);
    }
}
