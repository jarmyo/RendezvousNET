using System.Numerics;
using VrView.Core;

namespace VrView.Core.Tests;

public class FineTuningTests
{
    private static readonly VideoFormat Vr180 = new(Projection.Dome180, StereoMode.SideBySide);
    private static readonly VideoFormat Fisheye = new(Projection.Fisheye, StereoMode.SideBySide);

    [Fact]
    public void Wider_180_coverage_is_stored_and_back_to_standard_is_zero()
    {
        var wider = FineTuning.Apply(Vr180, MenuAction.TuneHPlus)!.Value;
        Assert.Equal(185f, wider.EquirectHFov);
        Assert.True(wider.IsTuned);
        var back = FineTuning.Apply(wider, MenuAction.TuneHMinus)!.Value;
        Assert.Equal(0f, back.HFov);
        Assert.False(back.IsTuned);
    }

    [Fact]
    public void Fisheye_buttons_do_not_apply_to_equirect_and_vice_versa()
    {
        Assert.Null(FineTuning.Apply(Vr180, MenuAction.TuneLeft));
        Assert.Null(FineTuning.Apply(Fisheye, MenuAction.TuneVPlus));
        Assert.Null(FineTuning.Apply(VideoFormat.Default, MenuAction.TuneHPlus));
        Assert.NotNull(FineTuning.Apply(VideoFormat.Default, MenuAction.TuneNadir));
    }

    [Fact]
    public void Fisheye_field_of_view_center_radius_and_lens()
    {
        var f = FineTuning.Apply(Fisheye, MenuAction.TuneHPlus)!.Value;
        Assert.Equal(195f, f.FisheyeFov);
        f = FineTuning.Apply(f, MenuAction.TuneLeft)!.Value;
        Assert.Equal(FineTuning.CenterStep, f.CenterX, 4);
        f = FineTuning.Apply(f, MenuAction.TuneRadiusMinus)!.Value;
        Assert.Equal(0.99f, f.RadiusScale, 3);
        f = FineTuning.Apply(f, MenuAction.TuneLens)!.Value;
        Assert.Equal(FisheyeLens.Equisolid, f.Lens);
        var reset = FineTuning.Apply(f, MenuAction.TuneReset)!.Value;
        Assert.Equal(new VideoFormat(Projection.Fisheye, StereoMode.SideBySide), reset);
    }

    [Fact]
    public void Nadir_cycles_through_sizes_and_off()
    {
        var f = VideoFormat.Default;
        foreach (float expected in FineTuning.NadirSizes.Skip(1).Append(0f))
        {
            f = FineTuning.Apply(f, MenuAction.TuneNadir)!.Value;
            Assert.Equal(expected, f.NadirDeg);
        }
    }

    [Fact]
    public void Tuned_format_round_trips_and_old_strings_still_parse()
    {
        var f = Fisheye with { FisheyeFov = 200, Lens = FisheyeLens.Stereographic, CenterX = -0.015f, CenterY = 0.01f, RadiusScale = 1.04f, NadirDeg = 30 };
        Assert.Equal(f, VideoFormat.TryParse(f.Serialize()));
        var g = Vr180 with { HFov = 200, VFov = 160 };
        Assert.Equal(g, VideoFormat.TryParse(g.Serialize()));
        Assert.Equal(Vr180, VideoFormat.TryParse("Dome180,SideBySide,0,190"));
        Assert.Equal("Dome180,SideBySide,0,190", Vr180.Serialize());
    }
}

public class TunedMeshTests
{
    private static Vector3 Pos(Mesh m, int vertex) =>
        new(m.Vertices[vertex * 5], m.Vertices[vertex * 5 + 1], m.Vertices[vertex * 5 + 2]);

    [Fact]
    public void Narrower_vertical_coverage_stops_before_the_poles()
    {
        var m = MeshBuilder.Equirect(1f, 180f, 8, 4, latSpanDeg: 120f);
        // First row is the bottom edge: latitude -60°.
        Assert.Equal(-MathF.Sin(60f * VrMath.Deg2Rad), Pos(m, 0).Y, 3);
    }

    [Fact]
    public void Lens_models_agree_at_the_center_and_edge_but_not_in_between()
    {
        float max = 95f * VrMath.Deg2Rad, mid = max / 2;
        float Norm(FisheyeLens l, float t) => MeshBuilder.LensRadius(l, t) / MeshBuilder.LensRadius(l, max);
        Assert.Equal(0.5f, Norm(FisheyeLens.Equidistant, mid), 3);
        Assert.True(Norm(FisheyeLens.Equisolid, mid) > 0.5f);
        Assert.True(Norm(FisheyeLens.Stereographic, mid) < 0.5f);
    }

    [Fact]
    public void Off_center_fisheye_shifts_texture_coordinates_and_stays_inside()
    {
        var m = MeshBuilder.Fisheye(1f, 190f, 4, 8, centerX: 0.02f, radiusScale: 1.2f);
        // Ring 0 is the center of the circle.
        Assert.Equal(0.52f, m.Vertices[3], 3);
        for (int i = 3; i < m.Vertices.Length; i += 5)
        {
            Assert.InRange(m.Vertices[i], 0f, 1f);
            Assert.InRange(m.Vertices[i + 1], 0f, 1f);
        }
    }

    [Fact]
    public void Cap_rotated_down_covers_the_nadir()
    {
        var down = Vector3.TransformNormal(-Vector3.UnitZ, Matrix4x4.CreateRotationX(-MathF.PI / 2));
        Assert.Equal(-1f, down.Y, 4);
        var cap = MeshBuilder.Cap(1f, 30f);
        Assert.Equal(-1f, Pos(cap, 0).Z, 4); // center of the cap on its axis
    }
}
