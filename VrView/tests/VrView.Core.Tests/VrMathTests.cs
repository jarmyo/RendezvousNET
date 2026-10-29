using System.Numerics;
using VrView.Core;

namespace VrView.Core.Tests;

public class VrMathTests
{
    private const float Eps = 1e-4f;

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"expected {expected}, got {actual}");
    }

    /// <summary>Builds Android's row-major device→world matrix from the world images of the device axes.</summary>
    private static float[] R(Vector3 devX, Vector3 devY, Vector3 devZ) => new[]
    {
        devX.X, devY.X, devZ.X,
        devX.Y, devY.Y, devZ.Y,
        devX.Z, devY.Z, devZ.Z,
    };

    private static readonly Vector3 East = Vector3.UnitX, North = Vector3.UnitY, Up = Vector3.UnitZ;

    [Fact]
    public void Landscape_phone_looking_north_sees_scene_front_with_up_up()
    {
        // ROTATION_90: phone in landscape, top edge pointing west, screen facing south (towards the user).
        var r = R(devX: Up, devY: -East, devZ: -North);
        var view = VrMath.HeadView(r, 1);
        Near(new Vector3(0, 0, -1), VrMath.Forward(view));
        Near(new Vector3(0, 1, 0), VrMath.Up(view));
    }

    [Fact]
    public void Reverse_landscape_is_handled()
    {
        // ROTATION_270: top edge pointing east, screen facing south.
        var r = R(devX: -Up, devY: East, devZ: -North);
        var view = VrMath.HeadView(r, 3);
        Near(new Vector3(0, 0, -1), VrMath.Forward(view));
        Near(new Vector3(0, 1, 0), VrMath.Up(view));
    }

    [Fact]
    public void Portrait_phone_held_upright_looking_north()
    {
        var r = R(devX: East, devY: Up, devZ: -North);
        var view = VrMath.HeadView(r, 0);
        Near(new Vector3(0, 0, -1), VrMath.Forward(view));
        Near(new Vector3(0, 1, 0), VrMath.Up(view));
    }

    [Fact]
    public void Turning_head_right_looks_at_scene_right()
    {
        // Landscape, looking east: device -Z (back camera) points east, so devZ = -East; top edge north.
        var r = R(devX: Up, devY: North, devZ: -East);
        var view = VrMath.HeadView(r, 1);
        Near(new Vector3(1, 0, 0), VrMath.Forward(view));
    }

    [Fact]
    public void Looking_down_gives_negative_pitch()
    {
        // Landscape, tilted 90° down: back camera points at the floor, top edge west, screen up.
        var r = R(devX: North, devY: -East, devZ: Up);
        var view = VrMath.HeadView(r, 1);
        Near(new Vector3(0, -1, 0), VrMath.Forward(view));
        Assert.Equal(-MathF.PI / 2, VrMath.PitchOf(VrMath.Forward(view)), 3);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(-120f)]
    [InlineData(179f)]
    public void Recenter_yaw_brings_scene_front_to_gaze(float headingDeg)
    {
        float a = headingDeg * VrMath.Deg2Rad;
        var forward = new Vector3(MathF.Sin(a), 0.3f, -MathF.Cos(a));
        float yaw = VrMath.YawOf(forward);
        var front = Vector3.TransformNormal(-Vector3.UnitZ, Matrix4x4.CreateRotationY(yaw));
        var horiz = Vector3.Normalize(forward with { Y = 0 });
        Near(horiz, front);
    }

    [Fact]
    public void Video_model_roll_tilts_horizon()
    {
        var m = VrMath.VideoModel(0, 10, 0, 0);
        var right = Vector3.TransformNormal(Vector3.UnitX, m);
        Assert.True(right.Y > 0.17f && right.Y < 0.18f); // sin(10°)
    }

    [Fact]
    public void ToGl_matches_numerics_transform()
    {
        var m = Matrix4x4.CreateRotationY(0.3f) * Matrix4x4.CreateTranslation(1, 2, 3);
        var gl = new float[16];
        VrMath.ToGl(m, gl);
        // Column-major multiply as GL would do it: out = M * v.
        var v = new Vector4(0.5f, -1f, 2f, 1f);
        var expected = Vector4.Transform(v, m);
        float x = gl[0] * v.X + gl[4] * v.Y + gl[8] * v.Z + gl[12] * v.W;
        float y = gl[1] * v.X + gl[5] * v.Y + gl[9] * v.Z + gl[13] * v.W;
        float z = gl[2] * v.X + gl[6] * v.Y + gl[10] * v.Z + gl[14] * v.W;
        Assert.Equal(expected.X, x, 4);
        Assert.Equal(expected.Y, y, 4);
        Assert.Equal(expected.Z, z, 4);
    }

    [Fact]
    public void Projection_shift_moves_center_horizontally()
    {
        var p = VrMath.Projection(90, 1, 0.1f, 100, 0.1f);
        var clip = Vector4.Transform(new Vector4(0, 0, -5, 1), p);
        Assert.Equal(0.1f, clip.X / clip.W, 4);
        Assert.Equal(0f, clip.Y / clip.W, 4);
    }

    [Fact]
    public void Panel_hit_test_center_and_edges()
    {
        float yaw = 0.5f, pitch = -0.3f, d = 2f, w = 3.2f, h = 1f;
        var model = VrMath.PanelModel(yaw, pitch, d);
        var center = Vector3.Transform(Vector3.Zero, model);
        var hit = VrMath.PanelHit(Vector3.Normalize(center), yaw, pitch, d, w, h);
        Assert.NotNull(hit);
        Assert.Equal(0.5f, hit!.Value.X, 3);
        Assert.Equal(0.5f, hit.Value.Y, 3);

        // Top-left corner of the panel in its local frame is (-w/2, +h/2, 0) before the push-out.
        var corner = Vector3.Transform(new Vector3(-w / 2, h / 2, 0), model);
        var hc = VrMath.PanelHit(Vector3.Normalize(corner), yaw, pitch, d, w, h)!.Value;
        Assert.Equal(0f, hc.X, 3);
        Assert.Equal(0f, hc.Y, 3);

        // Looking backwards never hits.
        Assert.Null(VrMath.PanelHit(-Vector3.Normalize(center), yaw, pitch, d, w, h));
    }

    [Fact]
    public void Equirect_mesh_has_expected_shape()
    {
        var m = MeshBuilder.Equirect(10, 180, 8, 4);
        Assert.Equal(9 * 5, m.VertexCount);
        Assert.Equal(8 * 4 * 6, m.Indices.Length);
        Assert.All(m.Indices, i => Assert.True(i < m.VertexCount));
        // Middle vertex of the middle row is straight ahead (-Z) with UV (0.5, 0.5).
        int mid = (2 * 9 + 4) * Mesh.FloatsPerVertex;
        Near(new Vector3(0, 0, -10), new Vector3(m.Vertices[mid], m.Vertices[mid + 1], m.Vertices[mid + 2]));
        Assert.Equal(0.5f, m.Vertices[mid + 3], 4);
        Assert.Equal(0.5f, m.Vertices[mid + 4], 4);
        // Right edge (u = 1) of a 180 dome points to +X.
        int right = (2 * 9 + 8) * Mesh.FloatsPerVertex;
        Near(new Vector3(10, 0, 0), new Vector3(m.Vertices[right], m.Vertices[right + 1], m.Vertices[right + 2]));
    }

    [Fact]
    public void Fisheye_mesh_center_and_rim()
    {
        var m = MeshBuilder.Fisheye(1, 180, 4, 8);
        // First ring is the center: straight ahead, UV center.
        Near(new Vector3(0, 0, -1), new Vector3(m.Vertices[0], m.Vertices[1], m.Vertices[2]));
        Assert.Equal(0.5f, m.Vertices[3], 4);
        // Outer ring at phi = 0: 90° to the right, UV at the right edge.
        int rim = (4 * 9) * Mesh.FloatsPerVertex;
        Near(new Vector3(1, 0, 0), new Vector3(m.Vertices[rim], m.Vertices[rim + 1], m.Vertices[rim + 2]));
        Assert.Equal(1f, m.Vertices[rim + 3], 4);
        Assert.Equal(0.5f, m.Vertices[rim + 4], 4);
    }
}
