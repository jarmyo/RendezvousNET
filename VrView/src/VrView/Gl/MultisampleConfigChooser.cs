using Android.Opengl;
using Javax.Microedition.Khronos.Egl;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;
using EGLDisplay = Javax.Microedition.Khronos.Egl.EGLDisplay;

namespace VrView.Gl;

/// <summary>
/// Picks an RGB888 OpenGL ES 2 surface with 4× multisampling (smooth edges of the flat screen,
/// menu and buttons), falling back to no multisampling when the device has none.
/// </summary>
internal sealed class MultisampleConfigChooser : Java.Lang.Object, GLSurfaceView.IEGLConfigChooser
{
    // EGL constants (EGL 1.0 / 1.3).
    private const int EglRedSize = 0x3024, EglGreenSize = 0x3023, EglBlueSize = 0x3022, EglDepthSize = 0x3025;
    private const int EglRenderableType = 0x3040, EglOpenGlEs2Bit = 4;
    private const int EglSampleBuffers = 0x3032, EglSamples = 0x3031, EglNone = 0x3038;

    public EGLConfig? ChooseConfig(IEGL10? egl, EGLDisplay? display)
    {
        return Choose(egl!, display!, samples: 4)
               ?? Choose(egl!, display!, samples: 0)
               ?? throw new InvalidOperationException("No OpenGL ES 2 configuration available.");
    }

    private static EGLConfig? Choose(IEGL10 egl, EGLDisplay display, int samples)
    {
        var attributes = samples > 0
            ? new[] { EglRedSize, 8, EglGreenSize, 8, EglBlueSize, 8, EglDepthSize, 0, EglRenderableType, EglOpenGlEs2Bit,
                      EglSampleBuffers, 1, EglSamples, samples, EglNone }
            : new[] { EglRedSize, 8, EglGreenSize, 8, EglBlueSize, 8, EglDepthSize, 0, EglRenderableType, EglOpenGlEs2Bit, EglNone };
        var configs = new EGLConfig[1];
        var count = new int[1];
        return egl.EglChooseConfig(display, attributes, configs, 1, count) && count[0] > 0 ? configs[0] : null;
    }
}
