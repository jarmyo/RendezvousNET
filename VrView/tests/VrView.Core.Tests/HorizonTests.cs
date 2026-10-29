using System.Numerics;
using VrView.Core;

namespace VrView.Core.Tests;

public class HorizonTests
{
    private const float D = VrMath.Deg2Rad;

    /// <summary>Scene→camera view of a head with the given yaw, pitch and roll (degrees).</summary>
    private static Matrix4x4 Head(float yaw, float pitch, float roll)
    {
        var cameraToScene = Matrix4x4.CreateRotationZ(roll * D) * Matrix4x4.CreateRotationX(pitch * D) * Matrix4x4.CreateRotationY(yaw * D);
        return Matrix4x4.Transpose(cameraToScene);
    }

    private static void Near(float expected, float actual) =>
        Assert.True(MathF.Abs(expected - actual) < 0.01f, $"expected {expected}, got {actual}");

    private static void NearMatrix(Matrix4x4 a, Matrix4x4 b)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                Assert.True(MathF.Abs(a[r, c] - b[r, c]) < 1e-4f, $"[{r},{c}] {a[r, c]} vs {b[r, c]}");
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(12.5f, -7, 30)]
    [InlineData(-40, 170, -95)]
    [InlineData(80, -33, 179)]
    public void Decompose_is_the_inverse_of_VideoModel(float pitch, float roll, float yaw)
    {
        var (p, r, y) = VrMath.DecomposeHorizon(VrMath.VideoModel(pitch, roll, yaw, 0));
        Near(pitch, p);
        Near(roll, r);
        Near(yaw, y);
    }

    [Fact]
    public void Tilted_head_becomes_the_new_level()
    {
        var (p, r, y) = VrMath.HorizonFromView(0, 0, 0, 0, Head(yaw: 0, pitch: 0, roll: 10));
        Near(0, p);
        Near(-10, r);
        Near(0, y);
    }

    [Fact]
    public void Looking_down_at_the_horizon_raises_it_to_eye_level()
    {
        var (p, r, y) = VrMath.HorizonFromView(0, 0, 0, 0, Head(yaw: 0, pitch: -15, roll: 0));
        Near(15, p);
        Near(0, r);
        Near(0, y);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0f, 30f, -12f, 8f)]
    [InlineData(5f, -3f, 20f, 1.2f, 95f, 10f, -6f)]
    [InlineData(-20f, 45f, -150f, -2.5f, -170f, -35f, 25f)]
    public void After_setting_a_level_head_sees_what_was_seen_at_capture(
        float pitch0, float roll0, float yaw0, float recenter0, float headYaw, float headPitch, float headRoll)
    {
        var view = Head(headYaw, headPitch, headRoll);
        var seenBefore = VrMath.VideoModel(pitch0, roll0, yaw0, recenter0) * view;

        var (p, r, y) = VrMath.HorizonFromView(pitch0, roll0, yaw0, recenter0, view);
        float recenter1 = VrMath.YawOf(VrMath.Forward(view));
        var levelHead = Head(headYaw, 0, 0); // same direction, head straightened
        var seenAfter = VrMath.VideoModel(p, r, y, recenter1) * levelHead;

        NearMatrix(seenBefore, seenAfter);
    }

    [Fact]
    public void Stored_yaw_does_not_depend_on_where_the_room_front_is()
    {
        // Same relative pose (head 20° right of the current front), different absolute sensor yaw.
        var a = VrMath.HorizonFromView(0, 0, 0, 0.3f, Head(0.3f / D - 20, 0, 5));
        var b = VrMath.HorizonFromView(0, 0, 0, 2.0f, Head(2.0f / D - 20, 0, 5));
        Near(a.YawDeg, b.YawDeg);
        Near(a.RollDeg, b.RollDeg);
    }
}

public class StillnessTests
{
    private static readonly Vector3 Fwd = -Vector3.UnitZ, Up = Vector3.UnitY;

    private static Vector3 Turn(float deg) => Vector3.TransformNormal(Fwd, Matrix4x4.CreateRotationY(deg * VrMath.Deg2Rad));

    [Fact]
    public void Fires_after_warmup_and_hold()
    {
        var s = new StillnessDetector { WarmupSeconds = 1, HoldSeconds = 2 };
        int frames = 0;
        while (!s.Update(Fwd, Up, 0.1f)) frames++;
        Assert.InRange(frames, 29, 31); // 1 s warm-up + 2 s hold at 10 fps
    }

    [Fact]
    public void Small_jitter_is_tolerated_but_a_real_move_restarts()
    {
        var s = new StillnessDetector { WarmupSeconds = 0, HoldSeconds = 1, ToleranceDegrees = 2.5f };
        for (int i = 0; i < 5; i++) Assert.False(s.Update(Turn(i % 2 == 0 ? 1f : -1f), Up, 0.1f));
        Assert.True(s.Progress > 0.3f);
        Assert.False(s.Update(Turn(10), Up, 0.1f));
        Assert.Equal(0f, s.Progress);
    }

    [Fact]
    public void Nothing_fires_during_warmup_even_if_still()
    {
        var s = new StillnessDetector { WarmupSeconds = 1.5f, HoldSeconds = 0.1f };
        for (int i = 0; i < 14; i++) Assert.False(s.Update(Fwd, Up, 0.1f));
    }
}

public class FolderKeyTests
{
    [Fact]
    public void Saf_documents_in_the_same_folder_share_a_key()
    {
        const string tree = "content://com.android.externalstorage.documents/tree/primary%3AMovies";
        string a = MediaUtil.FolderKey(tree + "/document/primary%3AMovies%2FVR%2Fa_180_LR.mp4");
        string b = MediaUtil.FolderKey(tree + "/document/primary%3AMovies%2FVR%2Fb.mp4");
        string other = MediaUtil.FolderKey(tree + "/document/primary%3AMovies%2FOtra%2Fc.mp4");
        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void Opaque_ids_fall_back_to_the_tree()
    {
        const string tree = "content://com.android.providers.downloads.documents/tree/downloads";
        Assert.Equal(tree, MediaUtil.FolderKey(tree + "/document/msf%3A123"));
    }

    [Fact]
    public void Plain_paths_use_the_parent()
    {
        Assert.Equal("/sdcard/Movies", MediaUtil.FolderKey("/sdcard/Movies/x.mp4"));
    }
}
