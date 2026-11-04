using System.Numerics;

namespace VrView.Core;

/// <summary>
/// Cube seen from inside for 3x2 cubemap frames, either plain (ffmpeg v360 <c>c3x2</c>) or
/// equi-angular (YouTube EAC, ffmpeg v360 <c>eac</c>). Layouts, verified against ffmpeg output
/// (viewer looks down -Z, +Y up, +X right; "row 0" is the top half of the picture):
/// <code>
///          col 0          col 1          col 2
/// EAC  0   left           front          right           (upright)
///      1   down (rot)     back (rot)     up (rot)        (image up = world +X)
/// c3x2 0   right          left           up              (image up = world +Z / back)
///      1   down           front          back            (down: image up = world -Z / front)
/// </code>
/// ffmpeg pads real EAC frames with 2 pixels at the outer frame borders and at the top and bottom
/// of each row; at video resolutions that is ~0.1° and is ignored here. UVs are inset by
/// <see cref="UvInset"/> inside each face cell so linear filtering at the face borders does not
/// pick texels of the neighboring cell (the true half-texel inset depends on the resolution).
/// </summary>
public static class CubemapMesh
{
    /// <summary>Inset of each face's UVs inside its cell of the frame (in UV units).</summary>
    public const float UvInset = 0.0005f;

    /// <summary>A face: outward normal, world directions of the picture's right and up, cell in the frame.</summary>
    private readonly record struct Face(Vector3 Normal, Vector3 Right, Vector3 Up, int Col, int Row);

    private static readonly Face[] Eac =
    {
        new(-Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY, 0, 0),
        new(-Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 1, 0),
        new(Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, 2, 0),
        new(-Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX, 0, 1),
        new(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, 1, 1),
        new(Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitX, 2, 1),
    };

    private static readonly Face[] C3x2 =
    {
        new(Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, 0, 0),
        new(-Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY, 1, 0),
        new(Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 2, 0),
        new(-Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ, 0, 1),
        new(-Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 1, 1),
        new(Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY, 2, 1),
    };

    /// <summary>
    /// Six independent grids (one per face, so seams never share UVs), vertices on the sphere of
    /// <paramref name="radius"/>. Grid lines are uniform in texture space.
    /// </summary>
    public static Mesh Build(float radius, bool equiAngular, int segmentsPerFace = 32)
    {
        int n = segmentsPerFace + 1;
        if (segmentsPerFace < 1 || 6 * n * n > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(segmentsPerFace));
        var v = new float[6 * n * n * Mesh.FloatsPerVertex];
        var idx = new ushort[6 * segmentsPerFace * segmentsPerFace * 6];
        int k = 0, q = 0;
        foreach (var f in equiAngular ? Eac : C3x2)
        {
            int baseVertex = k / Mesh.FloatsPerVertex;
            for (int r = 0; r < n; r++)
            {
                float b = 2f * r / segmentsPerFace - 1f;
                for (int c = 0; c < n; c++)
                {
                    float a = 2f * c / segmentsPerFace - 1f;
                    var p = Vector3.Normalize(f.Normal + Unwarp(a, equiAngular) * f.Right + Unwarp(b, equiAngular) * f.Up) * radius;
                    var (u, vv) = CellUv(f, a, b);
                    v[k++] = p.X; v[k++] = p.Y; v[k++] = p.Z; v[k++] = u; v[k++] = vv;
                }
            }
            // Same winding as MeshBuilder's grids: columns go right, rows go up as seen from inside.
            for (int r = 0; r < segmentsPerFace; r++)
                for (int c = 0; c < segmentsPerFace; c++)
                {
                    int i0 = baseVertex + r * n + c, i1 = i0 + 1, i2 = i0 + n, i3 = i2 + 1;
                    idx[q++] = (ushort)i0; idx[q++] = (ushort)i2; idx[q++] = (ushort)i1;
                    idx[q++] = (ushort)i1; idx[q++] = (ushort)i2; idx[q++] = (ushort)i3;
                }
        }
        return new Mesh { Vertices = v, Indices = idx };
    }

    /// <summary>Texture coordinate (GL convention, v = 0 at the bottom) where a direction is found in the frame.</summary>
    public static (float U, float V) DirectionToUv(Vector3 dir, bool equiAngular)
    {
        var faces = equiAngular ? Eac : C3x2;
        var f = faces[0];
        float best = float.NegativeInfinity;
        foreach (var face in faces)
        {
            float d = Vector3.Dot(dir, face.Normal);
            if (d > best) { best = d; f = face; }
        }
        float a = Vector3.Dot(dir, f.Right) / best, b = Vector3.Dot(dir, f.Up) / best;
        return CellUv(f, Warp(a, equiAngular), Warp(b, equiAngular));
    }

    /// <summary>Face plane coordinate (-1..1) to texture coordinate (-1..1).</summary>
    private static float Warp(float x, bool equiAngular) => equiAngular ? 4f / MathF.PI * MathF.Atan(x) : x;

    private static float Unwarp(float t, bool equiAngular) => equiAngular ? MathF.Tan(t * MathF.PI / 4f) : t;

    /// <summary>Texture coordinates -1..1 inside a face to frame UVs, inset inside the face's cell.</summary>
    private static (float U, float V) CellUv(Face f, float a, float b)
    {
        float u = f.Col / 3f + UvInset + (a + 1f) / 2f * (1f / 3f - 2f * UvInset);
        float v = (1 - f.Row) / 2f + UvInset + (b + 1f) / 2f * (0.5f - 2f * UvInset);
        return (u, v);
    }
}
