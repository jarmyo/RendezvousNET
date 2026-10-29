using Android.Opengl;
using Android.Util;
using Java.Nio;
using VrView.Core;

namespace VrView.Gl;

/// <summary>Small OpenGL ES 2.0 helpers: shader programs and static vertex/index buffers.</summary>
internal static class GlUtil
{
    private const string Tag = "VrView.Gl";

    public static int CreateProgram(string vertexSource, string fragmentSource)
    {
        int vs = Compile(GLES20.GlVertexShader, vertexSource);
        int fs = Compile(GLES20.GlFragmentShader, fragmentSource);
        int program = GLES20.GlCreateProgram();
        GLES20.GlAttachShader(program, vs);
        GLES20.GlAttachShader(program, fs);
        GLES20.GlLinkProgram(program);
        var status = new int[1];
        GLES20.GlGetProgramiv(program, GLES20.GlLinkStatus, status, 0);
        if (status[0] == 0)
        {
            string log = GLES20.GlGetProgramInfoLog(program) ?? "";
            GLES20.GlDeleteProgram(program);
            throw new InvalidOperationException("GL link failed: " + log);
        }
        GLES20.GlDeleteShader(vs);
        GLES20.GlDeleteShader(fs);
        return program;
    }

    private static int Compile(int type, string source)
    {
        int shader = GLES20.GlCreateShader(type);
        GLES20.GlShaderSource(shader, source);
        GLES20.GlCompileShader(shader);
        var status = new int[1];
        GLES20.GlGetShaderiv(shader, GLES20.GlCompileStatus, status, 0);
        if (status[0] == 0)
        {
            string log = GLES20.GlGetShaderInfoLog(shader) ?? "";
            GLES20.GlDeleteShader(shader);
            throw new InvalidOperationException("GL compile failed: " + log);
        }
        return shader;
    }

    public static void CheckError(string where)
    {
        int err;
        while ((err = GLES20.GlGetError()) != GLES20.GlNoError)
            Log.Warn(Tag, $"GL error 0x{err:X} at {where}");
    }

    public static FloatBuffer ToBuffer(float[] data)
    {
        var fb = ByteBuffer.AllocateDirect(data.Length * 4).Order(ByteOrder.NativeOrder()!)!.AsFloatBuffer()!;
        fb.Put(data);
        fb.Position(0);
        return fb;
    }

    public static ShortBuffer ToBuffer(ushort[] data)
    {
        var shorts = new short[data.Length];
        System.Buffer.BlockCopy(data, 0, shorts, 0, data.Length * 2);
        var sb = ByteBuffer.AllocateDirect(shorts.Length * 2).Order(ByteOrder.NativeOrder()!)!.AsShortBuffer()!;
        sb.Put(shorts);
        sb.Position(0);
        return sb;
    }
}

/// <summary>A <see cref="Mesh"/> uploaded to GPU buffers (VBO + IBO).</summary>
internal sealed class GlMesh : IDisposable
{
    private readonly int _vbo, _ibo;
    public int IndexCount { get; }

    public GlMesh(Mesh mesh)
    {
        var ids = new int[2];
        GLES20.GlGenBuffers(2, ids, 0);
        _vbo = ids[0];
        _ibo = ids[1];
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, _vbo);
        GLES20.GlBufferData(GLES20.GlArrayBuffer, mesh.Vertices.Length * 4, GlUtil.ToBuffer(mesh.Vertices), GLES20.GlStaticDraw);
        GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, _ibo);
        GLES20.GlBufferData(GLES20.GlElementArrayBuffer, mesh.Indices.Length * 2, GlUtil.ToBuffer(mesh.Indices), GLES20.GlStaticDraw);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);
        GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, 0);
        IndexCount = mesh.Indices.Length;
    }

    /// <summary>Binds position (vec3) and texture coordinate (vec2) attributes and draws.</summary>
    public void Draw(int positionAttr, int uvAttr)
    {
        const int stride = Mesh.FloatsPerVertex * 4;
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, _vbo);
        GLES20.GlEnableVertexAttribArray(positionAttr);
        GLES20.GlVertexAttribPointer(positionAttr, 3, GLES20.GlFloat, false, stride, 0);
        if (uvAttr >= 0)
        {
            GLES20.GlEnableVertexAttribArray(uvAttr);
            GLES20.GlVertexAttribPointer(uvAttr, 2, GLES20.GlFloat, false, stride, 12);
        }
        GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, _ibo);
        GLES20.GlDrawElements(GLES20.GlTriangles, IndexCount, GLES20.GlUnsignedShort, 0);
        GLES20.GlDisableVertexAttribArray(positionAttr);
        if (uvAttr >= 0) GLES20.GlDisableVertexAttribArray(uvAttr);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);
        GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, 0);
    }

    public void Dispose()
    {
        GLES20.GlDeleteBuffers(2, new[] { _vbo, _ibo }, 0);
    }
}
