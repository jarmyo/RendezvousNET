namespace VrView.Gl;

/// <summary>GLSL ES 1.00 sources.</summary>
internal static class Shaders
{
    /// <summary>Video: samples the decoder's external OES texture with a per-eye sub-rectangle.</summary>
    public const string VideoVertex = """
        uniform mat4 uMvp;
        uniform mat4 uStMatrix;   // SurfaceTexture transform
        uniform vec4 uEyeRect;    // scaleU, scaleV, offsetU, offsetV
        attribute vec4 aPosition;
        attribute vec2 aUv;
        varying vec2 vTex;
        void main() {
            gl_Position = uMvp * aPosition;
            vec2 uv = aUv * uEyeRect.xy + uEyeRect.zw;
            vTex = (uStMatrix * vec4(uv, 0.0, 1.0)).xy;
        }
        """;

    public const string VideoFragment = """
        #extension GL_OES_EGL_image_external : require
        precision mediump float;
        uniform samplerExternalOES uTexture;
        varying vec2 vTex;
        void main() {
            gl_FragColor = texture2D(uTexture, vTex);
        }
        """;

    /// <summary>Plain textured geometry (menu panel, distortion pass input).</summary>
    public const string TexturedVertex = """
        uniform mat4 uMvp;
        attribute vec4 aPosition;
        attribute vec2 aUv;
        varying vec2 vUv;
        void main() {
            gl_Position = uMvp * aPosition;
            vUv = aUv;
        }
        """;

    public const string TexturedFragment = """
        precision mediump float;
        uniform sampler2D uTexture;
        uniform float uAlpha;
        varying vec2 vUv;
        void main() {
            vec4 c = texture2D(uTexture, vUv);
            gl_FragColor = vec4(c.rgb, c.a * uAlpha);
        }
        """;

    /// <summary>Gaze reticle: a point sprite drawn as a dot with a progress ring.</summary>
    public const string ReticleVertex = """
        uniform mat4 uMvp;
        uniform float uSize;
        attribute vec4 aPosition;
        void main() {
            gl_Position = uMvp * aPosition;
            gl_PointSize = uSize;
        }
        """;

    public const string ReticleFragment = """
        precision mediump float;
        uniform float uProgress;  // 0..1 dwell progress
        uniform vec4 uColor;
        void main() {
            vec2 p = gl_PointCoord * 2.0 - 1.0;
            float r = length(p);
            if (r > 1.0) discard;
            float center = 1.0 - smoothstep(0.22, 0.32, r);
            float ring = smoothstep(0.62, 0.7, r) * (1.0 - smoothstep(0.9, 1.0, r));
            float angle = atan(p.x, p.y) / 6.2831853 + 0.5;
            float filled = step(angle, uProgress);
            vec4 ringColor = mix(vec4(1.0, 1.0, 1.0, 0.25), vec4(0.31, 0.76, 0.97, 1.0), filled);
            gl_FragColor = center * uColor + ring * ringColor * (1.0 - center);
        }
        """;

    /// <summary>
    /// Lens pre-distortion (barrel) applied to an eye image rendered off-screen, to counter the
    /// pincushion distortion of the viewer's lenses. Coefficients follow the usual Brown model.
    /// </summary>
    public const string DistortionFragment = """
        precision mediump float;
        uniform sampler2D uTexture;
        uniform vec2 uK;        // k1, k2
        uniform float uAspect;  // eye viewport width / height
        uniform float uScale;   // normalization so the horizontal edge maps to the edge
        varying vec2 vUv;
        void main() {
            vec2 q = (vUv - 0.5) * 2.0;
            q.x *= uAspect;
            float r2 = dot(q, q) / (uAspect * uAspect);
            float f = (1.0 + uK.x * r2 + uK.y * r2 * r2) / uScale;
            vec2 s = q * f;
            s.x /= uAspect;
            s = s * 0.5 + 0.5;
            if (s.x < 0.0 || s.x > 1.0 || s.y < 0.0 || s.y > 1.0) {
                gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0);
            } else {
                gl_FragColor = texture2D(uTexture, s);
            }
        }
        """;
}
