using System;
using System.Text;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// The rendering command stream (crates/unbound-runtime/src/render_commands.rs): everything that
    /// renders — cameras, command buffers, blits, global shader state — appends commands here in
    /// the order Unity runs them, and the renderer executes them in that order. Recorded commands
    /// are handed over at the end of a frame and before anything that must see their results
    /// (a resource change, a read back).
    /// </summary>
    internal static unsafe class GfxStream
    {
        const byte BeginFrameOp = 1, EndFrameOp = 2, SetTargetOp = 3, ClearOp = 4, ViewportOp = 5,
            CameraOp = 6, GlobalOp = 7, KeywordOp = 8, DrawOp = 9;

        internal const ulong NoTexture = ulong.MaxValue;
        internal const byte ClearColor = 1, ClearDepth = 2, ClearStencil = 4;
        internal const uint CameraOverlay = 1;

        /// <summary>Property kinds.</summary>
        internal const byte FloatKind = 0, VectorKind = 1, TextureKind = 2, IntKind = 3, MatrixKind = 4;

        static byte[] s_Buffer = new byte[64 * 1024];
        static int s_Length;

        internal static bool Pending => s_Length > 0;

        /// <summary>Hands the recorded commands to the renderer.</summary>
        internal static void Flush()
        {
            if (s_Length == 0) return;
            fixed (byte* p = s_Buffer) Native.SubmitCommands(p, s_Length);
            s_Length = 0;
        }

        static void Reserve(int bytes)
        {
            if (s_Length + bytes <= s_Buffer.Length) return;
            Array.Resize(ref s_Buffer, Math.Max(s_Buffer.Length * 2, s_Length + bytes));
        }

        static void U8(byte v)
        {
            Reserve(1);
            s_Buffer[s_Length++] = v;
        }

        static void U32(uint v)
        {
            Reserve(4);
            fixed (byte* p = &s_Buffer[s_Length]) *(uint*)p = v;
            s_Length += 4;
        }

        static void I32(int v) => U32((uint)v);

        static void U64(ulong v)
        {
            Reserve(8);
            fixed (byte* p = &s_Buffer[s_Length]) *(ulong*)p = v;
            s_Length += 8;
        }

        static void F32(float v) => U32(*(uint*)&v);

        static void Vec4(Vector4 v) { F32(v.x); F32(v.y); F32(v.z); F32(v.w); }

        static void Color(Color c) { F32(c.r); F32(c.g); F32(c.b); F32(c.a); }

        static void Rect(Rect r) { F32(r.x); F32(r.y); F32(r.width); F32(r.height); }

        static void Matrix(in Matrix4x4 m)
        {
            Reserve(64);
            fixed (byte* p = &s_Buffer[s_Length])
            {
                var f = (float*)p;
                for (int i = 0; i < 16; i++) f[i] = m[i];
            }
            s_Length += 64;
        }

        static void String(string s)
        {
            int n = Encoding.UTF8.GetByteCount(s);
            U32((uint)n);
            Reserve(n);
            Encoding.UTF8.GetBytes(s, 0, s.Length, s_Buffer, s_Length);
            s_Length += n;
        }

        internal static void BeginFrame(int screenWidth, int screenHeight, ColorSpace colorSpace)
        {
            U8(BeginFrameOp);
            I32(screenWidth);
            I32(screenHeight);
            I32(colorSpace == ColorSpace.Linear ? 1 : 0);
        }

        /// <summary>Ends the frame (the screen is shown) and hands it over.</summary>
        internal static void EndFrame()
        {
            U8(EndFrameOp);
            Flush();
        }

        /// <summary>Draws go into <paramref name="color"/> (0: the screen) and its depth buffer
        /// (<paramref name="depth"/> 0), another's or none (<see cref="NoTexture"/>); the viewport
        /// becomes the whole target.</summary>
        internal static void SetTarget(ulong color, ulong depth = 0, int mip = 0, int slice = 0)
        {
            U8(SetTargetOp);
            U64(color);
            U64(depth);
            I32(mip);
            I32(slice);
        }

        internal static void Clear(byte flags, Color color, float depth = 1f, uint stencil = 0)
        {
            U8(ClearOp);
            U8(flags);
            Color(color);
            F32(depth);
            U32(stencil);
        }

        /// <summary>Pixels, origin bottom-left.</summary>
        internal static void Viewport(Rect pixels)
        {
            U8(ViewportOp);
            Rect(pixels);
        }

        internal static void Camera(in Matrix4x4 view, in Matrix4x4 projection, uint flags = 0)
        {
            U8(CameraOp);
            Matrix(view);
            Matrix(projection);
            U32(flags);
        }

        internal static void GlobalFloat(string name, float value)
        {
            U8(GlobalOp);
            String(name);
            U8(FloatKind);
            F32(value);
        }

        internal static void GlobalInt(string name, int value)
        {
            U8(GlobalOp);
            String(name);
            U8(IntKind);
            I32(value);
        }

        internal static void GlobalVector(string name, Vector4 value)
        {
            U8(GlobalOp);
            String(name);
            U8(VectorKind);
            Vec4(value);
        }

        internal static void GlobalMatrix(string name, in Matrix4x4 value)
        {
            U8(GlobalOp);
            String(name);
            U8(MatrixKind);
            Matrix(value);
        }

        internal static void GlobalTexture(string name, ulong texture)
        {
            U8(GlobalOp);
            String(name);
            U8(TextureKind);
            U64(texture);
            Vec4(new Vector4(1, 1, 0, 0));
        }

        internal static void Keyword(string name, bool enabled)
        {
            U8(KeywordOp);
            String(name);
            U8(enabled ? (byte)1 : (byte)0);
        }

        /// <summary>A mesh's submesh with a material: <paramref name="pass"/> -1 draws the passes
        /// the built-in forward renderer draws, else that pass of the shader.</summary>
        internal static void Draw(ulong mesh, int submesh, ulong material, int pass, in Matrix4x4 model, Color color,
            ulong mainTexture = 0, Rect? clip = null)
        {
            U8(DrawOp);
            U64(mesh);
            I32(submesh);
            U64(material);
            I32(pass);
            Matrix(model);
            Color(color);
            U64(mainTexture);
            U8(clip.HasValue ? (byte)1 : (byte)0);
            Rect(clip ?? default);
            U32(0);
        }
    }
}
