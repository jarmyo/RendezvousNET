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
    /// full spheres and 180 for VR180; latitude always spans -90..90.
    /// </summary>
    public static Mesh Equirect(float radius, float lonSpanDeg, int lonSegments = 96, int latSegments = 48)
    {
        int cols = lonSegments + 1, rows = latSegments + 1;
        var v = new float[cols * rows * Mesh.FloatsPerVertex];
        int k = 0;
        float lonMin = -lonSpanDeg / 2f * VrMath.Deg2Rad, lonSpan = lonSpanDeg * VrMath.Deg2Rad;
        for (int r = 0; r < rows; r++)
        {
            float fv = (float)r / latSegments;
            float lat = (fv - 0.5f) * MathF.PI;
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
    /// Equidistant fisheye: a polar grid around the front direction, where the angle from the
    /// center is proportional to the distance from the image center.
    /// </summary>
    public static Mesh Fisheye(float radius, float fovDeg, int rings = 48, int segments = 96)
    {
        float maxTheta = fovDeg / 2f * VrMath.Deg2Rad;
        int cols = segments + 1, rowCount = rings + 1;
        var v = new float[cols * rowCount * Mesh.FloatsPerVertex];
        int k = 0;
        for (int r = 0; r < rowCount; r++)
        {
            float fr = (float)r / rings;
            float theta = fr * maxTheta;
            for (int c = 0; c < cols; c++)
            {
                float phi = (float)c / segments * MathF.Tau;
                float s = MathF.Sin(theta);
                v[k++] = radius * s * MathF.Cos(phi);
                v[k++] = radius * s * MathF.Sin(phi);
                v[k++] = -radius * MathF.Cos(theta);
                v[k++] = 0.5f + 0.5f * fr * MathF.Cos(phi);
                v[k++] = 0.5f + 0.5f * fr * MathF.Sin(phi);
            }
        }
        return new Mesh { Vertices = v, Indices = GridIndices(cols, rowCount) };
    }

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
