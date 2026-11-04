using System.Numerics;
using System.Text;
using VrView.Core;

namespace VrView.Core.Tests;

public class CubemapMeshTests
{
    // Fixtures: a synthetic 2048x1024 equirect where R = cos(lon), B = sin(lon), G = lat (each
    // mapped to 0..255; lon = 0 at the center, positive to the right; lat = +90 at the top),
    // converted by ffmpeg 6.1 at full resolution and downscaled (so ffmpeg's 2-pixel EAC padding
    // becomes negligible):
    //   ffmpeg -i equi.ppm -vf v360=input=e:output=eac:w=3072:h=2048,scale=192:128:flags=area cubemap_eac.ppm
    //   ffmpeg -i equi.ppm -vf v360=input=e:output=c3x2:w=3072:h=2048,scale=192:128:flags=area cubemap_c3x2.ppm
    [Theory]
    [InlineData("cubemap_eac.ppm", true)]
    [InlineData("cubemap_c3x2.ppm", false)]
    public void DirectionToUv_matches_ffmpeg_v360_output(string fixture, bool equiAngular)
    {
        var img = Ppm.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var rnd = new Random(1);
        var errors = new List<float>();
        for (int i = 0; i < 4000; i++)
        {
            var dir = RandomDirection(rnd);
            var (u, v) = CubemapMesh.DirectionToUv(dir, equiAngular);
            var c = img.SampleInCell(u, v);
            float lon = MathF.Atan2(c.Z / 255f * 2f - 1f, c.X / 255f * 2f - 1f);
            float lat = (c.Y / 255f - 0.5f) * MathF.PI;
            var seen = new Vector3(MathF.Cos(lat) * MathF.Sin(lon), MathF.Sin(lat), -MathF.Cos(lat) * MathF.Cos(lon));
            errors.Add(AngleDeg(dir, seen));
        }
        errors.Sort();
        Assert.True(errors[errors.Count / 2] < 0.4f, $"median error {errors[errors.Count / 2]:F2}°");
        Assert.True(errors[^1] < 1.5f, $"max error {errors[^1]:F2}°");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Face_centers_land_in_the_expected_cells(bool equiAngular)
    {
        // (direction, col, row from the top)
        var expected = equiAngular
            ? new[] { (-Vector3.UnitX, 0, 0), (-Vector3.UnitZ, 1, 0), (Vector3.UnitX, 2, 0), (-Vector3.UnitY, 0, 1), (Vector3.UnitZ, 1, 1), (Vector3.UnitY, 2, 1) }
            : new[] { (Vector3.UnitX, 0, 0), (-Vector3.UnitX, 1, 0), (Vector3.UnitY, 2, 0), (-Vector3.UnitY, 0, 1), (-Vector3.UnitZ, 1, 1), (Vector3.UnitZ, 2, 1) };
        foreach (var (dir, col, row) in expected)
        {
            var (u, v) = CubemapMesh.DirectionToUv(dir, equiAngular);
            Assert.Equal((col + 0.5f) / 3f, u, 4);
            Assert.Equal(0.75f - 0.5f * row, v, 4);
        }
    }

    [Theory]
    [InlineData(true, 32)]
    [InlineData(false, 32)]
    [InlineData(true, 103)]
    public void Build_vertices_agree_with_DirectionToUv(bool equiAngular, int segments)
    {
        const float radius = 10f;
        var mesh = CubemapMesh.Build(radius, equiAngular, segments);
        Assert.Equal(6 * (segments + 1) * (segments + 1), mesh.VertexCount);
        Assert.True(mesh.VertexCount <= ushort.MaxValue);
        var vs = mesh.Vertices;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            int o = i * Mesh.FloatsPerVertex;
            var p = new Vector3(vs[o], vs[o + 1], vs[o + 2]);
            Assert.Equal(radius, p.Length(), 1e-3f);
            // Vertices on a cube edge belong to two faces: check them against their own cell.
            var (u, v) = UvInCellOf(p, vs[o + 3], vs[o + 4], equiAngular);
            Assert.Equal(u, vs[o + 3], 1e-4f);
            Assert.Equal(v, vs[o + 4], 1e-4f);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_faces_inward_and_keeps_uvs_inside_their_cell(bool equiAngular)
    {
        var mesh = CubemapMesh.Build(1f, equiAngular, 8);
        var vs = mesh.Vertices;
        Vector3 P(int i) => new(vs[i * 5], vs[i * 5 + 1], vs[i * 5 + 2]);
        Vector2 T(int i) => new(vs[i * 5 + 3], vs[i * 5 + 4]);
        for (int t = 0; t < mesh.Indices.Length; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            // Same winding as MeshBuilder.Equirect: the normal (b-a)x(c-a) points away from the center.
            var n = Vector3.Cross(P(b) - P(a), P(c) - P(a));
            Assert.True(Vector3.Dot(n, P(a) + P(b) + P(c)) > 0);
            // All three UVs in one cell, never touching its border.
            var cell = Cell(T(a));
            Assert.Equal(cell, Cell(T(b)));
            Assert.Equal(cell, Cell(T(c)));
            foreach (var uv in new[] { T(a), T(b), T(c) })
            {
                Assert.InRange(uv.X * 3f - cell.Col, CubemapMesh.UvInset * 3f * 0.99f, 1f - CubemapMesh.UvInset * 3f * 0.99f);
                Assert.InRange(uv.Y * 2f - cell.Row, CubemapMesh.UvInset * 2f * 0.99f, 1f - CubemapMesh.UvInset * 2f * 0.99f);
            }
        }
    }

    [Fact]
    public void Build_rejects_meshes_too_dense_for_16_bit_indices() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CubemapMesh.Build(1f, true, 104));

    private static (int Col, int Row) Cell(Vector2 uv) => ((int)(uv.X * 3f), (int)(uv.Y * 2f));

    /// <summary>DirectionToUv of a point nudged toward the center of the face that owns <paramref name="u"/>, <paramref name="v"/>.</summary>
    private static (float U, float V) UvInCellOf(Vector3 p, float u, float v, bool equiAngular)
    {
        var cell = Cell(new Vector2(u, v));
        Vector3 center = default;
        foreach (var axis in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
            if (Cell(ToVector(CubemapMesh.DirectionToUv(axis, equiAngular))) == cell) center = axis;
        return CubemapMesh.DirectionToUv(Vector3.Normalize(p) + center * 1e-5f, equiAngular);
    }

    private static Vector2 ToVector((float U, float V) uv) => new(uv.U, uv.V);

    private static Vector3 RandomDirection(Random rnd)
    {
        while (true)
        {
            var d = new Vector3(rnd.NextSingle() * 2 - 1, rnd.NextSingle() * 2 - 1, rnd.NextSingle() * 2 - 1);
            float l = d.Length();
            if (l is > 0.1f and <= 1f) return d / l;
        }
    }

    private static float AngleDeg(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * VrMath.Rad2Deg;

    /// <summary>Minimal binary PPM (P6, maxval 255) reader.</summary>
    private sealed class Ppm(int width, int height, byte[] rgb)
    {
        public static Ppm Load(string path)
        {
            var data = File.ReadAllBytes(path);
            int pos = 0;
            string Token()
            {
                while (char.IsWhiteSpace((char)data[pos])) pos++;
                int start = pos;
                while (!char.IsWhiteSpace((char)data[pos])) pos++;
                return Encoding.ASCII.GetString(data, start, pos - start);
            }
            Assert.Equal("P6", Token());
            int w = int.Parse(Token()), h = int.Parse(Token());
            Assert.Equal("255", Token());
            return new Ppm(w, h, data[(pos + 1)..]);
        }

        /// <summary>Bilinear sample at a GL texture coordinate, clamped to the 3x2 cell it falls in.</summary>
        public Vector3 SampleInCell(float u, float v)
        {
            int cw = width / 3, ch = height / 2;
            float x = u * width - 0.5f, y = (1f - v) * height - 0.5f;
            int col = Math.Clamp((int)(u * 3f), 0, 2), row = Math.Clamp((int)((1f - v) * 2f), 0, 1);
            x = Math.Clamp(x, col * cw, (col + 1) * cw - 1);
            y = Math.Clamp(y, row * ch, (row + 1) * ch - 1);
            int x0 = Math.Min((int)x, (col + 1) * cw - 2), y0 = Math.Min((int)y, (row + 1) * ch - 2);
            float fx = x - x0, fy = y - y0;
            return Vector3.Lerp(Vector3.Lerp(Pixel(x0, y0), Pixel(x0 + 1, y0), fx),
                                Vector3.Lerp(Pixel(x0, y0 + 1), Pixel(x0 + 1, y0 + 1), fx), fy);
        }

        private Vector3 Pixel(int x, int y)
        {
            int o = (y * width + x) * 3;
            return new Vector3(rgb[o], rgb[o + 1], rgb[o + 2]);
        }
    }
}
