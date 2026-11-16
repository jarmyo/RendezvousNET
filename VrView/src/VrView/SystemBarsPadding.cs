using Android.Views;

namespace VrView;

/// <summary>
/// Keeps a screen's content clear of the status and navigation bars (and the camera cutout).
/// Since Android 15 apps are drawn edge to edge, under those bars, so the root view adds their
/// size to its own padding.
/// </summary>
internal static class SystemBarsPadding
{
    public static T Apply<T>(T root) where T : View
    {
        int left = root.PaddingLeft, top = root.PaddingTop, right = root.PaddingRight, bottom = root.PaddingBottom;
        root.SetOnApplyWindowInsetsListener(new Listener((v, insets) =>
        {
            int l, t, r, b;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout() | WindowInsets.Type.Ime());
                (l, t, r, b) = (bars.Left, bars.Top, bars.Right, bars.Bottom);
            }
            else
            {
#pragma warning disable CA1422 // the old API is the one there before Android 11
                (l, t, r, b) = (insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
#pragma warning restore CA1422
            }
            v.SetPadding(left + l, top + t, right + r, bottom + b);
            return insets;
        }));
        return root;
    }

    private sealed class Listener(Func<View, WindowInsets, WindowInsets> apply) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets) => apply(v, insets);
    }
}
