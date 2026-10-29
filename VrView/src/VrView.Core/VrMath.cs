using System.Numerics;

namespace VrView.Core;

/// <summary>
/// Head-tracking and camera math. Uses System.Numerics (row-vector convention: v' = v * M,
/// and A * B means "apply A, then B"). A Matrix4x4 copied in memory order is exactly the
/// column-major array OpenGL ES expects, see <see cref="ToGl"/>.
///
/// Spaces:
///  - Scene: Y up, -Z is the front/center of the video, X to the right.
///  - Sensor world (Android): X east, Y magnetic north (or arbitrary for game rotation vector), Z up.
///  - Device (Android): X right edge, Y top edge, Z out of the screen, in natural (portrait) orientation.
///  - Camera (GL): X right on screen, Y up on screen, looking down -Z.
/// </summary>
public static class VrMath
{
    public const float Deg2Rad = MathF.PI / 180f;
    public const float Rad2Deg = 180f / MathF.PI;

    /// <summary>Scene (Y up) to sensor world (Z up): x→x, y→z, z→-y.</summary>
    public static readonly Matrix4x4 SceneToWorld = new(
        1, 0, 0, 0,
        0, 0, 1, 0,
        0, -1, 0, 0,
        0, 0, 0, 1);

    /// <summary>
    /// Builds the world→device rotation from Android's 3x3 rotation matrix
    /// (<c>SensorManager.getRotationMatrixFromVector</c>, row-major, maps device→world).
    /// </summary>
    public static Matrix4x4 WorldToDevice(ReadOnlySpan<float> r9) => new(
        r9[0], r9[1], r9[2], 0,
        r9[3], r9[4], r9[5], 0,
        r9[6], r9[7], r9[8], 0,
        0, 0, 0, 1);

    /// <summary>
    /// Device axes → on-screen camera axes for a display rotation (Surface.ROTATION_0..270 = 0..3).
    /// ROTATION_90 is the usual landscape: the phone turned counter-clockwise (top edge to the left).
    /// </summary>
    public static Matrix4x4 DeviceToCamera(int displayRotation) => (displayRotation & 3) switch
    {
        1 => new Matrix4x4(0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
        2 => new Matrix4x4(-1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
        3 => new Matrix4x4(0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
        _ => Matrix4x4.Identity,
    };

    /// <summary>Scene→camera rotation for the current head pose.</summary>
    public static Matrix4x4 HeadView(ReadOnlySpan<float> r9, int displayRotation) =>
        SceneToWorld * WorldToDevice(r9) * DeviceToCamera(displayRotation);

    /// <summary>Direction the camera looks at, in scene space (unit vector).</summary>
    public static Vector3 Forward(in Matrix4x4 view) => new(-view.M13, -view.M23, -view.M33);

    /// <summary>Camera "up" in scene space.</summary>
    public static Vector3 Up(in Matrix4x4 view) => new(view.M12, view.M22, view.M32);

    /// <summary>
    /// Yaw (radians) such that <c>Matrix4x4.CreateRotationY(yaw)</c> turns the scene front (-Z)
    /// towards the horizontal component of <paramref name="forward"/>.
    /// </summary>
    public static float YawOf(Vector3 forward) => MathF.Atan2(-forward.X, -forward.Z);

    /// <summary>Elevation (radians) of a direction; positive is up.</summary>
    public static float PitchOf(Vector3 forward) => MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));

    /// <summary>
    /// Model transform for the video: first the stored horizon correction (roll, then pitch,
    /// then yaw), then the session recenter yaw.
    /// </summary>
    public static Matrix4x4 VideoModel(float pitchDeg, float rollDeg, float yawDeg, float recenterYawRad) =>
        Matrix4x4.CreateRotationZ(rollDeg * Deg2Rad)
        * Matrix4x4.CreateRotationX(pitchDeg * Deg2Rad)
        * Matrix4x4.CreateRotationY(yawDeg * Deg2Rad + recenterYawRad);

    /// <summary>
    /// Inverse of <see cref="VideoModel"/> (with no recenter): splits a rotation built as
    /// RotZ(roll) * RotX(pitch) * RotY(yaw) back into its angles, in degrees.
    /// </summary>
    public static (float PitchDeg, float RollDeg, float YawDeg) DecomposeHorizon(in Matrix4x4 m)
    {
        float pitch = MathF.Asin(Math.Clamp(-m.M32, -1f, 1f));
        float roll = MathF.Atan2(m.M12, m.M22);
        float yaw = MathF.Atan2(m.M31, m.M33);
        return (pitch * Rad2Deg, roll * Rad2Deg, yaw * Rad2Deg);
    }

    /// <summary>
    /// "Set horizon with the head": returns the horizon correction that turns what the user sees
    /// right now (current correction + recenter + head pose <paramref name="view"/>) into the level,
    /// straight-ahead view. The caller must then recenter on the head's current yaw
    /// (<c>YawOf(Forward(view))</c>), so the stored yaw stays relative to the video, not to the room.
    /// </summary>
    public static (float PitchDeg, float RollDeg, float YawDeg) HorizonFromView(
        float pitchDeg, float rollDeg, float yawDeg, float recenterYawRad, in Matrix4x4 view)
    {
        // Seen now: VideoModel(current) * view. After recentering on the head yaw ψ and straightening
        // the head (view = RotY(-ψ)), the view becomes C1 * RotY(ψ) * RotY(-ψ) = C1, so C1 must be
        // exactly what is seen now.
        var seen = VideoModel(pitchDeg, rollDeg, yawDeg, recenterYawRad) * view;
        return DecomposeHorizon(seen);
    }

    /// <summary>
    /// Perspective projection with an optional horizontal lens-center shift (in NDC units),
    /// used to adapt the image to the viewer's lens spacing (IPD).
    /// </summary>
    public static Matrix4x4 Projection(float fovYDeg, float aspect, float near, float far, float ndcShiftX)
    {
        var p = Matrix4x4.CreatePerspectiveFieldOfView(fovYDeg * Deg2Rad, aspect, near, far);
        return ndcShiftX == 0 ? p : p * Matrix4x4.CreateTranslation(ndcShiftX, 0, 0);
    }

    /// <summary>Copies a matrix into a GL-ready float[16] (column-major for column vectors).</summary>
    public static void ToGl(in Matrix4x4 m, float[] dst)
    {
        dst[0] = m.M11; dst[1] = m.M12; dst[2] = m.M13; dst[3] = m.M14;
        dst[4] = m.M21; dst[5] = m.M22; dst[6] = m.M23; dst[7] = m.M24;
        dst[8] = m.M31; dst[9] = m.M32; dst[10] = m.M33; dst[11] = m.M34;
        dst[12] = m.M41; dst[13] = m.M42; dst[14] = m.M43; dst[15] = m.M44;
    }

    /// <summary>World-locked panel placement: pushed out to <paramref name="distance"/>, then rotated.</summary>
    public static Matrix4x4 PanelModel(float yawRad, float pitchRad, float distance) =>
        Matrix4x4.CreateTranslation(0, 0, -distance)
        * Matrix4x4.CreateRotationX(pitchRad)
        * Matrix4x4.CreateRotationY(yawRad);

    /// <summary>
    /// Intersects the gaze ray (from the scene origin along <paramref name="forward"/>) with a panel
    /// placed by <see cref="PanelModel"/>. Returns panel coordinates (u right, v down, both 0..1
    /// inside the panel) or null when looking away from it.
    /// </summary>
    public static Vector2? PanelHit(Vector3 forward, float yawRad, float pitchRad, float distance, float width, float height)
    {
        var inv = Matrix4x4.CreateRotationY(-yawRad) * Matrix4x4.CreateRotationX(-pitchRad);
        var d = Vector3.TransformNormal(forward, inv);
        if (d.Z > -1e-4f) return null;
        float t = -distance / d.Z;
        float x = d.X * t, y = d.Y * t;
        float u = x / width + 0.5f, v = 0.5f - y / height;
        if (u < -0.25f || u > 1.25f || v < -0.5f || v > 1.5f) return null;
        return new Vector2(u, v);
    }
}
