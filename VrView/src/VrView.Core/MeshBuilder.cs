namespace VrView.Core;

/// <summary>Interleaved triangle mesh: 5 floats per vertex (x, y, z, u, v), 16-bit indices.</summary>
public sealed class Mesh
{
    public const int FloatsPerVertex = 5;
    public required float[] Vertices { get; init; }
    public required ushort[] Indices { get; init; }
    public int VertexCount => Vertices.Length / FloatsPerVertex;
}

/// <summary>
/// Generates the geometry the video is projected on. Texture coordinates use GL convention
/// (v = 0 at the bottom of the picture); they are later remapped per eye and by the
/// SurfaceTexture transform in the shader.
/// </summary>
public static class MeshBuilder
{
    /// <summary>
    /// Equirectangular sphere section seen from inside. <paramref name="lonSpanDeg"/> is 360 for
    /// full spheres and 180 for VR180; <paramref name="latSpanDeg"/> is normally 180 (pole to pole)
    /// but some cameras cover less (or more horizontally), which the fine tuning adjusts.
    /// </summary>
    public static Mesh Equirect(float radius, float lonSpanDeg, int lonSegments = 96, int latSegments = 48, float latSpanDeg = 180f)
    {
        int cols = lonSegments + 1, rows = latSegments + 1;
        var v = new float[cols * rows * Mesh.FloatsPerVertex];
        int k = 0;
        float lonMin = -lonSpanDeg / 2f * VrMath.Deg2Rad, lonSpan = lonSpanDeg * VrMath.Deg2Rad;
        for (int r = 0; r < rows; r++)
        {
            float fv = (float)r / latSegments;
            float lat = (fv - 0.5f) * latSpanDeg * VrMath.Deg2Rad;
            for (int c = 0; c < cols; c++)
            {
                float fu = (float)c / lonSegments;
                float lon = lonMin + fu * lonSpan;
                v[k++] = radius * MathF.Cos(lat) * MathF.Sin(lon);
                v[k++] = radius * MathF.Sin(lat);
                v[k++] = -radius * MathF.Cos(lat) * MathF.Cos(lon);
                v[k++] = fu;
                v[k++] = fv;
            }
        }
        return new Mesh { Vertices = v, Indices = GridIndices(cols, rows) };
    }

    /// <summary>
    /// Fisheye: a polar grid around the front direction. The distance from the circle center grows
    /// with the angle from the lens axis according to <paramref name="lens"/>. The circle can be
    /// off-center (<paramref name="centerX"/>, <paramref name="centerY"/>, fractions of the frame)
    /// and smaller or bigger than the frame (<paramref name="radiusScale"/>).
    /// </summary>
    public static Mesh Fisheye(float radius, float fovDeg, int rings = 48, int segments = 96,
        FisheyeLens lens = FisheyeLens.Equidistant, float centerX = 0f, float centerY = 0f, float radiusScale = 1f)
    {
        float maxTheta = fovDeg / 2f * VrMath.Deg2Rad;
        float rMax = LensRadius(lens, maxTheta);
        int cols = segments + 1, rowCount = rings + 1;
        var v = new float[cols * rowCount * Mesh.FloatsPerVertex];
        int k = 0;
        for (int r = 0; r < rowCount; r++)
        {
            float fr = (float)r / rings;
            float theta = fr * maxTheta;
            // Distance from the circle center, 0..1 at the edge of the lens' field of view.
            float rr = LensRadius(lens, theta) / rMax * radiusScale;
            for (int c = 0; c < cols; c++)
            {
                float phi = (float)c / segments * MathF.Tau;
                float s = MathF.Sin(theta);
                v[k++] = radius * s * MathF.Cos(phi);
                v[k++] = radius * s * MathF.Sin(phi);
                v[k++] = -radius * MathF.Cos(theta);
                // Clamp so a bigger circle never samples the other eye's half of the frame.
                v[k++] = Math.Clamp(0.5f + centerX + 0.5f * rr * MathF.Cos(phi), 0f, 1f);
                v[k++] = Math.Clamp(0.5f + centerY + 0.5f * rr * MathF.Sin(phi), 0f, 1f);
            }
        }
        return new Mesh { Vertices = v, Indices = GridIndices(cols, rowCount) };
    }

    /// <summary>Image-plane distance for an angle θ from the lens axis (unnormalized).</summary>
    public static float LensRadius(FisheyeLens lens, float theta) => lens switch
    {
        FisheyeLens.Equisolid => 2f * MathF.Sin(theta / 2f),
        FisheyeLens.Stereographic => 2f * MathF.Tan(theta / 2f),
        _ => theta,
    };

    /// <summary>
    /// Disc (spherical cap) of angular radius <paramref name="angleDeg"/> around the -Z axis, with
    /// polar texture coordinates (0.5, 0.5 at the center, radius 0.5 at the rim). Rotated to point
    /// down, it hides the nadir.
    /// </summary>
    public static Mesh Cap(float radius, float angleDeg, int rings = 8, int segments = 48) =>
        Fisheye(radius, angleDeg * 2f, rings, segments);

    /// <summary>Flat rectangle centered on the -Z axis at <paramref name="distance"/>.</summary>
    public static Mesh Quad(float width, float height, float distance)
    {
        float x = width / 2f, y = height / 2f, z = -distance;
        return new Mesh
        {
            Vertices = new[]
            {
                -x, -y, z, 0f, 0f,
                 x, -y, z, 1f, 0f,
                 x,  y, z, 1f, 1f,
                -x,  y, z, 0f, 1f,
            },
            Indices = new ushort[] { 0, 1, 2, 0, 2, 3 },
        };
    }

    private static ushort[] GridIndices(int cols, int rows)
    {
        if (cols * rows > ushort.MaxValue) throw new ArgumentException("Mesh too dense for 16-bit indices.");
        var idx = new ushort[(cols - 1) * (rows - 1) * 6];
        int k = 0;
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                idx[k++] = (ushort)a; idx[k++] = (ushort)d; idx[k++] = (ushort)b;
                idx[k++] = (ushort)b; idx[k++] = (ushort)d; idx[k++] = (ushort)e;
            }
        return idx;
    }
}
