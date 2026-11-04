using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using VrView.Core;

namespace VrView.Core.Tests;

public class SphericalMetadataTests
{
    // ------------------------------------------------------------------ MP4 box writer

    private static byte[] Box(string type, params byte[][] parts)
    {
        int len = 8 + parts.Sum(p => p.Length);
        var b = new byte[len];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)len);
        Encoding.ASCII.GetBytes(type, 0, 4, b, 4);
        int o = 8;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }

    private static byte[] FullBox(string type, int version, params byte[][] parts) =>
        Box(type, [new byte[] { (byte)version, 0, 0, 0 }, .. parts]);

    private static byte[] U8(int v) => [(byte)v];
    private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }
    private static byte[] I32(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); return b; }
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] Fixed16(float deg) => I32((int)MathF.Round(deg * 65536f));
    private static byte[] Fixed32(double frac) => U32((uint)Math.Round(frac * 4294967296.0));

    private static byte[] Hdlr(string handler) =>
        FullBox("hdlr", 0, U32(0), Ascii(handler), new byte[12], Ascii("Handler\0"));

    /// <summary>VisualSampleEntry: 78 bytes of fixed fields, then the child boxes.</summary>
    private static byte[] SampleEntry(string type, params byte[][] children)
    {
        var fixedPart = new byte[78];
        BinaryPrimitives.WriteUInt16BigEndian(fixedPart.AsSpan(6), 1);    // data_reference_index
        BinaryPrimitives.WriteUInt16BigEndian(fixedPart.AsSpan(24), 256); // width
        BinaryPrimitives.WriteUInt16BigEndian(fixedPart.AsSpan(26), 128); // height
        BinaryPrimitives.WriteUInt16BigEndian(fixedPart.AsSpan(74), 24);  // depth
        return Box(type, [fixedPart, Box("avcC", new byte[7]), .. children]);
    }

    private static byte[] Trak(string handler, byte[] sampleEntry, params byte[][] trakExtras) =>
        Box("trak", [
            FullBox("tkhd", 0, new byte[80]),
            Box("mdia",
                FullBox("mdhd", 0, new byte[20]),
                Hdlr(handler),
                Box("minf",
                    FullBox("vmhd", 0, new byte[8]),
                    Box("stbl",
                        FullBox("stsd", 0, U32(1), sampleEntry),
                        FullBox("stts", 0, U32(0))))),
            .. trakExtras]);

    private static byte[] Moov(params byte[][] traks) => Box("moov", [FullBox("mvhd", 0, new byte[96]), .. traks]);

    private static byte[] Ftyp() => Box("ftyp", Ascii("isom"), U32(512), Ascii("isomavc1"));

    private static byte[] St3d(int mode) => FullBox("st3d", 0, U8(mode));

    private static byte[] Sv3d(byte[] projectionBox, float yaw = 0, float pitch = 0, float roll = 0, string source = "test") =>
        Box("sv3d",
            FullBox("svhd", 0, Ascii(source + "\0")),
            Box("proj",
                FullBox("prhd", 0, Fixed16(yaw), Fixed16(pitch), Fixed16(roll)),
                projectionBox));

    private static byte[] Equi(double top, double bottom, double left, double right) =>
        FullBox("equi", 0, Fixed32(top), Fixed32(bottom), Fixed32(left), Fixed32(right));

    /// <summary>File with moov after a dummy mdat (the common non-faststart layout).</summary>
    private static MemoryStream File(byte[] moov) => new(Cat(Ftyp(), Box("mdat", new byte[100]), moov));

    // ------------------------------------------------------------------ mesh encoder

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = new();
        private int _bit; // bits used in the last byte

        public void Write(uint value, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                if (_bit == 0) _bytes.Add(0);
                if (((value >> i) & 1) != 0) _bytes[^1] |= (byte)(0x80 >> _bit);
                _bit = (_bit + 1) & 7;
            }
        }

        public void Align() => _bit = 0;
        public byte[] ToArray() => _bytes.ToArray();
    }

    private static int Bits(int count) => (int)Math.Ceiling(Math.Log2(count * 2.0));
    private static uint ZigZag(int n) => n >= 0 ? (uint)n * 2 : (uint)(-n * 2 - 1);

    private sealed record VertexList(int TextureId, int Type, int[] Indices);

    /// <summary>Encodes a mesh box payload per the Spherical Video V2 RFC.</summary>
    private static byte[] EncodeMesh(float[] coords, int[][] vertices, VertexList[] lists, bool padEachList = false)
    {
        var w = new BitWriter();
        w.Write((uint)coords.Length, 32);
        foreach (var c in coords) w.Write(BitConverter.SingleToUInt32Bits(c), 32);
        w.Write((uint)vertices.Length, 32);
        int ccsb = Bits(coords.Length);
        var prev = new int[5];
        foreach (var v in vertices)
            for (int c = 0; c < 5; c++)
            {
                w.Write(ZigZag(v[c] - prev[c]), ccsb);
                prev[c] = v[c];
            }
        w.Align();
        w.Write((uint)lists.Length, 32);
        int vcsb = Bits(vertices.Length);
        foreach (var l in lists)
        {
            w.Write((uint)l.TextureId, 8);
            w.Write((uint)l.Type, 8);
            w.Write((uint)l.Indices.Length, 32);
            int p = 0;
            foreach (var i in l.Indices) { w.Write(ZigZag(i - p), vcsb); p = i; }
            if (padEachList) w.Align();
        }
        return Box("mesh", w.ToArray());
    }

    private static byte[] Mshp(bool deflate, params byte[][] meshes)
    {
        byte[] payload = Cat(meshes);
        if (deflate)
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) ds.Write(payload);
            payload = ms.ToArray();
        }
        // crc32 is not verified by the reader; write a dummy value.
        return FullBox("mshp", 0, U32(0xDEADBEEF), Ascii(deflate ? "dfl8" : "raw "), payload);
    }

    /// <summary>
    /// A 4-vertex quad: coordinates pool has the distinct values, vertices index into it.
    /// Positions (-1,-1,-1) (1,-1,-1) (1,1,-1) (-1,1,-1); uv (0,0) (1,0) (1,1) (0,1).
    /// </summary>
    private static readonly float[] QuadCoords = [-1f, 1f, 0f, 0.25f, 0.5f];
    private static readonly int[][] QuadVertices =
    [
        [0, 0, 0, 2, 2], // (-1,-1,-1) uv (0, 0)
        [1, 0, 0, 1, 2], // ( 1,-1,-1) uv (1, 0)
        [1, 1, 0, 1, 1], // ( 1, 1,-1) uv (1, 1)
        [0, 1, 0, 2, 1], // (-1, 1,-1) uv (0, 1)
    ];

    private static float[] ExpectedQuadVertices(float[] coords) =>
        QuadVertices.SelectMany(v => v.Select(i => coords[i])).ToArray();

    // ------------------------------------------------------------------ tests

    [Fact]
    public void Reads_equirect_with_stereo_pose_and_bounds()
    {
        var entry = SampleEntry("avc1", St3d(2), Sv3d(Equi(0.25, 0.25, 0.25, 0.25), yaw: 90, pitch: -10.5f, roll: 3.25f, source: "Unit"));
        var info = SphericalMetadata.Read(File(Moov(Trak("vide", entry))));

        Assert.NotNull(info);
        Assert.Equal(StereoMode.SideBySide, info.Stereo);
        Assert.False(info.SwapEyes);
        Assert.Equal(SphericalProjection.Equirect, info.Projection);
        Assert.Equal(0.25f, info.BoundsTop, 5);
        Assert.Equal(0.25f, info.BoundsRight, 5);
        Assert.Equal(180f, info.HorizontalCoverageDeg, 3);
        Assert.Equal(90f, info.VerticalCoverageDeg, 3);
        Assert.Equal(90f, info.PoseYaw, 4);
        Assert.Equal(-10.5f, info.PosePitch, 4);
        Assert.Equal(3.25f, info.PoseRoll, 4);
        Assert.Equal("Unit", info.MetadataSource);
        Assert.Null(info.LeftMesh);
        Assert.False(info.IsV1);
    }

    [Theory]
    [InlineData(0, StereoMode.Mono, false)]
    [InlineData(1, StereoMode.TopBottom, false)]
    [InlineData(2, StereoMode.SideBySide, false)]
    [InlineData(3, null, false)]
    [InlineData(4, StereoMode.SideBySide, true)]
    public void Maps_st3d_modes(int mode, StereoMode? expected, bool swap)
    {
        var info = SphericalMetadata.Read(File(Moov(Trak("vide", SampleEntry("hvc1", St3d(mode))))));
        Assert.NotNull(info);
        Assert.Equal(expected, info.Stereo);
        Assert.Equal(swap, info.SwapEyes);
        Assert.Equal(SphericalProjection.None, info.Projection);
    }

    [Fact]
    public void Full_sphere_without_bounds_covers_360_by_180()
    {
        var info = SphericalMetadata.Read(File(Moov(Trak("vide", SampleEntry("vp09", Sv3d(Equi(0, 0, 0, 0)))))));
        Assert.NotNull(info);
        Assert.Equal(StereoMode.Mono, info.Stereo);
        Assert.Equal(360f, info.HorizontalCoverageDeg);
        Assert.Equal(180f, info.VerticalCoverageDeg);
    }

    [Fact]
    public void Reads_cubemap()
    {
        var entry = SampleEntry("av01", St3d(1), Sv3d(FullBox("cbmp", 0, U32(0), U32(8))));
        var info = SphericalMetadata.Read(File(Moov(Trak("vide", entry))));
        Assert.NotNull(info);
        Assert.Equal(SphericalProjection.Cubemap, info.Projection);
        Assert.Equal(StereoMode.TopBottom, info.Stereo);
        Assert.Equal(0u, info.CubemapLayout);
        Assert.Equal(8u, info.CubemapPadding);
    }

    [Fact]
    public void Ignores_non_video_tracks_and_finds_the_video_track()
    {
        var audio = Trak("soun", SampleEntry("mp4a", St3d(1))); // nonsense on purpose: must be ignored
        var video = Trak("vide", SampleEntry("avc1", St3d(2), Sv3d(Equi(0, 0, 0.25, 0.25))));
        var info = SphericalMetadata.Read(File(Moov(audio, video)));
        Assert.NotNull(info);
        Assert.Equal(StereoMode.SideBySide, info.Stereo);
    }

    [Fact]
    public void Returns_null_without_metadata()
    {
        Assert.Null(SphericalMetadata.Read(File(Moov(Trak("vide", SampleEntry("avc1"))))));
        Assert.Null(SphericalMetadata.Read(new MemoryStream(Ftyp())));
        Assert.Null(SphericalMetadata.Read(new MemoryStream()));
    }

    [Fact]
    public void Handles_largesize_mdat_before_moov_and_size_zero_last_box()
    {
        var moov = Moov(Trak("vide", SampleEntry("avc1", St3d(1), Sv3d(Equi(0, 0, 0, 0)))));
        // mdat with 64-bit size (size field = 1) followed by moov.
        var mdatPayload = new byte[1000];
        var mdat = Cat(U32(1), Ascii("mdat"), BigEndian64(16 + mdatPayload.Length), mdatPayload);
        var info = SphericalMetadata.Read(new MemoryStream(Cat(Ftyp(), mdat, moov)));
        Assert.Equal(StereoMode.TopBottom, info?.Stereo);

        // faststart layout: moov first, then an mdat with size 0 (extends to the end of the file).
        var info2 = SphericalMetadata.Read(new MemoryStream(Cat(Ftyp(), moov, U32(0), Ascii("mdat"), new byte[500])));
        Assert.Equal(StereoMode.TopBottom, info2?.Stereo);

        static byte[] BigEndian64(long v) { var b = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, v); return b; }
    }

    [Fact]
    public void Skips_huge_mdat_without_reading_it()
    {
        // A stream that claims to be 8 GB: only the box headers and moov are actually read.
        var moov = Moov(Trak("vide", SampleEntry("avc1", St3d(2))));
        long mdatSize = 8L << 30;
        var head = Cat(Ftyp(), U32(1), Ascii("mdat"), I64(mdatSize));
        long moovPos = head.Length - 16 + mdatSize;
        using var s = new SparseStream(moovPos + moov.Length, (0, head), (moovPos, moov));
        var info = SphericalMetadata.Read(s);
        Assert.Equal(StereoMode.SideBySide, info?.Stereo);
        Assert.True(s.BytesRead < 10_000, $"read {s.BytesRead} bytes");

        static byte[] I64(long v) { var b = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, v); return b; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reads_mono_mesh_with_triangles_strip_and_fan(bool deflate)
    {
        var mesh = EncodeMesh(QuadCoords, QuadVertices,
        [
            new VertexList(0, 0, [0, 1, 2]),       // one triangle
            new VertexList(0, 1, [0, 1, 3, 2]),    // strip: (0,1,3) (3,1,2) -> 2 triangles
            new VertexList(0, 2, [0, 1, 2, 3]),    // fan: (0,1,2) (0,2,3)
            new VertexList(1, 0, [3, 2, 1]),       // reserved texture id: skipped
        ]);
        var entry = SampleEntry("avc1", St3d(2), Sv3d(Mshp(deflate, mesh)));
        var info = SphericalMetadata.Read(File(Moov(Trak("vide", entry))));

        Assert.NotNull(info);
        Assert.Equal(SphericalProjection.Mesh, info.Projection);
        Assert.Equal(StereoMode.SideBySide, info.Stereo);
        Assert.NotNull(info.LeftMesh);
        Assert.Null(info.RightMesh);
        var m = info.LeftMesh;
        Assert.Equal(4, m.VertexCount);
        // Texture coordinates are kept as stored (GL convention, v = 0 at the bottom, per the RFC).
        Assert.Equal(ExpectedQuadVertices(QuadCoords), m.Vertices);
        Assert.Equal(new ushort[] { 0, 1, 2, 0, 1, 3, 3, 1, 2, 0, 1, 2, 0, 2, 3 }, m.Indices);
    }

    [Fact]
    public void Reads_stereo_custom_mesh_pair_deflated()
    {
        var left = EncodeMesh(QuadCoords, QuadVertices, [new VertexList(0, 2, [0, 1, 2, 3])]);
        float[] rightCoords = [-2f, 2f, 0f, 0.5f, 1f];
        var right = EncodeMesh(rightCoords, QuadVertices, [new VertexList(0, 0, [0, 1, 2, 0, 2, 3])]);
        var entry = SampleEntry("hvc1", St3d(3), Sv3d(Mshp(true, left, right)));
        var info = SphericalMetadata.Read(File(Moov(Trak("vide", entry))));

        Assert.NotNull(info);
        Assert.Null(info.Stereo);
        Assert.NotNull(info.LeftMesh);
        Assert.NotNull(info.RightMesh);
        Assert.Equal(ExpectedQuadVertices(QuadCoords), info.LeftMesh.Vertices);
        Assert.Equal(ExpectedQuadVertices(rightCoords), info.RightMesh.Vertices);
        Assert.Equal(info.LeftMesh.Indices, info.RightMesh.Indices);
    }

    [Fact]
    public void Reads_mesh_with_rfc_padding_after_each_list()
    {
        // 3 vertices -> vcsb = 3 bits: a list of 3 indices + 16 bits of header leaves the stream unaligned,
        // so padded and unpadded layouts really differ for the second list.
        float[] coords = [0f, 1f];
        int[][] verts = [[0, 0, 0, 0, 0], [1, 0, 0, 1, 0], [0, 1, 0, 0, 1]];
        var mesh = EncodeMesh(coords, verts, [new VertexList(0, 0, [0, 1, 2]), new VertexList(0, 0, [2, 1, 0])], padEachList: true);
        var info = SphericalMetadata.Read(File(Moov(Trak("vide", SampleEntry("avc1", Sv3d(Mshp(false, mesh)))))));
        Assert.Equal(new ushort[] { 0, 1, 2, 2, 1, 0 }, info?.LeftMesh?.Indices);

        var unpadded = EncodeMesh(coords, verts, [new VertexList(0, 0, [0, 1, 2]), new VertexList(0, 0, [2, 1, 0])]);
        info = SphericalMetadata.Read(File(Moov(Trak("vide", SampleEntry("avc1", Sv3d(Mshp(false, unpadded)))))));
        Assert.Equal(new ushort[] { 0, 1, 2, 2, 1, 0 }, info?.LeftMesh?.Indices);
    }

    [Fact]
    public void Drops_mesh_with_too_many_vertices()
    {
        float[] coords = [0f, 1f];
        var verts = Enumerable.Range(0, 65536).Select(i => new[] { i & 1, 0, 0, 0, 0 }).ToArray();
        var mesh = EncodeMesh(coords, verts, [new VertexList(0, 0, [0, 1, 2])]);
        var info = SphericalMetadata.Read(File(Moov(Trak("vide", SampleEntry("avc1", Sv3d(Mshp(true, mesh)))))));
        Assert.NotNull(info);
        Assert.Equal(SphericalProjection.Mesh, info.Projection);
        Assert.Null(info.LeftMesh);
    }

    [Fact]
    public void Rejects_out_of_range_mesh_indices()
    {
        var mesh = EncodeMesh(QuadCoords, QuadVertices, [new VertexList(0, 0, [0, 1, 4])]);
        var info = SphericalMetadata.Read(File(Moov(Trak("vide", SampleEntry("avc1", Sv3d(Mshp(false, mesh)))))));
        Assert.Equal(SphericalProjection.Mesh, info?.Projection);
        Assert.Null(info?.LeftMesh);
    }

    [Fact]
    public void Scale_multiplies_positions_only()
    {
        var m = new Mesh { Vertices = [1f, 2f, -3f, 0.25f, 0.75f], Indices = [0] };
        var s = SphericalMetadata.Scale(m, 10f);
        Assert.Equal(new[] { 10f, 20f, -30f, 0.25f, 0.75f }, s.Vertices);
        Assert.Equal(new[] { 1f, 2f, -3f, 0.25f, 0.75f }, m.Vertices);
    }

    [Fact]
    public void Reads_v1_spherical_xml()
    {
        const string xml = """
            <?xml version="1.0"?><rdf:SphericalVideo xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:GSpherical="http://ns.google.com/videos/1.0/spherical/">
            <GSpherical:Spherical>true</GSpherical:Spherical>
            <GSpherical:Stitched>true</GSpherical:Stitched>
            <GSpherical:StitchingSoftware>Spherical Metadata Tool</GSpherical:StitchingSoftware>
            <GSpherical:ProjectionType>equirectangular</GSpherical:ProjectionType>
            <GSpherical:StereoMode>top-bottom</GSpherical:StereoMode>
            <GSpherical:CroppedAreaImageWidthPixels>2048</GSpherical:CroppedAreaImageWidthPixels>
            <GSpherical:CroppedAreaImageHeightPixels>2048</GSpherical:CroppedAreaImageHeightPixels>
            <GSpherical:FullPanoWidthPixels>4096</GSpherical:FullPanoWidthPixels>
            <GSpherical:FullPanoHeightPixels>2048</GSpherical:FullPanoHeightPixels>
            <GSpherical:CroppedAreaLeftPixels>1024</GSpherical:CroppedAreaLeftPixels>
            <GSpherical:CroppedAreaTopPixels>0</GSpherical:CroppedAreaTopPixels>
            </rdf:SphericalVideo>
            """;
        byte[] uuid = [0xff, 0xcc, 0x82, 0x63, 0xf8, 0x55, 0x4a, 0x93, 0x88, 0x14, 0x58, 0x7a, 0x02, 0x52, 0x1f, 0xdd];
        var trak = Trak("vide", SampleEntry("avc1"), Box("uuid", uuid, Encoding.UTF8.GetBytes(xml)));
        var info = SphericalMetadata.Read(File(Moov(trak)));

        Assert.NotNull(info);
        Assert.True(info.IsV1);
        Assert.Equal(SphericalProjection.Equirect, info.Projection);
        Assert.Equal(StereoMode.TopBottom, info.Stereo);
        Assert.Equal(180f, info.HorizontalCoverageDeg, 3);
        Assert.Equal(180f, info.VerticalCoverageDeg, 3);
        Assert.Equal(0.25f, info.BoundsLeft, 5);
        Assert.Equal("Spherical Metadata Tool", info.MetadataSource);

        // V2 wins over V1 when both are present.
        var both = Trak("vide", SampleEntry("avc1", St3d(2)), Box("uuid", uuid, Encoding.UTF8.GetBytes(xml)));
        Assert.Equal(StereoMode.SideBySide, SphericalMetadata.Read(File(Moov(both)))?.Stereo);
    }

    [Fact]
    public void Never_throws_on_truncated_or_corrupted_input()
    {
        var mesh = EncodeMesh(QuadCoords, QuadVertices, [new VertexList(0, 1, [0, 1, 3, 2])]);
        var good = File(Moov(Trak("vide", SampleEntry("avc1", St3d(2), Sv3d(Mshp(true, mesh)))))).ToArray();
        for (int len = 0; len < good.Length; len++)
            SphericalMetadata.Read(new MemoryStream(good, 0, len));

        var rng = new Random(1234);
        for (int iter = 0; iter < 3000; iter++)
        {
            var bad = (byte[])good.Clone();
            for (int k = 0; k < 1 + rng.Next(6); k++)
                bad[rng.Next(bad.Length)] = (byte)rng.Next(256);
            SphericalMetadata.Read(new MemoryStream(bad));
        }
        var noise = new byte[4096];
        rng.NextBytes(noise);
        SphericalMetadata.Read(new MemoryStream(noise));
        // Boxes claiming sizes smaller than their header or far beyond the end.
        SphericalMetadata.Read(new MemoryStream(Cat(U32(3), Ascii("moov"), new byte[32])));
        SphericalMetadata.Read(new MemoryStream(Cat(U32(0xFFFFFFF0), Ascii("moov"), Ascii("dddd"))));
        SphericalMetadata.Read(new MemoryStream(Cat(U32(1), Ascii("moov"), U32(0xFFFFFFFF), U32(0xFFFFFFFF))));
    }

    // ------------------------------------------------------------------ real files written by ffmpeg

    /// <summary>
    /// Fixtures made with ffmpeg 6.1: a 64x32 one-frame H.264 clip, the synthetic boxes of these
    /// tests injected into its moov, then remuxed with <c>ffmpeg -c copy -strict unofficial</c>, so
    /// the st3d/sv3d boxes in the fixtures were written by ffmpeg's own mov muxer.
    /// </summary>
    [Theory]
    [InlineData("ffmpeg_sv3d_equi_lr.mp4")]           // moov after mdat
    [InlineData("ffmpeg_sv3d_equi_lr_faststart.mp4")] // moov before mdat
    public void Reads_files_written_by_ffmpeg(string name)
    {
        using var f = System.IO.File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        var info = SphericalMetadata.Read(f);
        Assert.NotNull(info);
        Assert.Equal(StereoMode.SideBySide, info.Stereo);
        Assert.Equal(SphericalProjection.Equirect, info.Projection);
        Assert.Equal(180f, info.HorizontalCoverageDeg, 2);
        Assert.Equal(180f, info.VerticalCoverageDeg, 2);
        Assert.Equal(30f, info.PoseYaw, 3);
        Assert.Equal(-5f, info.PosePitch, 3);
        Assert.Equal(2f, info.PoseRoll, 3);
        Assert.Equal("Lavf", info.MetadataSource);
    }

    /// <summary>Read-only stream of a given length that is zero everywhere except a few chunks.</summary>
    private sealed class SparseStream(long length, params (long Pos, byte[] Data)[] chunks) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Max(0, Math.Min(count, length - Position));
            Array.Clear(buffer, offset, n);
            foreach (var (pos, data) in chunks)
            {
                long s = Math.Max(pos, Position), e = Math.Min(pos + data.Length, Position + n);
                for (long i = s; i < e; i++) buffer[offset + (i - Position)] = data[i - pos];
            }
            Position += n;
            BytesRead += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => length + offset,
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
