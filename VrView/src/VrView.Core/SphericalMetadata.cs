using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace VrView.Core;

/// <summary>Projection declared by the Spherical Video V2 <c>proj</c> box.</summary>
public enum SphericalProjection
{
    /// <summary>No projection box (for example only <c>st3d</c> is present).</summary>
    None,
    /// <summary><c>equi</c>: equirectangular, possibly cropped (see the bounds).</summary>
    Equirect,
    /// <summary><c>cbmp</c>: cubemap (layout 0 = 3x2 grid: right, left, up / down, front, back).</summary>
    Cubemap,
    /// <summary><c>mshp</c>: arbitrary mesh embedded in the file (VR180 cameras).</summary>
    Mesh,
}

/// <summary>
/// Spherical video metadata of the first video track that has any.
/// </summary>
/// <param name="Stereo">
/// Frame packing from <c>st3d</c>. <c>null</c> for "stereo-custom" (mode 3: each eye has its own
/// mesh with its own texture coordinates). Right-left (mode 4) is reported as
/// <see cref="StereoMode.SideBySide"/> with <see cref="SwapEyes"/> set. Mono when there is no <c>st3d</c>.
/// </param>
/// <param name="Projection">Projection from <c>sv3d/proj</c> (or Equirect for V1 XML metadata).</param>
/// <param name="BoundsTop">Equirect: fraction of the full sphere cropped from the top (0..1).</param>
/// <param name="BoundsBottom">Equirect: fraction cropped from the bottom.</param>
/// <param name="BoundsLeft">Equirect: fraction cropped from the left.</param>
/// <param name="BoundsRight">Equirect: fraction cropped from the right.</param>
/// <param name="PoseYaw">Degrees, counter-clockwise around the up vector.</param>
/// <param name="PosePitch">Degrees, counter-clockwise around the right vector (after yaw).</param>
/// <param name="PoseRoll">Degrees, clockwise around the forward vector (after yaw and pitch).</param>
/// <param name="LeftMesh">
/// Mesh projection: the first (or only) mesh, coordinates as stored in the file (unit-ish sphere,
/// use <see cref="SphericalMetadata.Scale"/>). Texture coordinates are GL-style (v = 0 at the
/// bottom) and cover the whole frame; with a single mesh and a non-custom <paramref name="Stereo"/>
/// the caller still has to remap them per eye. <c>null</c> when the mesh could not be decoded or
/// has more than <see cref="SphericalMetadata.MaxMeshVertices"/> vertices.
/// </param>
/// <param name="RightMesh">Second mesh (right eye) when the file has two; otherwise <c>null</c>.</param>
public sealed record SphericalInfo(
    StereoMode? Stereo, SphericalProjection Projection,
    float BoundsTop, float BoundsBottom, float BoundsLeft, float BoundsRight,
    float PoseYaw, float PosePitch, float PoseRoll,
    Mesh? LeftMesh, Mesh? RightMesh)
{
    /// <summary>True for <c>st3d</c> mode 4 (right eye on the left half).</summary>
    public bool SwapEyes { get; init; }

    /// <summary>Cubemap layout from <c>cbmp</c> (0 = the standard 3x2 grid).</summary>
    public uint CubemapLayout { get; init; }

    /// <summary>Cubemap padding in pixels around each face, from <c>cbmp</c>.</summary>
    public uint CubemapPadding { get; init; }

    /// <summary>Tool that wrote the metadata (<c>svhd</c>, or V1 <c>StitchingSoftware</c>).</summary>
    public string? MetadataSource { get; init; }

    /// <summary>True when the info comes from the legacy V1 XML (uuid box) instead of st3d/sv3d.</summary>
    public bool IsV1 { get; init; }

    /// <summary>Horizontal coverage of an equirectangular projection, in degrees.</summary>
    public float HorizontalCoverageDeg => 360f * (1f - BoundsLeft - BoundsRight);

    /// <summary>Vertical coverage of an equirectangular projection, in degrees.</summary>
    public float VerticalCoverageDeg => 180f * (1f - BoundsTop - BoundsBottom);
}

/// <summary>
/// Reads Google Spherical Video metadata (V2: <c>st3d</c> / <c>sv3d</c>, including <c>mshp</c>
/// meshes; V1: the spherical XML uuid box) from MP4 / MOV files, without loading the file: only
/// box headers and the few small boxes of interest are read. Never throws on malformed input.
/// Spec: https://github.com/google/spatial-media/blob/master/docs/spherical-video-v2-rfc.md
/// </summary>
public static class SphericalMetadata
{
    /// <summary>Meshes with more vertices than this cannot use 16-bit indices and are dropped.</summary>
    public const int MaxMeshVertices = ushort.MaxValue;

    private const int MaxBoxesVisited = 200_000;
    private const int MaxSv3dBytes = 32 << 20;      // sv3d holding a mesh is normally < 1 MB
    private const int MaxInflatedBytes = 64 << 20;  // guard against deflate bombs
    private const int MaxXmlBytes = 1 << 20;
    private const int MaxOutputIndices = 6_000_000;

    private static ReadOnlySpan<byte> SphericalV1Uuid =>
        [0xff, 0xcc, 0x82, 0x63, 0xf8, 0x55, 0x4a, 0x93, 0x88, 0x14, 0x58, 0x7a, 0x02, 0x52, 0x1f, 0xdd];

    /// <summary>
    /// Returns the spherical metadata of the first video track that has any, or <c>null</c> when
    /// there is none, the stream is not seekable, or the file is not a readable MP4/MOV.
    /// The stream position is changed.
    /// </summary>
    public static SphericalInfo? Read(Stream seekableStream)
    {
        try
        {
            if (seekableStream is null || !seekableStream.CanSeek || !seekableStream.CanRead) return null;
            var walker = new Walker(seekableStream);
            foreach (var top in walker.Children(0, seekableStream.Length))
            {
                if (top.Type != Fcc("moov")) continue;
                foreach (var trak in walker.Children(top.DataStart, top.End))
                {
                    if (trak.Type != Fcc("trak")) continue;
                    var info = ReadTrak(walker, trak);
                    if (info != null) return info;
                }
                return null; // only one moov per file
            }
            return null;
        }
        catch (Exception)
        {
            // Malformed input must never crash the player.
            return null;
        }
    }

    /// <summary>Returns a copy of <paramref name="mesh"/> with positions multiplied by <paramref name="radius"/>.</summary>
    public static Mesh Scale(Mesh mesh, float radius)
    {
        var v = (float[])mesh.Vertices.Clone();
        for (int i = 0; i + 2 < v.Length; i += Mesh.FloatsPerVertex)
        {
            v[i] *= radius;
            v[i + 1] *= radius;
            v[i + 2] *= radius;
        }
        return new Mesh { Vertices = v, Indices = mesh.Indices };
    }

    // ------------------------------------------------------------------ track level

    private static SphericalInfo? ReadTrak(Walker w, Box trak)
    {
        bool isVideo = false;
        Box? stsd = null;
        byte[]? v1Xml = null;
        foreach (var b in w.Children(trak.DataStart, trak.End))
        {
            if (b.Type == Fcc("mdia"))
            {
                foreach (var m in w.Children(b.DataStart, b.End))
                {
                    if (m.Type == Fcc("hdlr"))
                    {
                        // FullBox header (4), pre_defined / QuickTime component type (4), handler_type (4).
                        var h = w.ReadAt(m.DataStart, 12, m.End);
                        if (h != null && BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(8)) == Fcc("vide")) isVideo = true;
                    }
                    else if (m.Type == Fcc("minf"))
                    {
                        var stbl = w.Find(m.DataStart, m.End, "stbl");
                        if (stbl is { } s) stsd = w.Find(s.DataStart, s.End, "stsd");
                    }
                }
            }
            else if (b.Type == Fcc("uuid") && b.Size - b.HeaderSize >= 16)
            {
                var id = w.ReadAt(b.DataStart, 16, b.End);
                if (id != null && id.AsSpan().SequenceEqual(SphericalV1Uuid))
                {
                    long len = b.End - b.DataStart - 16;
                    if (len > 0 && len <= MaxXmlBytes) v1Xml = w.ReadAt(b.DataStart + 16, (int)len, b.End);
                }
            }
        }
        if (!isVideo) return null;

        SphericalInfo? v2 = stsd is { } sd ? ReadSampleEntries(w, sd) : null;
        if (v2 != null) return v2;
        return v1Xml != null ? ParseV1Xml(Encoding.UTF8.GetString(v1Xml)) : null;
    }

    private static SphericalInfo? ReadSampleEntries(Walker w, Box stsd)
    {
        // stsd: FullBox header (4) + entry_count (4), then the sample entries as boxes.
        foreach (var entry in w.Children(stsd.DataStart + 8, stsd.End))
        {
            // VisualSampleEntry: 6 reserved + 2 data_reference_index + 16 pre_defined/reserved
            // + width/height (4) + resolutions (8) + reserved (4) + frame_count (2)
            // + compressorname (32) + depth (2) + pre_defined (2) = 78 bytes before the child boxes.
            long childStart = entry.DataStart + 78;
            if (childStart >= entry.End) continue;
            int? stereoRaw = null;
            SphericalInfo? sv3d = null;
            foreach (var c in w.Children(childStart, entry.End))
            {
                if (c.Type == Fcc("st3d"))
                {
                    var d = w.ReadAt(c.DataStart, 5, c.End);
                    if (d != null) stereoRaw = d[4];
                }
                else if (c.Type == Fcc("sv3d"))
                {
                    long len = c.End - c.DataStart;
                    if (len > 0 && len <= MaxSv3dBytes)
                    {
                        var data = w.ReadAt(c.DataStart, (int)len, c.End);
                        if (data != null) sv3d = ParseSv3d(data);
                    }
                }
            }
            if (stereoRaw == null && sv3d == null) continue;

            var (stereo, swap) = stereoRaw switch
            {
                null or 0 => ((StereoMode?)StereoMode.Mono, false),
                1 => (StereoMode.TopBottom, false),
                2 => (StereoMode.SideBySide, false),
                4 => (StereoMode.SideBySide, true),
                _ => (null, false), // 3 = stereo-custom (per-eye meshes); reserved values likewise unknown
            };
            var info = sv3d ?? new SphericalInfo(null, SphericalProjection.None, 0, 0, 0, 0, 0, 0, 0, null, null);
            return info with { Stereo = stereo, SwapEyes = swap };
        }
        return null;
    }

    // ------------------------------------------------------------------ sv3d

    /// <summary>Parses the payload (children) of an sv3d box.</summary>
    private static SphericalInfo? ParseSv3d(byte[] data)
    {
        var w = new Walker(new MemoryStream(data, writable: false));
        string? source = null;
        var proj = SphericalProjection.None;
        float top = 0, bottom = 0, left = 0, right = 0, yaw = 0, pitch = 0, roll = 0;
        uint layout = 0, padding = 0;
        Mesh? leftMesh = null, rightMesh = null;
        bool any = false;

        foreach (var b in w.Children(0, data.Length))
        {
            if (b.Type == Fcc("svhd"))
            {
                any = true;
                long len = b.End - b.DataStart - 4;
                if (len > 0 && len < 4096)
                {
                    var s = w.ReadAt(b.DataStart + 4, (int)len, b.End);
                    if (s != null)
                    {
                        int nul = Array.IndexOf(s, (byte)0);
                        source = Encoding.UTF8.GetString(s, 0, nul < 0 ? s.Length : nul);
                    }
                }
            }
            else if (b.Type == Fcc("proj"))
            {
                any = true;
                foreach (var p in w.Children(b.DataStart, b.End))
                {
                    if (p.Type == Fcc("prhd"))
                    {
                        var d = w.ReadAt(p.DataStart, 16, p.End);
                        if (d == null) continue;
                        yaw = Fixed16(d, 4);
                        pitch = Fixed16(d, 8);
                        roll = Fixed16(d, 12);
                    }
                    else if (p.Type == Fcc("equi"))
                    {
                        proj = SphericalProjection.Equirect;
                        var d = w.ReadAt(p.DataStart, 20, p.End);
                        if (d == null) continue;
                        top = Fixed32(d, 4);
                        bottom = Fixed32(d, 8);
                        left = Fixed32(d, 12);
                        right = Fixed32(d, 16);
                        // Invalid bounds (nothing left of the picture): ignore them.
                        if (top + bottom >= 1f) top = bottom = 0;
                        if (left + right >= 1f) left = right = 0;
                    }
                    else if (p.Type == Fcc("cbmp"))
                    {
                        proj = SphericalProjection.Cubemap;
                        var d = w.ReadAt(p.DataStart, 12, p.End);
                        if (d == null) continue;
                        layout = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4));
                        padding = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(8));
                    }
                    else if (p.Type == Fcc("mshp") || p.Type == Fcc("ytmp"))
                    {
                        // 'ytmp' is the pre-standard YouTube name of the same box (also accepted by ExoPlayer).
                        proj = SphericalProjection.Mesh;
                        var d = w.ReadAt(p.DataStart, (int)(p.End - p.DataStart), p.End);
                        if (d != null) (leftMesh, rightMesh) = ParseMshp(d);
                    }
                }
            }
        }
        if (!any) return null;
        return new SphericalInfo(StereoMode.Mono, proj, top, bottom, left, right, yaw, pitch, roll, leftMesh, rightMesh)
        {
            CubemapLayout = layout,
            CubemapPadding = padding,
            MetadataSource = source,
        };
    }

    /// <summary>Parses the payload of an mshp box (after its 8-byte header).</summary>
    private static (Mesh? Left, Mesh? Right) ParseMshp(byte[] d)
    {
        // FullBox version/flags (4) + crc32 (4, not verified, like ExoPlayer) + encoding four-cc (4).
        if (d.Length < 12 || d[0] != 0) return (null, null);
        uint encoding = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(8));
        byte[] payload;
        if (encoding == Fcc("raw "))
            payload = d[12..];
        else if (encoding == Fcc("dfl8"))
        {
            var inflated = Inflate(d, 12);
            if (inflated == null) return (null, null);
            payload = inflated;
        }
        else return (null, null);

        var w = new Walker(new MemoryStream(payload, writable: false));
        var meshes = new List<Mesh?>(2);
        foreach (var b in w.Children(0, payload.Length))
        {
            if (b.Type != Fcc("mesh")) continue;
            long len = b.End - b.DataStart;
            var body = len > 0 ? w.ReadAt(b.DataStart, (int)len, b.End) : null;
            meshes.Add(body == null ? null : DecodeMesh(body));
            if (meshes.Count == 2) break;
        }
        return meshes.Count switch
        {
            0 => (null, null),
            1 => (meshes[0], null),
            _ => (meshes[0], meshes[1]),
        };
    }

    private static byte[]? Inflate(byte[] d, int offset)
    {
        try
        {
            using var ds = new DeflateStream(new MemoryStream(d, offset, d.Length - offset, writable: false), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = ds.Read(buf, 0, buf.Length)) > 0)
            {
                if (ms.Length + n > MaxInflatedBytes) return null;
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }
        catch (InvalidDataException) { return null; }
    }

    // ------------------------------------------------------------------ mesh

    /// <summary>
    /// Decodes the payload of a <c>mesh</c> box. The RFC syntax puts byte-alignment padding after
    /// each vertex list, while ExoPlayer's decoder (the de facto reference) reads the lists back to
    /// back. Both layouts are identical with a single list. Otherwise both are tried and the one
    /// that decodes and ends exactly at the end of the box wins (ExoPlayer's on a tie); trailing
    /// bytes are allowed by the RFC, so a layout that decodes but leaves bytes over is the fallback.
    /// </summary>
    private static Mesh? DecodeMesh(byte[] body)
    {
        var (exo, exoExact) = DecodeMesh(body, padEachList: false);
        if (exo != null && exoExact) return exo;
        var (rfc, rfcExact) = DecodeMesh(body, padEachList: true);
        if (rfc != null && rfcExact) return rfc;
        return exo ?? rfc;
    }

    private static (Mesh? Mesh, bool ExactEnd) DecodeMesh(byte[] body, bool padEachList)
    {
        var r = new BitReader(body);
        // Coordinates: 1 reserved bit + u31 count, then float32 values.
        if (!r.TryRead(32, out uint cc)) return default;
        long coordCount = cc & 0x7FFFFFFF;
        if (coordCount == 0 || coordCount * 4 > r.RemainingBits / 8) return default;
        var coords = new float[coordCount];
        for (long i = 0; i < coordCount; i++)
        {
            r.TryRead(32, out uint bits);
            coords[i] = BitConverter.UInt32BitsToSingle(bits);
        }

        // Vertices: u31 count, then 5 zig-zag deltas (x, y, z, u, v) per vertex, each component
        // with its own running index, ccsb bits each.
        if (!r.TryRead(32, out uint vc)) return default;
        long vertexCount = vc & 0x7FFFFFFF;
        if (vertexCount == 0 || vertexCount > MaxMeshVertices) return default;
        int ccsb = BitsFor(coordCount);
        if (vertexCount * 5 * ccsb > r.RemainingBits) return default;
        var vertices = new float[vertexCount * Mesh.FloatsPerVertex];
        Span<long> running = stackalloc long[5];
        for (long v = 0, k = 0; v < vertexCount; v++)
            for (int c = 0; c < 5; c++)
            {
                r.TryRead(ccsb, out uint z);
                running[c] += ZigZag(z);
                if (running[c] < 0 || running[c] >= coordCount) return default;
                vertices[k++] = coords[running[c]];
            }
        r.AlignToByte();

        // Vertex lists: u32 count; each: u8 texture_id, u8 index_type, u31 index_count, then
        // zig-zag index deltas of vcsb bits (running index starting at zero for each list).
        if (!r.TryRead(32, out uint lc)) return default;
        long listCount = lc & 0x7FFFFFFF;
        int vcsb = BitsFor(vertexCount);
        var indices = new List<ushort>();
        var list = new List<int>();
        for (long l = 0; l < listCount; l++)
        {
            if (!r.TryRead(8, out uint textureId) || !r.TryRead(8, out uint indexType) || !r.TryRead(32, out uint ic))
                return default;
            long indexCount = ic & 0x7FFFFFFF;
            if (indexCount * vcsb > r.RemainingBits) return default;
            list.Clear();
            long index = 0;
            for (long i = 0; i < indexCount; i++)
            {
                r.TryRead(vcsb, out uint z);
                index += ZigZag(z);
                if (index < 0 || index >= vertexCount) return default;
                list.Add((int)index);
            }
            if (padEachList) r.AlignToByte();
            if (textureId != 0) continue; // > 0 is reserved: not the video frame
            if (!AppendTriangles(list, indexType, indices)) return default;
            if (indices.Count > MaxOutputIndices) return default;
        }
        if (indices.Count == 0) return default;
        r.AlignToByte();
        return (new Mesh { Vertices = vertices, Indices = indices.ToArray() }, r.RemainingBits == 0);
    }

    /// <summary>Converts a triangle list / strip / fan to triangle-list indices (CCW kept).</summary>
    private static bool AppendTriangles(List<int> l, uint type, List<ushort> output)
    {
        void Tri(int a, int b, int c)
        {
            if (a == b || b == c || a == c) return; // degenerate (strip joins)
            output.Add((ushort)a); output.Add((ushort)b); output.Add((ushort)c);
        }
        switch (type)
        {
            case 0:
                for (int i = 0; i + 2 < l.Count; i += 3) Tri(l[i], l[i + 1], l[i + 2]);
                return true;
            case 1:
                for (int i = 2; i < l.Count; i++)
                    if ((i & 1) == 0) Tri(l[i - 2], l[i - 1], l[i]);
                    else Tri(l[i - 1], l[i - 2], l[i]);
                return true;
            case 2:
                for (int i = 2; i < l.Count; i++) Tri(l[0], l[i - 1], l[i]);
                return true;
            default:
                return false;
        }
    }

    /// <summary>ceil(log2(count * 2)): bits of each zig-zag delta.</summary>
    private static int BitsFor(long count)
    {
        ulong x = (ulong)count * 2;
        return x <= 1 ? 0 : BitOperations.Log2(x - 1) + 1;
    }

    private static long ZigZag(uint z) => (z & 1) == 0 ? (long)(z >> 1) : -(long)(z >> 1) - 1;

    private sealed class BitReader(byte[] data)
    {
        private long _pos; // in bits
        public long RemainingBits => (long)data.Length * 8 - _pos;

        public bool TryRead(int bits, out uint value)
        {
            value = 0;
            if (bits < 0 || bits > 32 || bits > RemainingBits) return false;
            ulong acc = 0;
            for (int i = 0; i < bits; i++)
            {
                int bit = (data[_pos >> 3] >> (7 - (int)(_pos & 7))) & 1;
                acc = (acc << 1) | (uint)bit;
                _pos++;
            }
            value = (uint)acc;
            return true;
        }

        public void AlignToByte() => _pos = (_pos + 7) & ~7L;
    }

    // ------------------------------------------------------------------ V1 XML

    private static string? XmlTag(string xml, string name)
    {
        var m = Regex.Match(xml, @"<(?:[\w.-]+:)?" + name + @"\b[^>]*>\s*([^<]*?)\s*<", RegexOptions.CultureInvariant);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static SphericalInfo? ParseV1Xml(string xml)
    {
        if (!string.Equals(XmlTag(xml, "Spherical"), "true", StringComparison.OrdinalIgnoreCase)) return null;
        string? projType = XmlTag(xml, "ProjectionType");
        if (projType != null && !projType.Equals("equirectangular", StringComparison.OrdinalIgnoreCase)) return null;

        StereoMode? stereo = XmlTag(xml, "StereoMode")?.ToLowerInvariant() switch
        {
            null or "" or "mono" => StereoMode.Mono,
            "top-bottom" => StereoMode.TopBottom,
            "left-right" => StereoMode.SideBySide,
            _ => null,
        };

        float top = 0, bottom = 0, left = 0, right = 0;
        float? fullW = Num(xml, "FullPanoWidthPixels"), fullH = Num(xml, "FullPanoHeightPixels");
        float? cropW = Num(xml, "CroppedAreaImageWidthPixels"), cropH = Num(xml, "CroppedAreaImageHeightPixels");
        float cropL = Num(xml, "CroppedAreaLeftPixels") ?? 0, cropT = Num(xml, "CroppedAreaTopPixels") ?? 0;
        if (fullW > 0 && cropW > 0 && cropW <= fullW)
        {
            left = Math.Clamp(cropL / fullW.Value, 0, 1);
            right = Math.Clamp(1f - (cropL + cropW.Value) / fullW.Value, 0, 1);
            if (left + right >= 1f) left = right = 0;
        }
        if (fullH > 0 && cropH > 0 && cropH <= fullH)
        {
            top = Math.Clamp(cropT / fullH.Value, 0, 1);
            bottom = Math.Clamp(1f - (cropT + cropH.Value) / fullH.Value, 0, 1);
            if (top + bottom >= 1f) top = bottom = 0;
        }
        return new SphericalInfo(stereo, SphericalProjection.Equirect, top, bottom, left, right,
            Num(xml, "PoseHeadingDegrees") ?? 0, Num(xml, "PosePitchDegrees") ?? 0, Num(xml, "PoseRollDegrees") ?? 0,
            null, null)
        {
            IsV1 = true,
            MetadataSource = XmlTag(xml, "StitchingSoftware"),
        };

        static float? Num(string xml, string name) =>
            float.TryParse(XmlTag(xml, name), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) && float.IsFinite(f) ? f : null;
    }

    // ------------------------------------------------------------------ box walking

    private static uint Fcc(string s) =>
        (uint)s[0] << 24 | (uint)s[1] << 16 | (uint)s[2] << 8 | s[3];

    private static float Fixed16(byte[] d, int o) => BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(o)) / 65536f;

    private static float Fixed32(byte[] d, int o) => (float)(BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o)) / 4294967296.0);

    private readonly record struct Box(uint Type, long Start, long HeaderSize, long End)
    {
        public long DataStart => Start + HeaderSize;
        public long Size => End - Start;
    }

    /// <summary>Iterates boxes by seeking over their payloads; shares a visit budget against loops.</summary>
    private sealed class Walker(Stream s)
    {
        private int _visited;
        private readonly byte[] _hdr = new byte[16];

        public IEnumerable<Box> Children(long start, long end)
        {
            end = Math.Min(end, s.Length);
            long pos = start;
            while (pos + 8 <= end && ++_visited <= MaxBoxesVisited)
            {
                if (!ReadInto(pos, _hdr, 8)) yield break;
                long size = BinaryPrimitives.ReadUInt32BigEndian(_hdr);
                uint type = BinaryPrimitives.ReadUInt32BigEndian(_hdr.AsSpan(4));
                long header = 8;
                if (size == 1)
                {
                    if (pos + 16 > end || !ReadInto(pos + 8, _hdr, 8)) yield break;
                    ulong large = BinaryPrimitives.ReadUInt64BigEndian(_hdr);
                    if (large > long.MaxValue) yield break;
                    size = (long)large;
                    header = 16;
                }
                else if (size == 0)
                    size = end - pos; // extends to the end of the enclosing box / file
                if (size < header) yield break;
                // A truncated file: clamp the last box to what is there.
                long boxEnd = size > end - pos ? end : pos + size;
                yield return new Box(type, pos, header, boxEnd);
                pos = boxEnd;
            }
        }

        public Box? Find(long start, long end, string type)
        {
            uint t = Fcc(type);
            foreach (var b in Children(start, end))
                if (b.Type == t) return b;
            return null;
        }

        /// <summary>Reads exactly <paramref name="count"/> bytes at <paramref name="pos"/> if they lie before <paramref name="limit"/>.</summary>
        public byte[]? ReadAt(long pos, int count, long limit)
        {
            if (count < 0 || pos < 0 || pos + count > limit) return null;
            var buf = new byte[count];
            return ReadInto(pos, buf, count) ? buf : null;
        }

        private bool ReadInto(long pos, byte[] buf, int count)
        {
            if (pos < 0 || pos + count > s.Length) return false;
            s.Position = pos;
            int done = 0;
            while (done < count)
            {
                int n = s.Read(buf, done, count - done);
                if (n <= 0) return false;
                done += n;
            }
            return true;
        }
    }
}
