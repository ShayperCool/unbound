using System;
using System.Collections.Generic;
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
            CameraOp = 6, GlobalOp = 7, KeywordOp = 8, DrawOp = 9, CopyTextureOp = 10,
            CreateBufferOp = 11, BufferDataOp = 12, ReleaseBufferOp = 13, DispatchOp = 14,
            RenderPipelineOp = 15, PassTagsOp = 16;

        internal const ulong NoTexture = ulong.MaxValue;
        internal const byte ClearColor = 1, ClearDepth = 2, ClearStencil = 4;
        internal const uint CameraOverlay = 1;

        /// <summary>Property kinds.</summary>
        internal const byte FloatKind = 0, VectorKind = 1, TextureKind = 2, IntKind = 3, MatrixKind = 4,
            BufferKind = 5, ArrayKind = 6, ImageKind = 7;

        static byte[] s_Buffer = new byte[64 * 1024];
        static int s_Length;

        /// <summary>The state the last commands set, to put back after a nested sequence (a blit or a
        /// command buffer inside a camera's rendering).</summary>
        internal struct State
        {
            internal ulong Color, Depth;
            internal Rect Viewport;
            internal Matrix4x4 View, Projection;
            internal uint Flags;
        }

        static State s_State = new State { View = Matrix4x4.identity, Projection = Matrix4x4.identity };
        internal static State Current => s_State;

        internal static void Restore(in State state)
        {
            if (state.Color != s_State.Color || state.Depth != s_State.Depth) SetTarget(state.Color, state.Depth);
            Viewport(state.Viewport);
            Camera(state.View, state.Projection, state.Flags);
        }

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
            s_State.Color = color;
            s_State.Depth = depth;
            s_State.Viewport = new Rect(0, 0, float.MaxValue, float.MaxValue);
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
            s_State.Viewport = pixels;
            U8(ViewportOp);
            Rect(pixels);
        }

        internal static void Camera(in Matrix4x4 view, in Matrix4x4 projection, uint flags = 0)
        {
            s_State.View = view;
            s_State.Projection = projection;
            s_State.Flags = flags;
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

        /// <summary>Pass numbers of <see cref="Draw"/> besides a shader pass index: the built-in
        /// forward passes, every pass, or the passes whose LightMode is one of <see cref="PassTags"/>.</summary>
        internal const int ForwardPasses = -1, AllPasses = -2, TaggedPasses = -3;

        /// <summary>The active pipeline's tag (Shader.globalRenderPipeline): which SubShader draws.</summary>
        internal static void RenderPipeline(string tag)
        {
            U8(RenderPipelineOp);
            String(tag ?? "");
        }

        static string[] s_PassTags = Array.Empty<string>();

        /// <summary>The LightMode tags <see cref="TaggedPasses"/> draws pick passes by, in order
        /// (an untagged pass is <c>SRPDefaultUnlit</c>).</summary>
        internal static void PassTags(string[] tags)
        {
            if (SameTags(tags, s_PassTags)) return;
            s_PassTags = (string[])tags.Clone();
            U8(PassTagsOp);
            U32((uint)tags.Length);
            foreach (var tag in tags) String(tag ?? "");
        }

        static bool SameTags(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>A per-draw property with its texture handle resolved (resolving can upload, which
        /// must not happen in the middle of a command).</summary>
        struct PropertyValue
        {
            internal string Name;
            internal byte Kind;
            internal Vector4 Vector;
            internal Matrix4x4 Matrix;
            internal ulong Texture;
            internal uint Components;
            internal float[] Array;
        }

        static readonly List<PropertyValue> s_Properties = new List<PropertyValue>();

        static void Resolve(Dictionary<int, object> properties)
        {
            s_Properties.Clear();
            if (properties == null) return;
            foreach (var p in properties)
            {
                var name = Shader.IdToName(p.Key);
                if (name == null) continue;
                var value = new PropertyValue { Name = name };
                switch (p.Value)
                {
                    case float f: value.Kind = FloatKind; value.Vector = new Vector4(f, 0, 0, 0); break;
                    case int i: value.Kind = IntKind; value.Vector = new Vector4(i, 0, 0, 0); break;
                    case Vector4 v: value.Kind = VectorKind; value.Vector = v; break;
                    case Matrix4x4 m: value.Kind = MatrixKind; value.Matrix = m; break;
                    case Texture t: value.Kind = TextureKind; value.Texture = TextureHandles.Of(t); break;
                    default:
                        if (!ArrayData(p.Value, out value.Components, out value.Array)) continue;
                        value.Kind = ArrayKind;
                        break;
                }
                s_Properties.Add(value);
            }
        }

        /// <summary>A mesh's submesh with a material: <paramref name="pass"/> <see cref="ForwardPasses"/>
        /// draws the passes the built-in forward renderer draws, <see cref="AllPasses"/> every pass,
        /// else that pass of the shader. <paramref name="properties"/> override the material's for
        /// this draw (a MaterialPropertyBlock's values).</summary>
        internal static void Draw(ulong mesh, int submesh, ulong material, int pass, in Matrix4x4 model, Color color,
            ulong mainTexture = 0, Rect? clip = null, Dictionary<int, object> properties = null)
        {
            Resolve(properties);
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
            U32((uint)s_Properties.Count);
            foreach (var p in s_Properties)
            {
                String(p.Name);
                U8(p.Kind);
                switch (p.Kind)
                {
                    case FloatKind: F32(p.Vector.x); break;
                    case IntKind: I32((int)p.Vector.x); break;
                    case VectorKind: Vec4(p.Vector); break;
                    case MatrixKind: Matrix(p.Matrix); break;
                    case TextureKind: U64(p.Texture); Vec4(new Vector4(1, 1, 0, 0)); break;
                    case ArrayKind:
                        U32(p.Components);
                        U32((uint)p.Array.Length);
                        foreach (var f in p.Array) F32(f);
                        break;
                }
            }
        }

        /// <summary>A global array: <paramref name="components"/> floats per element.</summary>
        internal static void GlobalArray(string name, uint components, float[] data)
        {
            U8(GlobalOp);
            String(name);
            U8(ArrayKind);
            U32(components);
            U32((uint)data.Length);
            foreach (var f in data) F32(f);
        }

        /// <summary>An array value as floats: its components per element and its data.</summary>
        internal static bool ArrayData(object value, out uint components, out float[] data)
        {
            switch (value)
            {
                case float[] fs:
                    components = 1;
                    data = fs;
                    return true;
                case Vector4[] vs:
                    components = 4;
                    data = new float[vs.Length * 4];
                    for (int i = 0; i < vs.Length; i++) { data[i * 4] = vs[i].x; data[i * 4 + 1] = vs[i].y; data[i * 4 + 2] = vs[i].z; data[i * 4 + 3] = vs[i].w; }
                    return true;
                case Matrix4x4[] ms:
                    components = 16;
                    data = new float[ms.Length * 16];
                    for (int i = 0; i < ms.Length; i++) for (int k = 0; k < 16; k++) data[i * 16 + k] = ms[i][k];
                    return true;
                default:
                    components = 0;
                    data = null;
                    return false;
            }
        }

        internal static void GlobalBuffer(string name, ulong buffer)
        {
            U8(GlobalOp);
            String(name);
            U8(BufferKind);
            U64(buffer);
        }

        /// <summary>A zeroed buffer of <paramref name="size"/> bytes (GraphicsBuffer.Target flags).</summary>
        internal static void CreateBuffer(ulong handle, long size, uint usage)
        {
            U8(CreateBufferOp);
            U64(handle);
            U64((ulong)size);
            U32(usage);
        }

        internal static void BufferData(ulong handle, long offset, ReadOnlySpan<byte> data)
        {
            U8(BufferDataOp);
            U64(handle);
            U64((ulong)offset);
            U32((uint)data.Length);
            Reserve(data.Length);
            data.CopyTo(new Span<byte>(s_Buffer, s_Length, data.Length));
            s_Length += data.Length;
        }

        internal static void ReleaseBuffer(ulong handle)
        {
            U8(ReleaseBufferOp);
            U64(handle);
        }

        /// <summary>A compute dispatch's property, resolved before the command is written.</summary>
        internal struct DispatchProperty
        {
            internal string Name;
            internal byte Kind;
            internal Vector4 Vector;
            internal Matrix4x4 Matrix;
            internal int Int;
            internal float[] Array;
            internal uint Components;
            internal ulong Handle;
        }

        /// <summary>Runs a kernel of a <c>.compute</c> file over thread groups.</summary>
        internal static void Dispatch(string file, string kernel, string keywords, int x, int y, int z, List<DispatchProperty> properties)
        {
            U8(DispatchOp);
            String(file);
            String(kernel);
            String(keywords);
            U32((uint)x);
            U32((uint)y);
            U32((uint)z);
            U32((uint)properties.Count);
            foreach (var p in properties)
            {
                String(p.Name);
                U8(p.Kind);
                switch (p.Kind)
                {
                    case FloatKind: F32(p.Vector.x); break;
                    case IntKind: I32(p.Int); break;
                    case VectorKind: Vec4(p.Vector); break;
                    case MatrixKind: Matrix(p.Matrix); break;
                    case TextureKind: U64(p.Handle); Vec4(new Vector4(1, 1, 0, 0)); break;
                    case ImageKind: U64(p.Handle); I32(p.Int); break;
                    case BufferKind: U64(p.Handle); break;
                    case ArrayKind:
                        U32(p.Components);
                        U32((uint)p.Array.Length);
                        foreach (var f in p.Array) F32(f);
                        break;
                }
            }
        }

        /// <summary>Copies texels between textures (a negative width copies the whole level).</summary>
        internal static void CopyTexture(ulong source, int sourceSlice, int sourceMip, RectInt sourceRect,
            ulong destination, int destinationSlice, int destinationMip, int destinationX, int destinationY)
        {
            U8(CopyTextureOp);
            U64(source);
            I32(sourceSlice);
            I32(sourceMip);
            I32(sourceRect.x); I32(sourceRect.y); I32(sourceRect.width); I32(sourceRect.height);
            U64(destination);
            I32(destinationSlice);
            I32(destinationMip);
            I32(destinationX);
            I32(destinationY);
        }
    }
}
