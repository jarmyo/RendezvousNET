using VrView.Core;
using ZXing;
using ZXing.QrCode;

namespace VrView.Core.Tests;

public class ViewerProfileTests
{
    /// <summary>Exact bytes of getCardboardV1DeviceParams() in the Cardboard SDK (sdk/qrcode/cardboard_v1/cardboard_v1.cc).</summary>
    private static readonly byte[] SdkCardboardV1 =
    [
        0xa, 0xc, 0x47, 0x6f, 0x6f, 0x67, 0x6c, 0x65, 0x2c, 0x20, 0x49,
        0x6e, 0x63, 0x2e, 0x12, 0xc, 0x43, 0x61, 0x72, 0x64, 0x62, 0x6f,
        0x61, 0x72, 0x64, 0x20, 0x76, 0x31, 0x1d, 0x31, 0x8, 0x2c, 0x3d,
        0x25, 0x8f, 0xc2, 0x75, 0x3d, 0x2a, 0x10, 0x0, 0x0, 0x20, 0x42,
        0x0, 0x0, 0x20, 0x42, 0x0, 0x0, 0x20, 0x42, 0x0, 0x0, 0x20,
        0x42, 0x35, 0x29, 0x5c, 0xf, 0x3d, 0x3a, 0x8, 0xc1, 0xca, 0xe1,
        0x3e, 0x77, 0xbe, 0x1f, 0x3e, 0x58, 0x0, 0x60, 0x1,
    ];

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void AssertSame(ViewerProfile expected, ViewerProfile? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Vendor, actual.Vendor);
        Assert.Equal(expected.Model, actual.Model);
        Assert.Equal(expected.ScreenToLensDistance, actual.ScreenToLensDistance);
        Assert.Equal(expected.InterLensDistance, actual.InterLensDistance);
        Assert.Equal(expected.LeftEyeFovAngles, actual.LeftEyeFovAngles);
        Assert.Equal(expected.VerticalAlignment, actual.VerticalAlignment);
        Assert.Equal(expected.TrayToLensDistance, actual.TrayToLensDistance);
        Assert.Equal(expected.DistortionCoefficients, actual.DistortionCoefficients);
        Assert.Equal(expected.HasMagnet, actual.HasMagnet);
        Assert.Equal(expected.PrimaryButton, actual.PrimaryButton);
    }

    [Fact]
    public void SdkCardboardV1Bytes_DecodeToPreset()
    {
        var p = ViewerProfile.FromProtobuf(SdkCardboardV1);
        AssertSame(ViewerProfile.CardboardV1, p);
        Assert.Equal(0.042f, p!.ScreenToLensDistance, 5);
        Assert.Equal([0.441f, 0.156f], p.DistortionCoefficients);
        Assert.Equal(ViewerProfile.ButtonMagnet, p.PrimaryButton);
    }

    [Fact]
    public void CardboardV1Preset_EncodesToSdkBytes()
    {
        Assert.Equal(SdkCardboardV1, ViewerProfile.CardboardV1.ToProtobuf());
    }

    [Theory]
    [InlineData("https://google.com/cardboard/cfg?p=")]
    [InlineData("http://google.com/cardboard/cfg?p=")]
    [InlineData("google.com/cardboard/cfg?p=")]
    [InlineData("https://www.google.com/cardboard/cfg?p=")]
    [InlineData("https://g.co/cardboard?p=")]
    [InlineData("  HTTPS://Google.com/cardboard/cfg?foo=1&p=")]
    public void FromUrl_AcceptsUrlVariants(string prefix)
    {
        AssertSame(ViewerProfile.CardboardV1, ViewerProfile.FromUrl(prefix + B64Url(SdkCardboardV1)));
    }

    [Fact]
    public void FromUrl_OriginalCardboardQr_IsV1()
    {
        AssertSame(ViewerProfile.CardboardV1, ViewerProfile.FromUrl("https://g.co/cardboard"));
        AssertSame(ViewerProfile.CardboardV1, ViewerProfile.FromUrl("http://g.co/cardboard/"));
    }

    [Fact]
    public void FromUrl_ToleratesPaddingAndStandardBase64()
    {
        string std = Convert.ToBase64String(SdkCardboardV1); // may contain + / =
        AssertSame(ViewerProfile.CardboardV1, ViewerProfile.FromUrl("https://google.com/cardboard/cfg?p=" + Uri.EscapeDataString(std)));
    }

    [Fact]
    public void RoundTrip_Url_And_Protobuf()
    {
        var custom = new ViewerProfile("Acme VR", "Box ñ 3", 0.0405f, 0.0625f, [52f, 48f, 55f, 57f],
            ViewerProfile.AlignCenter, 0.031f, [0.25f, 0.12f, -0.01f], HasMagnet: true, ViewerProfile.ButtonTouch);
        foreach (var p in new[] { custom, ViewerProfile.CardboardV1, ViewerProfile.CardboardV2, ViewerProfile.Default })
        {
            AssertSame(p, ViewerProfile.FromProtobuf(p.ToProtobuf()));
            string url = p.ToUrl();
            Assert.StartsWith(ViewerProfile.UrlPrefix, url);
            Assert.DoesNotContain('=', url[ViewerProfile.UrlPrefix.Length..]);
            AssertSame(p, ViewerProfile.FromUrl(url));
        }
    }

    [Fact]
    public void FromProtobuf_NonPackedFloatsAndUnknownFields()
    {
        var b = new List<byte>();
        void F(int field, float v)
        {
            for (uint tag = (uint)((field << 3) | 5); ; tag >>= 7)
            {
                if (tag < 0x80) { b.Add((byte)tag); break; }
                b.Add((byte)(tag | 0x80));
            }
            b.AddRange(BitConverter.GetBytes(v));
        }
        b.AddRange([0x0a, 0x01, (byte)'X']);           // vendor
        b.AddRange([0x40, 0x96, 0x01]);                 // field 8 varint (unknown)
        F(3, 0.04f); F(4, 0.064f);
        foreach (var a in new[] { 45f, 46f, 47f, 48f }) F(5, a); // non-packed repeated
        b.AddRange([0x4a, 0x03, 1, 2, 3]);              // field 9 bytes (unknown)
        F(7, 0.3f); F(7, 0.2f);
        b.AddRange([0x79, 1, 2, 3, 4, 5, 6, 7, 8]);     // field 15 fixed64 (unknown)
        F(99, 1f);                                      // unknown fixed32 with a 2-byte tag
        var p = ViewerProfile.FromProtobuf(b.ToArray());
        Assert.NotNull(p);
        Assert.Equal("X", p.Vendor);
        Assert.Equal([45f, 46f, 47f, 48f], p.LeftEyeFovAngles);
        Assert.Equal([0.3f, 0.2f], p.DistortionCoefficients);
        Assert.Equal(ViewerProfile.AlignBottom, p.VerticalAlignment); // proto default
        Assert.Equal(ViewerProfile.ButtonMagnet, p.PrimaryButton);    // proto default
        Assert.True(p.HasMagnet);
    }

    [Fact]
    public void Malformed_ReturnsNull()
    {
        Assert.Null(ViewerProfile.FromProtobuf([]));
        Assert.Null(ViewerProfile.FromProtobuf(SdkCardboardV1.AsSpan(0, 30)));      // truncated mid-field
        Assert.Null(ViewerProfile.FromProtobuf(SdkCardboardV1.AsSpan(0, 28)));      // missing required distances
        Assert.Null(ViewerProfile.FromProtobuf([0x0a, 0x7f, 0x41]));               // length past end
        Assert.Null(ViewerProfile.FromProtobuf([0x1a, 0x00]));                     // known field, wrong wire type
        Assert.Null(ViewerProfile.FromProtobuf([0x0b, 0x0c]));                     // group wire type
        Assert.Null(ViewerProfile.FromProtobuf([0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01])); // bad varint
        Assert.Null(ViewerProfile.FromProtobuf(System.Text.Encoding.ASCII.GetBytes("hello, this is not a protobuf")));
        var nan = (byte[])SdkCardboardV1.Clone();
        nan[29] = 0; nan[30] = 0; nan[31] = 0xc0; nan[32] = 0x7f;                 // screen_to_lens = NaN
        Assert.Null(ViewerProfile.FromProtobuf(nan));

        Assert.Null(ViewerProfile.FromUrl(null));
        Assert.Null(ViewerProfile.FromUrl(""));
        Assert.Null(ViewerProfile.FromUrl("not a url at all"));
        Assert.Null(ViewerProfile.FromUrl("https://google.com/cardboard/cfg"));
        Assert.Null(ViewerProfile.FromUrl("https://google.com/cardboard/cfg?p="));
        Assert.Null(ViewerProfile.FromUrl("https://google.com/cardboard/cfg?p=@@@###"));
        Assert.Null(ViewerProfile.FromUrl("https://google.com/cardboard/cfg?p=QUJD"));
        Assert.Null(ViewerProfile.FromUrl("https://example.com/cardboard/cfg?p=" + B64Url(SdkCardboardV1)));
        Assert.Null(ViewerProfile.FromUrl("ftp://google.com/cardboard/cfg?p=" + B64Url(SdkCardboardV1)));
        Assert.Null(ViewerProfile.FromUrl("https://goo.gl/abc123"));
    }

    [Theory]
    [InlineData("https://goo.gl/xyz123", true)]
    [InlineData("goo.gl/xyz123", true)]
    [InlineData("http://g.co/xyz", true)]
    [InlineData("https://bit.ly/abc", true)]
    [InlineData("https://g.co/cardboard", false)]
    [InlineData("https://goo.gl/", false)]
    [InlineData("https://example.com/abc", false)]
    [InlineData("https://google.com/cardboard/cfg?p=CgA", false)]
    [InlineData("", false)]
    public void IsShortLink(string url, bool expected) => Assert.Equal(expected, ViewerProfile.IsShortLink(url));

    // ---------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------

    private const float PhoneW = 0.1365f, PhoneH = 0.0645f; // ~6.1" 19.5:9 phone, landscape

    [Fact]
    public void RenderParams_AreSane()
    {
        foreach (var p in new[] { ViewerProfile.CardboardV1, ViewerProfile.CardboardV2 })
        {
            var (fov, ipd, k1, k2) = p.ToRenderParams(PhoneW, PhoneH);
            Assert.InRange(fov, 80f, 120f);
            Assert.InRange(ipd, -0.3f, 0.3f);
            Assert.InRange(k1, 0.05f, 0.6f);
            Assert.InRange(k2, 0f, 0.6f);
        }
        // Without distortion and a lens exactly on each half-screen center.
        var plain = ViewerProfile.CardboardV1 with { DistortionCoefficients = [], InterLensDistance = PhoneW / 2 };
        var r = plain.ToRenderParams(PhoneW, PhoneH);
        Assert.Equal(0f, r.K1);
        Assert.Equal(0f, r.K2);
        Assert.Equal(0f, r.IpdShiftNdc, 5);
        Assert.Equal(2 * MathF.Atan(PhoneH / 2 / plain.ScreenToLensDistance) * VrMath.Rad2Deg, r.FovDeg, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => plain.ToRenderParams(0, PhoneH));
    }

    [Fact]
    public void RenderParams_IpdSign_PositiveWhenLensesAreWider()
    {
        Assert.True((ViewerProfile.CardboardV2 with { InterLensDistance = PhoneW / 2 + 0.005f }).ToRenderParams(PhoneW, PhoneH).IpdShiftNdc > 0);
        Assert.True((ViewerProfile.CardboardV2 with { InterLensDistance = PhoneW / 2 - 0.005f }).ToRenderParams(PhoneW, PhoneH).IpdShiftNdc < 0);
    }

    /// <summary>
    /// Simulates the app's pipeline (perspective with Fov, then Shaders.DistortionFragment) and
    /// checks that every screen point is seen in the direction the Cardboard lens model predicts.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RenderParams_MatchCardboardLensModel(int which)
    {
        var p = (which == 0 ? ViewerProfile.CardboardV1 : ViewerProfile.CardboardV2) with { InterLensDistance = PhoneW / 2 };
        var (fov, ipd, k1, k2) = p.ToRenderParams(PhoneW, PhoneH);
        Assert.Equal(0f, ipd, 5);
        float aspect = PhoneW / 2 / PhoneH, tanHalf = MathF.Tan(fov * VrMath.Deg2Rad / 2), scale = 1 + k1 + k2;
        float ck1 = p.DistortionCoefficients[0], ck2 = p.DistortionCoefficients[1], d = p.ScreenToLensDistance;

        for (float qx = -1; qx <= 1.001f; qx += 0.25f)
            for (float qy = -1; qy <= 1.001f; qy += 0.25f)
            {
                // App: shader maps eye-viewport NDC q to FBO NDC s; the FBO is a perspective view.
                float r2 = (qx * aspect * qx * aspect + qy * qy) / (aspect * aspect);
                float f = (1 + k1 * r2 + k2 * r2 * r2) / scale;
                float appTanX = qx * f * tanHalf * aspect, appTanY = qy * f * tanHalf;

                // Cardboard: screen tan-angle from the lens center, then the radial polynomial.
                float tx = qx * PhoneW / 4 / d, ty = qy * PhoneH / 2 / d, t2 = tx * tx + ty * ty;
                float g = 1 + ck1 * t2 + ck2 * t2 * t2;
                Assert.Equal(tx * g, appTanX, 3);
                Assert.Equal(ty * g, appTanY, 3);
            }
    }

    /// <summary>The straight-ahead direction must appear on screen right under each lens center.</summary>
    [Theory]
    [InlineData(0.058f)]
    [InlineData(0.064f)]
    [InlineData(0.072f)]
    public void RenderParams_StraightAheadLandsOnLensCenter(float interLens)
    {
        var p = ViewerProfile.CardboardV2 with { InterLensDistance = interLens };
        var (_, ipd, k1, k2) = p.ToRenderParams(PhoneW, PhoneH);
        float scale = 1 + k1 + k2;
        // Left eye (side = -1): lens center in that eye's viewport NDC.
        float lensX = (PhoneW / 4 - interLens / 2) / (PhoneW / 4);
        float r2 = lensX * lensX;
        float sampled = lensX * (1 + k1 * r2 + k2 * r2 * r2) / scale; // FBO NDC shown at the lens center
        Assert.Equal(-1f * ipd, sampled, 5);                           // = where VrMath.Projection puts the center
    }

    // ---------------------------------------------------------------------------------------
    // QR decoding
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Renders <paramref name="text"/> as a QR code into a "photo": rotated, non-integer module
    /// size, uneven lighting, a little blur and noise, inside a larger picture.
    /// </summary>
    private static int[] FakePhoto(string text, int width, int height, float modulePx, float angleDeg, bool inverted, int seed)
    {
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0); // modules incl. quiet zone
        int n = matrix.Width;
        float cos = MathF.Cos(angleDeg * VrMath.Deg2Rad), sin = MathF.Sin(angleDeg * VrMath.Deg2Rad);
        float cx = width * 0.55f, cy = height * 0.45f;
        var rng = new Random(seed);
        var lum = new float[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // Inverse-rotate into code space.
                float dx = x - cx, dy = y - cy;
                float u = (cos * dx + sin * dy) / modulePx + n / 2f;
                float v = (-sin * dx + cos * dy) / modulePx + n / 2f;
                float light = 150 + 80 * x / (float)width - 30 * y / (float)height; // uneven lighting
                float val = light;
                if (u >= 0 && v >= 0 && u < n && v < n)
                {
                    bool dark = matrix[(int)u, (int)v] ^ inverted;
                    val = dark ? light * 0.18f : light;
                }
                else if (inverted && u >= -2 && v >= -2 && u < n + 2 && v < n + 2)
                {
                    val = light * 0.18f; // dark frame around a light-on-dark code
                }
                lum[y * width + x] = val;
            }
        var pixels = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // 3x3 box blur + noise.
                float sum = 0; int cnt = 0;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        int xx = x + i, yy = y + j;
                        if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
                        sum += lum[yy * width + xx]; cnt++;
                    }
                int g = Math.Clamp((int)(sum / cnt + (rng.NextDouble() - 0.5) * 50), 0, 255);
                int r = Math.Clamp(g + 10, 0, 255), b = Math.Clamp(g - 10, 0, 255);
                pixels[y * width + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
            }
        return pixels;
    }

    [Theory]
    [InlineData(1600, 1200, 9.3f, 7f, false)]
    [InlineData(1600, 900, 5.6f, -12f, false)]
    [InlineData(1200, 1600, 14.7f, 3f, false)]
    [InlineData(1600, 1200, 8.1f, 20f, true)]
    public void Qr_PhotoOfViewerCode_Decodes(int w, int h, float modulePx, float angle, bool inverted)
    {
        string url = ViewerProfile.CardboardV2.ToUrl();
        var pixels = FakePhoto(url, w, h, modulePx, angle, inverted, seed: w + h);
        string? text = QrDecoder.Decode(pixels, w, h);
        Assert.Equal(url, text);
        AssertSame(ViewerProfile.CardboardV2, ViewerProfile.FromUrl(text!));
    }

    [Fact]
    public void Qr_NoCode_ReturnsNull()
    {
        var rng = new Random(1);
        var pixels = new int[640 * 480];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = unchecked((int)0xFF000000) | rng.Next(0x1000000);
        Assert.Null(QrDecoder.Decode(pixels, 640, 480));
        Assert.Null(QrDecoder.Decode(new int[10], 640, 480));
    }
}
