using System.Buffers.Binary;
using System.Text;

namespace VrView.Core;

/// <summary>
/// Parameters of a phone VR viewer ("Cardboard-style" headset), as stored in the QR code printed
/// on the viewer. Mirrors the <c>DeviceParams</c> message of the Cardboard SDK
/// (<c>proto/cardboard_device.proto</c>):
/// <code>
///   vendor = 1 (string)                  model = 2 (string)
///   screen_to_lens_distance = 3 (float)  inter_lens_distance = 4 (float)
///   left_eye_field_of_view_angles = 5 (packed float: outer, inner, bottom, top, degrees)
///   tray_to_lens_distance = 6 (float)    distortion_coefficients = 7 (packed float)
///   has_magnet = 10 (bool, legacy field of the old Cardboard proto, not in the current SDK)
///   vertical_alignment = 11 (enum BOTTOM=0 [default], CENTER=1, TOP=2)
///   primary_button = 12 (enum NONE=0, MAGNET=1 [default], TOUCH=2, INDIRECT_TOUCH=3)
/// </code>
/// Lengths are in meters. Note that the float arrays make record equality reference-based for
/// those members; compare them with <c>SequenceEqual</c>.
/// </summary>
public sealed record ViewerProfile(
    string Vendor,
    string Model,
    float ScreenToLensDistance,
    float InterLensDistance,
    float[] LeftEyeFovAngles /* outer, inner, bottom, top in degrees */,
    int VerticalAlignment /* 0 bottom, 1 center, 2 top */,
    float TrayToLensDistance,
    float[] DistortionCoefficients,
    bool HasMagnet,
    int PrimaryButton /* 0 none, 1 magnet, 2 touch, 3 indirect touch */)
{
    public const int AlignBottom = 0, AlignCenter = 1, AlignTop = 2;
    public const int ButtonNone = 0, ButtonMagnet = 1, ButtonTouch = 2, ButtonIndirectTouch = 3;

    /// <summary>Base of the long-form viewer URL encoded in Cardboard QR codes.</summary>
    public const string UrlPrefix = "https://google.com/cardboard/cfg?p=";

    /// <summary>
    /// Original Cardboard (2014). Exact values of the Cardboard SDK's built-in
    /// <c>getCardboardV1DeviceParams()</c>; the original viewer's QR just says
    /// <c>https://g.co/cardboard</c>, which <see cref="FromUrl"/> maps to this profile.
    /// </summary>
    public static ViewerProfile CardboardV1 { get; } = new(
        "Google, Inc.", "Cardboard v1", 0.042f, 0.06f, [40f, 40f, 40f, 40f],
        AlignBottom, 0.035f, [0.441f, 0.156f], HasMagnet: true, ButtonMagnet);

    /// <summary>
    /// Cardboard "I/O 2015" (v2). Commonly published values of its QR code (not taken from the SDK
    /// sources, so treat them as nominal).
    /// </summary>
    public static ViewerProfile CardboardV2 { get; } = new(
        "Google, Inc.", "Cardboard I/O 2015", 0.039f, 0.0639f, [60f, 60f, 60f, 60f],
        AlignBottom, 0.035f, [0.34f, 0.55f], HasMagnet: false, ButtonTouch);

    /// <summary>Profile used when the user has not scanned a viewer (Cardboard v2-like).</summary>
    public static ViewerProfile Default => CardboardV2;

    /// <summary>Vertical field of view of the lens itself (bottom + top angle), in degrees.</summary>
    public float LensVerticalFovDeg => LeftEyeFovAngles[2] + LeftEyeFovAngles[3];

    // ---------------------------------------------------------------------------------------
    // URL handling
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Parses the text of a Cardboard QR code. Accepts <c>https://google.com/cardboard/cfg?p=…</c>,
    /// the same with <c>http://</c>, <c>www.</c> or no scheme, <c>g.co/cardboard?p=…</c>, and the
    /// bare <c>https://g.co/cardboard</c> of the original viewer (→ <see cref="CardboardV1"/>).
    /// Returns null for anything else, including short links (see <see cref="IsShortLink"/>).
    /// </summary>
    public static ViewerProfile? FromUrl(string? url)
    {
        if (!TryParseHttpUri(url, out var uri)) return null;
        string host = NormalizeHost(uri.Host);
        string path = uri.AbsolutePath.TrimEnd('/');
        bool cardboardPath = path.Equals("/cardboard/cfg", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/cardboard", StringComparison.OrdinalIgnoreCase);
        if (host is not ("google.com" or "g.co") || !cardboardPath) return null;

        string? p = QueryParam(uri.Query, "p");
        if (p is null)
        {
            // The original Cardboard QR carries no parameters at all.
            return host == "g.co" && path.Equals("/cardboard", StringComparison.OrdinalIgnoreCase)
                ? CardboardV1 : null;
        }
        var bytes = DecodeBase64Url(p);
        return bytes is null ? null : FromProtobuf(bytes);
    }

    /// <summary>
    /// True for URL-shortener links (goo.gl, g.co other than <c>g.co/cardboard</c>, bit.ly, …) that
    /// many third-party viewers print instead of the long form. The caller must resolve them by
    /// following HTTP redirects (the Cardboard SDK follows up to 5) and pass the final URL to
    /// <see cref="FromUrl"/>.
    /// </summary>
    public static bool IsShortLink(string? url)
    {
        if (!TryParseHttpUri(url, out var uri)) return false;
        if (FromUrl(url) is not null) return false;
        string host = NormalizeHost(uri.Host);
        if (uri.AbsolutePath.Trim('/').Length == 0) return false;
        return host is "goo.gl" or "g.co" or "bit.ly" or "tinyurl.com" or "ow.ly" or "t.co"
            or "is.gd" or "rebrand.ly" or "cutt.ly" or "shorturl.at" or "qrco.de";
    }

    /// <summary>Encodes this profile as a long-form Cardboard URL (inverse of <see cref="FromUrl"/>).</summary>
    public string ToUrl() => UrlPrefix + EncodeBase64Url(ToProtobuf());

    private static bool TryParseHttpUri(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url)) return false;
        string s = url.Trim();
        if (!s.Contains("://", StringComparison.Ordinal)) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) return false;
        uri = u;
        return true;
    }

    private static string NormalizeHost(string host)
    {
        host = host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private static string? QueryParam(string query, string name)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            string key = eq < 0 ? part : part[..eq];
            if (key == name) return eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return null;
    }

    /// <summary>URL-safe base64 without padding; also tolerates standard base64 and padding.</summary>
    private static byte[]? DecodeBase64Url(string s)
    {
        s = s.Trim().TrimEnd('=').Replace('-', '+').Replace('_', '/');
        if (s.Length == 0 || s.Length % 4 == 1) return null;
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }

    private static string EncodeBase64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---------------------------------------------------------------------------------------
    // Protobuf
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Decodes a serialized <c>DeviceParams</c> message. Unknown fields are skipped; repeated
    /// floats are accepted packed or not. Returns null for malformed data or when the required
    /// optics (screen-to-lens and inter-lens distance) are missing or implausible.
    /// </summary>
    public static ViewerProfile? FromProtobuf(ReadOnlySpan<byte> data)
    {
        string vendor = "", model = "";
        float? screenToLens = null, interLens = null;
        float trayToLens = Default.TrayToLensDistance;
        var fov = new List<float>(4);
        var distortion = new List<float>(4);
        bool? hasMagnet = null;
        int vertical = AlignBottom, button = ButtonMagnet; // proto defaults

        var r = new ProtoReader(data);
        while (!r.End)
        {
            if (!r.TryReadTag(out int field, out int wire)) return null;
            bool ok = (field, wire) switch
            {
                (1, 2) => r.TryReadString(out vendor),
                (2, 2) => r.TryReadString(out model),
                (3, 5) => r.TryReadFloat(out var v3) && Set(ref screenToLens, v3),
                (4, 5) => r.TryReadFloat(out var v4) && Set(ref interLens, v4),
                (5, 2 or 5) => r.TryReadFloats(wire, fov),
                (6, 5) => r.TryReadFloat(out trayToLens),
                (7, 2 or 5) => r.TryReadFloats(wire, distortion),
                (10, 0) => r.TryReadVarint(out var v10) && Set(ref hasMagnet, v10 != 0),
                (11, 0) => r.TryReadVarint(out var v11) && SetEnum(ref vertical, v11),
                (12, 0) => r.TryReadVarint(out var v12) && SetEnum(ref button, v12),
                (1 or 2 or 3 or 4 or 5 or 6 or 7 or 10 or 11 or 12, _) => false, // known field, wrong type
                _ => r.TrySkip(wire),
            };
            if (!ok) return null;
        }

        if (screenToLens is not { } d || !(d > 0.005f && d < 0.5f)) return null;
        if (interLens is not { } ipd || !(ipd > 0.01f && ipd < 0.5f)) return null;
        if (!float.IsFinite(trayToLens)) return null;
        if (distortion.Any(k => !float.IsFinite(k))) return null;
        float[] angles = fov.Count >= 4 && fov.Take(4).All(a => a > 0 && a < 90)
            ? fov.Take(4).ToArray()
            : (float[])Default.LeftEyeFovAngles.Clone();
        if (vertical is < 0 or > 2) vertical = AlignBottom;

        return new ViewerProfile(vendor, model, d, ipd, angles, vertical, trayToLens,
            distortion.ToArray(), hasMagnet ?? button == ButtonMagnet, button);

        static bool Set<T>(ref T? slot, T value) where T : struct { slot = value; return true; }
        static bool SetEnum(ref int slot, ulong value) { slot = value <= int.MaxValue ? (int)value : -1; return true; }
    }

    /// <summary>Serializes this profile as a <c>DeviceParams</c> message.</summary>
    public byte[] ToProtobuf()
    {
        var w = new List<byte>(96);
        if (Vendor.Length > 0) WriteBytes(w, 1, Encoding.UTF8.GetBytes(Vendor));
        if (Model.Length > 0) WriteBytes(w, 2, Encoding.UTF8.GetBytes(Model));
        WriteFloat(w, 3, ScreenToLensDistance);
        WriteFloat(w, 4, InterLensDistance);
        WritePackedFloats(w, 5, LeftEyeFovAngles);
        WriteFloat(w, 6, TrayToLensDistance);
        WritePackedFloats(w, 7, DistortionCoefficients);
        // has_magnet is a legacy field: only written when it disagrees with primary_button.
        if (HasMagnet != (PrimaryButton == ButtonMagnet)) { WriteTag(w, 10, 0); WriteVarint(w, HasMagnet ? 1u : 0u); }
        WriteTag(w, 11, 0); WriteVarint(w, (ulong)VerticalAlignment);
        WriteTag(w, 12, 0); WriteVarint(w, (ulong)PrimaryButton);
        return w.ToArray();
    }

    private static void WriteTag(List<byte> w, int field, int wire) => WriteVarint(w, (ulong)((field << 3) | wire));

    private static void WriteVarint(List<byte> w, ulong v)
    {
        while (v >= 0x80) { w.Add((byte)(v | 0x80)); v >>= 7; }
        w.Add((byte)v);
    }

    private static void WriteFloatRaw(List<byte> w, float v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, v);
        foreach (var x in b) w.Add(x);
    }

    private static void WriteFloat(List<byte> w, int field, float v) { WriteTag(w, field, 5); WriteFloatRaw(w, v); }

    private static void WriteBytes(List<byte> w, int field, byte[] data)
    {
        WriteTag(w, field, 2);
        WriteVarint(w, (ulong)data.Length);
        w.AddRange(data);
    }

    private static void WritePackedFloats(List<byte> w, int field, float[] values)
    {
        if (values.Length == 0) return;
        WriteTag(w, field, 2);
        WriteVarint(w, (ulong)(values.Length * 4));
        foreach (var v in values) WriteFloatRaw(w, v);
    }

    /// <summary>Minimal protobuf wire-format reader (varint, fixed64, length-delimited, fixed32).</summary>
    private ref struct ProtoReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _pos;

        public readonly bool End => _pos >= _data.Length;

        public bool TryReadVarint(out ulong value)
        {
            value = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                if (_pos >= _data.Length) return false;
                byte b = _data[_pos++];
                if (shift == 63 && b > 1) return false; // overflows 64 bits
                value |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80) return true;
            }
            return false;
        }

        public bool TryReadTag(out int field, out int wire)
        {
            field = wire = 0;
            if (!TryReadVarint(out var tag) || tag > uint.MaxValue) return false;
            field = (int)(tag >> 3);
            wire = (int)(tag & 7);
            return field > 0;
        }

        private bool TryReadLength(out ReadOnlySpan<byte> slice)
        {
            slice = default;
            if (!TryReadVarint(out var len) || len > (ulong)(_data.Length - _pos)) return false;
            slice = _data.Slice(_pos, (int)len);
            _pos += (int)len;
            return true;
        }

        public bool TryReadFloat(out float value)
        {
            value = 0;
            if (_data.Length - _pos < 4) return false;
            value = BinaryPrimitives.ReadSingleLittleEndian(_data[_pos..]);
            _pos += 4;
            return true;
        }

        public bool TryReadString(out string value)
        {
            value = "";
            if (!TryReadLength(out var s)) return false;
            try { value = new UTF8Encoding(false, true).GetString(s); return true; }
            catch (ArgumentException) { return false; }
        }

        /// <summary>Reads one element (wire type 5) or a packed run (wire type 2) of floats.</summary>
        public bool TryReadFloats(int wire, List<float> into)
        {
            if (wire == 5)
            {
                if (!TryReadFloat(out var v)) return false;
                into.Add(v);
                return true;
            }
            if (!TryReadLength(out var s) || s.Length % 4 != 0) return false;
            for (int i = 0; i < s.Length; i += 4) into.Add(BinaryPrimitives.ReadSingleLittleEndian(s[i..]));
            return true;
        }

        public bool TrySkip(int wire)
        {
            switch (wire)
            {
                case 0: return TryReadVarint(out _);
                case 1: if (_data.Length - _pos < 8) return false; _pos += 8; return true;
                case 2: return TryReadLength(out _);
                case 5: if (_data.Length - _pos < 4) return false; _pos += 4; return true;
                default: return false; // groups (3/4) and invalid wire types are not supported
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Mapping to the app's renderer
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Converts the viewer optics into the app's render settings for a phone in landscape whose
    /// screen measures <paramref name="screenWidthMeters"/> × <paramref name="screenHeightMeters"/>
    /// (each eye gets half of the width, full height).
    /// <para>
    /// How the app renders (VrRenderer + Shaders.DistortionFragment): each eye is rendered into an
    /// FBO with a perspective projection of vertical FOV <c>Fov</c> and aspect a = (W/2)/H, shifted
    /// by <c>side * IpdShift</c> NDC units after projection (VrMath.Projection; side = -1 for the
    /// left eye, so a positive IpdShift moves both images outward). The distortion pass then shows,
    /// at eye-viewport NDC point q, the FBO texel at s = q · (1 + K1 r² + K2 r⁴) / (1 + K1 + K2),
    /// where r = |q in isotropic units| / a, i.e. r = (distance from the viewport center) / (W/4):
    /// r = 1 at the left/right edge of the eye's viewport.
    /// </para>
    /// <para>
    /// Cardboard model (lens_distortion.cc): a screen point at tan-angle t = (distance from the lens
    /// center) / screen_to_lens_distance is seen at view tan-angle t · (1 + k1 t² + k2 t⁴).
    /// </para>
    /// <para>
    /// Matching both: with c = (W/4) / d we have t = c·r, hence K1 = k1·c², K2 = k2·c⁴. The app's
    /// fixed normalization 1/(1+K1+K2) then also fixes the scale: the FBO's NDC y must equal view
    /// tan / tan(Fov/2), which holds exactly when tan(Fov/2) = (1+K1+K2) · (H/2) / d. So the
    /// returned FOV is the one that makes the rendering metrically correct for this viewer; it is
    /// usually close to, but not the same as, the lens FOV limits (<see cref="LensVerticalFovDeg"/>),
    /// which in the Cardboard SDK only clip the visible area (the app does not clip).
    /// </para>
    /// <para>
    /// IpdShift: the left lens center sits (W/4 − IPD/2) to the right of its viewport center, which
    /// is x = 1 − 2·IPD/W in that eye's NDC. The straight-ahead direction (content NDC = side·IpdShift)
    /// must land there after distortion, so side·IpdShift = x·f(|x|) with f the shader factor; for
    /// either eye this gives IpdShift = (2·IPD/W − 1) · f. Positive when the lenses are farther apart
    /// than the two half-screen centers.
    /// </para>
    /// Assumptions/limitations: the app distorts around the viewport center, not around the lens
    /// center (exact only when IpdShift ≈ 0); the lens is assumed vertically centered on the screen
    /// (vertical_alignment / tray_to_lens_distance are ignored, the app cannot shift vertically);
    /// only the first two distortion coefficients are used. The caller may still clamp the results
    /// to the ranges its UI allows (Fov 40–130°, IpdShift ±0.3).
    /// </summary>
    public (float FovDeg, float IpdShiftNdc, float K1, float K2) ToRenderParams(float screenWidthMeters, float screenHeightMeters)
    {
        if (!(screenWidthMeters > 0) || !(screenHeightMeters > 0) || !float.IsFinite(screenWidthMeters) || !float.IsFinite(screenHeightMeters))
            throw new ArgumentOutOfRangeException(nameof(screenWidthMeters), "Screen size must be positive.");

        float w = screenWidthMeters, h = screenHeightMeters, d = ScreenToLensDistance;
        float k1 = DistortionCoefficients.Length > 0 ? DistortionCoefficients[0] : 0f;
        float k2 = DistortionCoefficients.Length > 1 ? DistortionCoefficients[1] : 0f;

        float c = w / 4f / d;
        float bigK1 = k1 * c * c;
        float bigK2 = k2 * c * c * c * c;
        float scale = 1f + bigK1 + bigK2;

        float fov = 2f * MathF.Atan(scale * (h / 2f) / d) * VrMath.Rad2Deg;

        float x = 2f * InterLensDistance / w - 1f;
        float r2 = x * x;
        float f = (1f + bigK1 * r2 + bigK2 * r2 * r2) / scale;
        float ipdShift = x * f;

        return (fov, ipdShift, bigK1, bigK2);
    }
}
