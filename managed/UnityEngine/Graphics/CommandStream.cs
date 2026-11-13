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
            RenderPipelineOp = 15, PassTagsOp = 16, DepthBiasOp = 17, StateOverrideOp = 18, ColorSpaceOp = 19, AnisotropyOp = 20,
            SetTargetsOp = 21, DrawSetupOp = 22, GenerateMipsOp = 23,
            // Render protocol 7.
            CopyBufferOp = 24, SetCounterOp = 25, CopyCounterOp = 26, DispatchIndirectOp = 27,
            // Render protocol 8 (NativeApi 17).
            DrawIndicesOp = 28,
            // Render protocol 9 (NativeApi 19).
            CopyDepthOp = 29,
            // Render protocol 11 (NativeApi 22); its element meaning from protocol 12 (NativeApi 28).
            MeshInstanceOp = 30, MeshBuffersOp = 31, InstanceMultiplierOp = 32,
            // NativeApi 39.
            ResolveTextureOp = 33,
            // NativeApi 40.
            PluginOp = 34,
            // NativeApi 50.
            ScreenStandInOp = 35,
            // NativeApi 59.
            DotsDrawOp = 36;

        /// <summary>The kinds of <see cref="PluginOp"/> (render_commands.rs's PluginCall).</summary>
        const byte PluginEventKind = 0, PluginEventAndDataKind = 1, PluginTextureUpdateV1Kind = 2,
            PluginTextureUpdateV2Kind = 3, PluginCustomBlitKind = 4;

        /// <summary>The sizes of a DrawIndices index: 16-bit, and 32-bit from render protocol 10.</summary>
        internal const byte DrawIndexSize = 2, DrawIndex32Size = 4;

        internal const ulong NoTexture = ulong.MaxValue;

        /// <summary>How many draws, clears and copies have gone into the screen: whether anything
        /// was drawn on it since a given point (FrameBuilder's screen stack). Binding it alone does
        /// not count.</summary>
        internal static long ScreenWrites;

        static bool OnScreen => s_State.Color == 0 && s_State.Colors == null;
        internal const byte ClearColor = 1, ClearDepth = 2, ClearStencil = 4;
        internal const uint CameraOverlay = 1;
        /// <summary>The camera's draws cull the other faces (a view that mirrors, GL.invertCulling).</summary>
        internal const uint CameraInvertCulling = 2;
        /// <summary>A camera starts rendering (named GrabPass textures are grabbed again).</summary>
        internal const uint CameraBegin = 4;
        /// <summary>A camera's setup: the camera built-ins (_WorldSpaceCameraPos,
        /// _ProjectionParams, _ZBufferParams, unity_OrthoParams) follow these matrices until the
        /// next one; blits and command buffer matrices leave them, as in Unity.</summary>
        internal const uint CameraProperties = 8;
        /// <summary>Triangles draw as lines (GL.wireframe).</summary>
        internal const uint CameraWireframe = 16;
        /// <summary>sRGB targets take the colors as they are, not converted (GL.sRGBWrite off).</summary>
        internal const uint CameraNoSrgbWrite = 32;
        internal const uint CameraGizmoDraw = 64;

        /// <summary>GL's invertCulling, wireframe and sRGBWrite as camera flags: they apply to every
        /// camera setup and matrix change until changed.</summary>
        static uint s_GlFlags;

        /// <summary>Sets a GL flag; the draws that follow see it (the current matrices are sent again
        /// with it).</summary>
        internal static void SetGlFlag(uint flag, bool on)
        {
            uint flags = on ? s_GlFlags | flag : s_GlFlags & ~flag;
            if (flags == s_GlFlags) return;
            s_GlFlags = flags;
            Camera(s_State.View, s_State.Projection, s_State.Flags);
        }

        /// <summary>Property kinds.</summary>
        internal const byte FloatKind = 0, VectorKind = 1, TextureKind = 2, IntKind = 3, MatrixKind = 4,
            BufferKind = 5, ArrayKind = 6, ImageKind = 7,
            // Render protocol 12: an array defined once per stream, then referenced by its id.
            SharedArrayKind = 8, ArrayRefKind = 9;

        /// <summary>A property array frozen for many draws (the draws of one instanced call): its
        /// floats go into the stream once per hand-over and the other draws reference them.</summary>
        internal sealed class SharedArray
        {
            internal readonly uint Components;
            internal readonly float[] Data;

            SharedArray(uint components, float[] data) { Components = components; Data = data; }

            /// <summary>A copy of a float, vector or matrix array; null for another value.</summary>
            internal static SharedArray Of(object value)
            {
                if (value is float[] fs) return new SharedArray(1, (float[])fs.Clone());
                return ArrayData(value, out var components, out var data) ? new SharedArray(components, data) : null;
            }
        }

        /// <summary>The shared arrays written since the last hand-over, by their stream id.</summary>
        static readonly Dictionary<SharedArray, uint> s_SharedArrays = new Dictionary<SharedArray, uint>(ReferenceEqualityComparer.Instance);

        static byte[] s_Buffer = new byte[64 * 1024];
        static int s_Length;

        /// <summary>The state the last commands set, to put back after a nested sequence (a blit or a
        /// command buffer inside a camera's rendering).</summary>
        internal struct State
        {
            internal ulong Color, Depth;
            internal ulong[] Colors;
            /// <summary>Slice: the target's layer, or -1 for all of them (<see cref="Layers"/>).</summary>
            internal int Mip, Slice, Layers;
            internal Rect Viewport;
            internal Rect? Scissor;
            internal Matrix4x4 View, Projection;
            internal uint Flags;
        }

        static State s_State = new State { View = Matrix4x4.identity, Projection = Matrix4x4.identity };
        internal static State Current => s_State;

        internal static void Restore(in State state)
        {
            if (state.Color != s_State.Color || state.Depth != s_State.Depth || state.Mip != s_State.Mip || state.Slice != s_State.Slice || state.Colors != s_State.Colors)
            {
                if (state.Colors != null) SetTargets(state.Colors, state.Depth, state.Mip, state.Slice, state.Layers);
                else SetTarget(state.Color, state.Depth, state.Mip, state.Slice, state.Layers);
            }
            s_State.Scissor = state.Scissor;
            Viewport(state.Viewport);
            Camera(state.View, state.Projection, state.Flags);
        }

        internal static bool Pending => s_Length > 0;

        /// <summary>The commands recorded since the last hand-over (read by tests).</summary>
        internal static ReadOnlySpan<byte> Recorded => new ReadOnlySpan<byte>(s_Buffer, 0, s_Length);

        /// <summary>Hands the recorded commands to the renderer.</summary>
        internal static void Flush()
        {
            if (s_Length == 0) return;
            fixed (byte* p = s_Buffer) Native.SubmitCommands(p, s_Length);
            s_Length = 0;
            s_SharedArrays.Clear();
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
            SpriteMasking.NewFrame();
            U8(BeginFrameOp);
            I32(screenWidth);
            I32(screenHeight);
            I32(colorSpace == ColorSpace.Linear ? 1 : 0);
        }

        /// <summary>A frame that does not draw on the screen (OnDemandRendering): the frame's sprite
        /// mask bookkeeping and color space without starting a screen frame, so the screen keeps
        /// showing the last one. Its commands are handed over with <see cref="Flush"/>, as those of
        /// a camera rendered between frames.</summary>
        internal static void BeginOffscreenFrame(ColorSpace colorSpace)
        {
            SpriteMasking.NewFrame();
            SetColorSpace(colorSpace);
            // Keep the normal frame's raster-state defaults without advancing the screen ring.
            DepthBias(0f, 0f);
            StateOverride(null);
        }

        /// <summary>The project's color space for what follows, before the first frame begins (a
        /// camera rendered from Start).</summary>
        internal static void SetColorSpace(UnityEngine.ColorSpace colorSpace)
        {
            U8(ColorSpaceOp);
            I32(colorSpace == UnityEngine.ColorSpace.Linear ? 1 : 0);
        }

        /// <summary>The global anisotropic filtering (QualitySettings.anisotropicFiltering) and
        /// its limits.</summary>
        internal static void Anisotropy(int mode, int forcedMin, int globalMax)
        {
            U8(AnisotropyOp);
            I32(mode);
            I32(forcedMin);
            I32(globalMax);
        }

        /// <summary>Ends the frame (the screen is shown) and hands it over.</summary>
        internal static void EndFrame()
        {
            U8(EndFrameOp);
            Flush();
        }

        /// <summary>Draws go into <paramref name="color"/> (0: the screen) and its depth buffer
        /// (<paramref name="depth"/> 0), another's or none (<see cref="NoTexture"/>); the viewport
        /// becomes the whole target.
        /// <para>A negative <paramref name="slice"/> binds every one of the target's
        /// <paramref name="layers"/>, as the Editor's GL device does (measured,
        /// tests/projects/droppedparams slice.*): a clear clears them all; a draw goes into the
        /// first, as on GL when the shader picks no layer.</para></summary>
        internal static void SetTarget(ulong color, ulong depth = 0, int mip = 0, int slice = 0, int layers = 1)
        {
            s_State.Color = color;
            s_State.Colors = null;
            s_State.Depth = depth;
            s_State.Mip = mip;
            s_State.Slice = slice;
            s_State.Layers = layers;
            s_State.Viewport = new Rect(0, 0, float.MaxValue, float.MaxValue);
            U8(SetTargetOp);
            U64(color);
            U64(depth);
            I32(mip);
            I32(Math.Max(slice, 0));
        }

        /// <summary>The layers a render target binds at once with slice -1: an array's or a
        /// volume's depth, a cube's six faces, else one.</summary>
        internal static int LayersOf(Texture texture) => texture is RenderTexture rt
            ? rt.dimension switch
            {
                UnityEngine.Rendering.TextureDimension.Tex2DArray or UnityEngine.Rendering.TextureDimension.Tex3D => Math.Max(1, rt.volumeDepth),
                UnityEngine.Rendering.TextureDimension.Cube => 6,
                UnityEngine.Rendering.TextureDimension.CubeArray => 6 * Math.Max(1, rt.volumeDepth),
                _ => 1,
            }
            : 1;

        /// <summary>Draws go into several colors at once (multiple render targets: the fragment
        /// program's outputs in order) and one depth buffer.</summary>
        internal static void SetTargets(ulong[] colors, ulong depth = 0, int mip = 0, int slice = 0, int layers = 1)
        {
            if (colors.Length <= 1)
            {
                SetTarget(colors.Length == 0 ? 0 : colors[0], depth, mip, slice, layers);
                return;
            }
            s_State.Color = colors[0];
            s_State.Colors = (ulong[])colors.Clone();
            s_State.Depth = depth;
            s_State.Mip = mip;
            s_State.Slice = slice;
            s_State.Layers = layers;
            s_State.Viewport = new Rect(0, 0, float.MaxValue, float.MaxValue);
            U8(SetTargetsOp);
            U32((uint)Math.Min(colors.Length, 8));
            for (int i = 0; i < colors.Length && i < 8; i++) U64(colors[i]);
            U64(depth);
            I32(mip);
            I32(Math.Max(slice, 0));
        }

        /// <summary>The render texture standing for the screen (0: none): the renderer stores
        /// it as the screen is.</summary>
        internal static void ScreenStandIn(ulong texture)
        {
            U8(ScreenStandInOp);
            U64(texture);
        }

        internal static void InstanceMultiplier(uint multiplier)
        {
            U8(InstanceMultiplierOp);
            U32(multiplier);
        }

        /// <summary>The next draw is instance <paramref name="index"/> of a mesh-instancing call
        /// of <paramref name="count"/> instances: its first logical instance, and the element its
        /// scalar uniforms take from the draw's property arrays (render protocol 12). Under an
        /// instance multiplier the renderer adds the call's further instances after its last one
        /// (NativeApi 65).</summary>
        internal static void MeshInstance(uint index, uint count)
        {
            U8(MeshInstanceOp);
            U32(index);
            U32(count);
        }

        /// <summary>The next draw is a BatchRendererGroup draw command's: its material's DOTS
        /// instancing variant, one instance per index of <paramref name="visible"/> (at most
        /// <see cref="Unbound.Runtime.BatchCulling.MaxInstancesPerDraw"/>), the draw's header
        /// (<see cref="Unbound.Runtime.BatchCulling.Header"/>), and the window of the batch's
        /// buffer a constant buffer reads (size 0: all of it); an indirect command's packed visible
        /// offset (else 0), with no visible instances.</summary>
        internal static void DotsDraw(in Unbound.Runtime.BatchCulling.Header header, uint windowOffset, uint windowSize, uint indirect, int* visible, int count)
        {
            U8(DotsDrawOp);
            U32(header.RenderingLayer);
            U32(header.Flags);
            F32(header.Center.x);
            F32(header.Center.y);
            F32(header.Center.z);
            F32(header.Extent.x);
            F32(header.Extent.y);
            F32(header.Extent.z);
            U32(windowOffset);
            U32(windowSize);
            U32(indirect);
            U32((uint)count);
            if (count == 0) return;
            Reserve(count * 4);
            new ReadOnlySpan<byte>(visible, count * 4).CopyTo(new Span<byte>(s_Buffer, s_Length, count * 4));
            s_Length += count * 4;
        }

        /// <summary>The next draw's <c>unity_BaseCommandID</c> (<see cref="Unbound.Runtime.DrawSetup.Command"/>).</summary>
        static int s_PendingCommand = -1;
        static readonly int s_BaseCommandId = Shader.PropertyToID("unity_BaseCommandID");

        /// <summary>Which per-object motion values the next draw takes (<see cref="NextDrawMotion"/>,
        /// <see cref="NextDrawBuiltinMotion"/>).</summary>
        enum PendingMotion : byte { None, RenderPipeline, BuiltIn }

        static PendingMotion s_PendingMotion;
        static Matrix4x4 s_MotionPrevious;
        static Vector4 s_MotionParams;
        static readonly int s_PreviousMId = Shader.PropertyToID("unity_MatrixPreviousM");
        static readonly int s_PreviousMIId = Shader.PropertyToID("unity_MatrixPreviousMI");
        static readonly int s_MotionParamsId = Shader.PropertyToID("unity_MotionVectorsParams");
        static readonly int s_BuiltinPreviousMId = Shader.PropertyToID("_PreviousM");
        static readonly int s_HasLastPositionDataId = Shader.PropertyToID("_HasLastPositionData");
        static readonly int s_ForceNoMotionId = Shader.PropertyToID("_ForceNoMotion");
        static readonly int s_MotionVectorDepthBiasId = Shader.PropertyToID("_MotionVectorDepthBias");

        /// <summary>The next draw's per-object motion vector values, which the engine sets for a
        /// renderer list with <see cref="UnityEngine.Rendering.PerObjectData.MotionVectors"/>:
        /// <c>unity_MatrixPreviousM</c> and its inverse <c>unity_MatrixPreviousMI</c>, and
        /// <c>unity_MotionVectorsParams</c> (x: last frame's vertex positions, y: 0 when the
        /// renderer forces no motion, z: depth bias, w: camera motion only). They win over a
        /// property block's values of those names.</summary>
        internal static void NextDrawMotion(in Matrix4x4 previous, Vector4 parameters)
        {
            s_PendingMotion = PendingMotion.RenderPipeline;
            s_MotionPrevious = previous;
            s_MotionParams = parameters;
        }

        /// <summary>The next draw's per-object motion vector values in the built-in renderer's
        /// motion vector pass (Hidden/Internal-MotionVectors' names): <c>_PreviousM</c>,
        /// <c>_HasLastPositionData</c> (0: no last-frame vertex positions), <c>_ForceNoMotion</c>
        /// and <c>_MotionVectorDepthBias</c> (0). They win over a property block's values of those
        /// names.</summary>
        internal static void NextDrawBuiltinMotion(in Matrix4x4 previous, bool forceNoMotion)
        {
            s_PendingMotion = PendingMotion.BuiltIn;
            s_MotionPrevious = previous;
            s_MotionParams = new Vector4(forceNoMotion ? 1f : 0f, 0f, 0f, 0f);
        }

        static bool IsMotionValue(PendingMotion motion, int id) => motion switch
        {
            PendingMotion.RenderPipeline => id == s_PreviousMId || id == s_PreviousMIId || id == s_MotionParamsId,
            PendingMotion.BuiltIn => id == s_BuiltinPreviousMId || id == s_HasLastPositionDataId || id == s_ForceNoMotionId || id == s_MotionVectorDepthBiasId,
            _ => false,
        };

        /// <summary>How the next draw runs: instances, or counts read on the GPU from an arguments
        /// buffer (indexed: DrawMeshInstancedIndirect's layout; not: DrawProceduralIndirect's);
        /// then, when it has them, the indices from a buffer it reads instead of its mesh's
        /// (DrawIndices: buffer, index size, first index, index count).</summary>
        internal static void DrawSetup(in Unbound.Runtime.DrawSetup setup)
        {
            if (setup.MeshInstance is uint index) MeshInstance(index, setup.MeshInstanceCount);
            s_PendingCommand = setup.Command ?? -1;
            U8(DrawSetupOp);
            U32(setup.Instances);
            U64(setup.Args);
            U32(setup.Offset);
            U8(setup.Indexed ? (byte)1 : (byte)0);
            if (setup.Indices == 0) return;
            U8(DrawIndicesOp);
            U64(setup.Indices);
            U8(setup.IndexSize == DrawIndex32Size ? DrawIndex32Size : DrawIndexSize);
            U32(setup.FirstIndex);
            U32(setup.IndexCount);
        }

        internal static void Clear(byte flags, Color color, float depth = 1f, uint stencil = 0)
        {
            if (s_State.Slice < 0 && s_State.Layers > 1)
            {
                // Every layer of a target bound whole (slice -1) is cleared.
                var whole = s_State;
                for (int layer = 0; layer < whole.Layers; layer++)
                {
                    if (whole.Colors != null) SetTargets(whole.Colors, whole.Depth, whole.Mip, layer);
                    else SetTarget(whole.Color, whole.Depth, whole.Mip, layer);
                    Viewport(whole.Viewport);
                    s_State.Scissor = whole.Scissor;
                    Clear(flags, color, depth, stencil);
                }
                Restore(whole);
                return;
            }
            if ((flags & (ClearDepth | ClearStencil)) != 0) SpriteMasking.Cleared(s_State.Color, s_State.Depth);
            var viewport = s_State.Viewport;
            if (s_State.Scissor is Rect scissor) Viewport(Intersect(viewport, scissor));
            if (OnScreen) ScreenWrites++;
            U8(ClearOp);
            U8(flags);
            Color(color);
            F32(depth);
            U32(stencil);
            if (s_State.Scissor.HasValue) Viewport(viewport);
        }

        /// <summary>Clear every active color attachment, retaining the MRT binding for later draws.</summary>
        internal static void Clear(byte flags, Color[] colors, float depth, uint stencil)
        {
            var state = s_State;
            int count = state.Colors?.Length ?? 1;
            if ((flags & ClearColor) == 0) { Clear(flags, colors[0], depth, stencil); return; }
            for (int i = 0; i < count; i++)
            {
                ulong color = state.Colors != null ? state.Colors[i] : state.Color;
                SetTarget(color, state.Depth == 0 ? state.Color : state.Depth, state.Mip, state.Slice, state.Layers);
                Viewport(state.Viewport);
                Clear(i == 0 ? flags : ClearColor, colors[i], depth, stencil);
            }
            Restore(state);
        }

        internal static void Scissor(Rect? rect) => s_State.Scissor = rect;

        static Rect Intersect(Rect a, Rect b)
        {
            float x = Math.Max(a.xMin, b.xMin), y = Math.Max(a.yMin, b.yMin);
            return new Rect(x, y, Math.Max(0f, Math.Min(a.xMax, b.xMax) - x), Math.Max(0f, Math.Min(a.yMax, b.yMax) - y));
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
            // Later camera commands (a command buffer's matrices) keep the flags, not the start.
            s_State.Flags = flags & ~(CameraBegin | CameraProperties);
            U8(CameraOp);
            Matrix(view);
            Matrix(projection);
            // GL.invertCulling turns the camera's culling around (a camera that already inverts,
            // a cube face's, back again).
            U32((flags ^ (s_GlFlags & CameraInvertCulling)) | (s_GlFlags & ~CameraInvertCulling));
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
        internal const int ForwardPasses = -1, AllPasses = -2, TaggedPasses = -3, BlitPasses = -4;

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

        /// <summary>The global depth bias (units, slope factor) added to every draw's Offset.</summary>
        internal static void DepthBias(float bias, float slopeBias)
        {
            U8(DepthBiasOp);
            F32(bias);
            F32(slopeBias);
        }

        /// <summary>The culling of the draws that follow, overriding their passes' (and nothing
        /// else of their state), or none: a TwoSided shadow caster's.</summary>
        internal static void CullOverride(UnityEngine.Rendering.CullMode? cull)
        {
            if (!cull.HasValue)
            {
                StateOverride(null);
                return;
            }
            StateOverride(new UnityEngine.Rendering.RenderStateBlock((UnityEngine.Rendering.RenderStateMask)CullOnlyMask)
            {
                rasterState = new UnityEngine.Rendering.RasterState(cull.Value),
            });
        }

        /// <summary>The render state override the draws now get (the last <see cref="StateOverride"/>).</summary>
        internal static UnityEngine.Rendering.RenderStateBlock? CurrentOverride { get; private set; }

        /// <summary>A state override mask bit of Unbound's own: the raster state's culling alone.</summary>
        const int CullOnlyMask = 16;

        /// <summary>The render state overrides of the draws that follow (a renderer list's
        /// RenderStateBlock), or none.</summary>
        internal static void StateOverride(UnityEngine.Rendering.RenderStateBlock? block)
        {
            CurrentOverride = block;
            U8(StateOverrideOp);
            if (!block.HasValue || block.Value.mask == UnityEngine.Rendering.RenderStateMask.Nothing)
            {
                U8(0);
                return;
            }
            var b = block.Value;
            U8(1);
            U32((uint)b.mask);
            var t = b.blendState.blendState0;
            U8((byte)t.writeMask);
            U8((byte)t.sourceColorBlendMode);
            U8((byte)t.destinationColorBlendMode);
            U8((byte)t.sourceAlphaBlendMode);
            U8((byte)t.destinationAlphaBlendMode);
            U8((byte)t.colorBlendOperation);
            U8((byte)t.alphaBlendOperation);
            var r = b.rasterState;
            U8((byte)r.cullingMode);
            I32(r.offsetUnits);
            F32(r.offsetFactor);
            var d = b.depthState;
            U8(d.writeEnabled ? (byte)1 : (byte)0);
            U8((byte)d.compareFunction);
            var s = b.stencilState;
            U8(s.enabled ? (byte)1 : (byte)0);
            U8((byte)b.stencilReference);
            U8(s.readMask);
            U8(s.writeMask);
            U8((byte)s.compareFunctionFront);
            U8((byte)s.passOperationFront);
            U8((byte)s.failOperationFront);
            U8((byte)s.zFailOperationFront);
            U8((byte)s.compareFunctionBack);
            U8((byte)s.passOperationBack);
            U8((byte)s.failOperationBack);
            U8((byte)s.zFailOperationBack);
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
            /// <summary>An int's value, exact (a float holds 24 bits of it).</summary>
            internal int Int;
            internal Matrix4x4 Matrix;
            internal ulong Texture;
            internal uint Components;
            internal float[] Array;
            /// <summary>A shared array (<see cref="SharedArrayKind"/>): defined in the stream, or
            /// referenced when it is already (decided when written: resolving can hand over).</summary>
            internal SharedArray Shared;
        }

        static readonly List<PropertyValue> s_Properties = new List<PropertyValue>();

        static void Resolve(Dictionary<int, object> properties)
        {
            s_Properties.Clear();
            // An indirect draw's command index wins over a block's value of the same name.
            int command = s_PendingCommand;
            s_PendingCommand = -1;
            if (command >= 0)
                s_Properties.Add(new PropertyValue { Name = "unity_BaseCommandID", Kind = IntKind, Int = command });
            var motion = s_PendingMotion;
            s_PendingMotion = PendingMotion.None;
            if (motion == PendingMotion.RenderPipeline)
            {
                s_Properties.Add(new PropertyValue { Name = "unity_MatrixPreviousM", Kind = MatrixKind, Matrix = s_MotionPrevious });
                s_Properties.Add(new PropertyValue { Name = "unity_MatrixPreviousMI", Kind = MatrixKind, Matrix = s_MotionPrevious.inverseDoublePrecision });
                s_Properties.Add(new PropertyValue { Name = "unity_MotionVectorsParams", Kind = VectorKind, Vector = s_MotionParams });
            }
            else if (motion == PendingMotion.BuiltIn)
            {
                s_Properties.Add(new PropertyValue { Name = "_PreviousM", Kind = MatrixKind, Matrix = s_MotionPrevious });
                s_Properties.Add(new PropertyValue { Name = "_HasLastPositionData", Kind = FloatKind });
                s_Properties.Add(new PropertyValue { Name = "_ForceNoMotion", Kind = FloatKind, Vector = s_MotionParams });
                s_Properties.Add(new PropertyValue { Name = "_MotionVectorDepthBias", Kind = FloatKind });
            }
            if (properties == null) return;
            foreach (var p in properties)
            {
                if (command >= 0 && p.Key == s_BaseCommandId) continue;
                if (IsMotionValue(motion, p.Key)) continue;
                var name = Shader.IdToName(p.Key);
                if (name == null) continue;
                var value = new PropertyValue { Name = name };
                switch (p.Value)
                {
                    case float f: value.Kind = FloatKind; value.Vector = new Vector4(f, 0, 0, 0); break;
                    case int i: value.Kind = IntKind; value.Int = i; break;
                    case Vector4 v: value.Kind = VectorKind; value.Vector = v; break;
                    case Color c: value.Kind = VectorKind; value.Vector = c; break;
                    case Matrix4x4 m: value.Kind = MatrixKind; value.Matrix = m; break;
                    case Texture t: value.Kind = TextureKind; value.Texture = TextureHandles.Of(t); break;
                    case ComputeBuffer or GraphicsBuffer: value.Kind = BufferKind; value.Texture = Material.BufferHandle(p.Value); break;
                    case GraphicsBufferHandle h: value.Kind = BufferKind; value.Texture = h.value; break;
                    case SharedArray shared: value.Kind = SharedArrayKind; value.Shared = shared; break;
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
            ulong mainTexture = 0, Rect? clip = null, Dictionary<int, object> properties = null, bool rawWinding = false, bool flipWinding = false)
        {
            if (s_State.Scissor is Rect scissor) clip = clip.HasValue ? Intersect(clip.Value, scissor) : scissor;
            Resolve(properties);
            if (OnScreen) ScreenWrites++;
            U8(DrawOp);
            U64(mesh);
            I32(submesh);
            U64(material);
            I32(pass);
            Matrix(model);
            Color(color);
            U64(mainTexture);
            // Winding flags also retain a reflection baked into world-space renderer geometry.
            U8((byte)((clip.HasValue ? 1 : 0) | (rawWinding ? 2 : 0) | (flipWinding ? 4 : 0)));
            Rect(clip ?? default);
            U32((uint)s_Properties.Count);
            foreach (var p in s_Properties)
            {
                String(p.Name);
                if (p.Kind == SharedArrayKind && s_SharedArrays.TryGetValue(p.Shared, out uint id))
                {
                    U8(ArrayRefKind);
                    U32(id);
                    continue;
                }
                U8(p.Kind);
                switch (p.Kind)
                {
                    case FloatKind: F32(p.Vector.x); break;
                    case IntKind: I32(p.Int); break;
                    case VectorKind: Vec4(p.Vector); break;
                    case MatrixKind: Matrix(p.Matrix); break;
                    case TextureKind: U64(p.Texture); Vec4(new Vector4(1, 1, 0, 0)); break;
                    case BufferKind: U64(p.Texture); break;
                    case ArrayKind:
                        Floats(p.Components, p.Array);
                        break;
                    case SharedArrayKind:
                        uint next = (uint)s_SharedArrays.Count;
                        s_SharedArrays.Add(p.Shared, next);
                        U32(next);
                        Floats(p.Shared.Components, p.Shared.Data);
                        break;
                }
            }
        }

        /// <summary>An array's components per element, its length and its floats.</summary>
        static void Floats(uint components, float[] data)
        {
            U32(components);
            U32((uint)data.Length);
            Reserve(data.Length * 4);
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan()).CopyTo(new Span<byte>(s_Buffer, s_Length, data.Length * 4));
            s_Length += data.Length * 4;
        }

        /// <summary>A global array: <paramref name="components"/> floats per element.</summary>
        internal static void GlobalArray(string name, uint components, float[] data)
        {
            U8(GlobalOp);
            String(name);
            U8(ArrayKind);
            Floats(components, data);
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

        internal static void MeshBuffers(ulong mesh, GpuBufferStorage[] streams, GpuBufferStorage indices,
            UnityEngine.Rendering.VertexAttributeDescriptor[] layout, Mesh.MeshDataStore data, byte sourceSlot = 0, uint vertexMask = uint.MaxValue)
        {
            U8(MeshBuffersOp); U64(mesh); U8(sourceSlot); U32(vertexMask); U32((uint)(data?.VertexCount ?? 0));
            for (int i=0;i<4;i++) { U64(streams?[i]?.Handle ?? 0); U32((uint)(data?.Stride(i) ?? 0)); }
            U32((uint)layout.Length);
            foreach (var d in layout) { U32((uint)d.attribute); U32((uint)d.format); U32((uint)d.dimension); U32((uint)d.stream); }
            U64(indices?.Handle ?? 0); U32(data?.IndexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4u : 2u); U32((uint)(data?.IndexCount ?? 0));
            U32((uint)(data?.SubMeshes.Count ?? 0));
            if (data != null)
                foreach (var sub in data.SubMeshes) { U32((uint)sub.indexStart); U32((uint)sub.indexCount); U32((uint)sub.topology); }
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

        /// <summary>Copies <paramref name="size"/> bytes between buffers.</summary>
        internal static void CopyBuffer(ulong source, long sourceOffset, ulong destination, long destinationOffset, long size)
        {
            U8(CopyBufferOp);
            U64(source);
            U64((ulong)sourceOffset);
            U64(destination);
            U64((ulong)destinationOffset);
            U64((ulong)size);
        }

        /// <summary>Sets the hidden counter of an Append or Counter buffer.</summary>
        internal static void SetCounter(ulong buffer, uint value)
        {
            U8(SetCounterOp);
            U64(buffer);
            U32(value);
        }

        /// <summary>Writes the hidden counter of an Append or Counter buffer into another buffer.</summary>
        internal static void CopyCounter(ulong source, ulong destination, uint offset)
        {
            U8(CopyCounterOp);
            U64(source);
            U64(destination);
            U32(offset);
        }

        /// <summary>Runs a kernel of a <c>.compute</c> file over thread groups; with an
        /// <paramref name="args"/> buffer (not 0) the group counts are its three <c>uint</c>s at
        /// <paramref name="argsOffset"/>, read on the GPU.</summary>
        internal static void Dispatch(string file, string kernel, string keywords, int x, int y, int z, List<DispatchProperty> properties,
            ulong args = 0, uint argsOffset = 0)
        {
            if (args != 0)
            {
                U8(DispatchIndirectOp);
                U64(args);
                U32(argsOffset);
            }
            else U8(DispatchOp);
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
                        Floats(p.Components, p.Array);
                        break;
                }
            }
        }

        internal static void ResolveTexture(ulong texture, ulong destination)
        {
            U8(ResolveTextureOp);
            U64(texture);
            U64(destination);
        }

        /// <summary>A native plugin's rendering callback, run in stream order. The stream is handed
        /// over at once: the callback has run when the call that issued it returns, as in the
        /// Editor, whose callbacks run on the main thread (tests/projects/nativegfx).</summary>
        /// <remarks>Without the native side (tests) the commands stay recorded.</remarks>
        internal static void PluginEvent(IntPtr callback, int eventId)
        {
            U8(PluginOp);
            U8(PluginEventKind);
            U64((ulong)callback);
            I32(eventId);
            HandOver();
        }

        /// <inheritdoc cref="PluginEvent"/>
        internal static void PluginEventAndData(IntPtr callback, int eventId, IntPtr data)
        {
            U8(PluginOp);
            U8(PluginEventAndDataKind);
            U64((ulong)callback);
            I32(eventId);
            U64((ulong)data);
            HandOver();
        }

        /// <summary>A plugin's texture update: begin and end events around the upload of the
        /// plugin's data into the texture's first level.</summary>
        internal static void PluginTextureUpdate(IntPtr callback, ulong texture, uint userData, bool v2, uint format, uint bytesPerPixel)
        {
            U8(PluginOp);
            U8(v2 ? PluginTextureUpdateV2Kind : PluginTextureUpdateV1Kind);
            U64((ulong)callback);
            U64(texture);
            U32(userData);
            U32(format);
            U32(bytesPerPixel);
            HandOver();
        }

        /// <summary>A plugin's custom blit into the current target.</summary>
        internal static void PluginCustomBlit(IntPtr callback, uint command, uint source, IntPtr destination, uint param, uint flags)
        {
            U8(PluginOp);
            U8(PluginCustomBlitKind);
            U64((ulong)callback);
            U32(command);
            U32(source);
            U64((ulong)destination);
            U32(param);
            U32(flags);
            HandOver();
        }

        /// <summary>Hands the commands to the renderer now, when there is one.</summary>
        static void HandOver()
        {
            if (Native.IsReady) Flush();
        }

        internal static void GenerateMips(ulong texture)
        {
            U8(GenerateMipsOp);
            U64(texture);
        }

        /// <summary>Copies texels between textures (a negative width copies the whole level).</summary>
        internal static void CopyTexture(ulong source, int sourceSlice, int sourceMip, RectInt sourceRect,
            ulong destination, int destinationSlice, int destinationMip, int destinationX, int destinationY)
        {
            if (destination == 0) ScreenWrites++;
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

        /// <summary>Copies the whole depth buffer of a render texture (0: the screen) into another
        /// of its size (0: the screen's); the destination's stencil is cleared.</summary>
        internal static void CopyDepth(ulong source, ulong destination)
        {
            if (destination == 0) ScreenWrites++;
            U8(CopyDepthOp);
            U64(source);
            U64(destination);
        }
    }
}
