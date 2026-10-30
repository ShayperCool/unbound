using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering
{
    /// <summary>A render target named by a texture, a built-in target or a shader property (a
    /// command buffer's temporary): resolved when the command executes.</summary>
    public partial struct RenderTargetIdentifier : IEquatable<RenderTargetIdentifier>
    {
        public const int AllDepthSlices = -1;

        BuiltinRenderTextureType m_Type;
        int m_NameID;
        int m_InstanceID;
        IntPtr m_BufferPointer;
        int m_MipLevel;
        CubemapFace m_CubeFace;
        int m_DepthSlice;

        public RenderTargetIdentifier(BuiltinRenderTextureType type)
        {
            m_Type = type;
            m_NameID = -1;
            m_InstanceID = 0;
            m_BufferPointer = IntPtr.Zero;
            m_MipLevel = 0;
            m_CubeFace = CubemapFace.Unknown;
            m_DepthSlice = 0;
        }

        public RenderTargetIdentifier(BuiltinRenderTextureType type, [Internal.DefaultValue("0")] int mipLevel, [Internal.DefaultValue("CubemapFace.Unknown")] CubemapFace cubeFace, [Internal.DefaultValue("0")] int depthSlice)
            : this(type)
        {
            m_MipLevel = mipLevel;
            m_CubeFace = cubeFace;
            m_DepthSlice = depthSlice;
        }

        public RenderTargetIdentifier(string name) : this(Shader.PropertyToID(name)) { }

        public RenderTargetIdentifier(string name, [Internal.DefaultValue("0")] int mipLevel, [Internal.DefaultValue("CubemapFace.Unknown")] CubemapFace cubeFace, [Internal.DefaultValue("0")] int depthSlice)
            : this(Shader.PropertyToID(name), mipLevel, cubeFace, depthSlice) { }

        public RenderTargetIdentifier(int nameID) : this(BuiltinRenderTextureType.PropertyName) => m_NameID = nameID;

        public RenderTargetIdentifier(int nameID, [Internal.DefaultValue("0")] int mipLevel, [Internal.DefaultValue("CubemapFace.Unknown")] CubemapFace cubeFace, [Internal.DefaultValue("0")] int depthSlice)
            : this(nameID)
        {
            m_MipLevel = mipLevel;
            m_CubeFace = cubeFace;
            m_DepthSlice = depthSlice;
        }

        public RenderTargetIdentifier(RenderTargetIdentifier renderTargetIdentifier, int mipLevel, [Internal.DefaultValue("CubemapFace.Unknown")] CubemapFace cubeFace, [Internal.DefaultValue("0")] int depthSlice)
        {
            this = renderTargetIdentifier;
            m_MipLevel = mipLevel;
            m_CubeFace = cubeFace;
            m_DepthSlice = depthSlice;
        }

        public RenderTargetIdentifier(Texture tex)
            : this(tex == null ? BuiltinRenderTextureType.None : tex is RenderTexture ? BuiltinRenderTextureType.RenderTexture : BuiltinRenderTextureType.BindableTexture)
        {
            m_InstanceID = tex != null ? tex.GetInstanceID() : 0;
        }

        public RenderTargetIdentifier(Texture tex, [Internal.DefaultValue("0")] int mipLevel, [Internal.DefaultValue("CubemapFace.Unknown")] CubemapFace cubeFace, [Internal.DefaultValue("0")] int depthSlice)
            : this(tex)
        {
            m_MipLevel = mipLevel;
            m_CubeFace = cubeFace;
            m_DepthSlice = depthSlice;
        }

        public static implicit operator RenderTargetIdentifier(BuiltinRenderTextureType type) => new RenderTargetIdentifier(type);
        public static implicit operator RenderTargetIdentifier(string name) => new RenderTargetIdentifier(name);
        public static implicit operator RenderTargetIdentifier(int nameID) => new RenderTargetIdentifier(nameID);
        public static implicit operator RenderTargetIdentifier(Texture tex) => new RenderTargetIdentifier(tex);

        internal BuiltinRenderTextureType Type => m_Type;
        internal int NameID => m_NameID;
        internal int InstanceID => m_InstanceID;
        internal int MipLevel => m_MipLevel;
        internal int Slice => m_CubeFace != CubemapFace.Unknown ? (int)m_CubeFace : Math.Max(0, m_DepthSlice);

        public override string ToString() => $"Type {m_Type} NameID {m_NameID} InstanceID {m_InstanceID}";

        public override int GetHashCode() => HashCode.Combine(m_Type, m_NameID, m_InstanceID, m_BufferPointer, m_MipLevel, m_CubeFace, m_DepthSlice);

        public bool Equals(RenderTargetIdentifier rhs) =>
            m_Type == rhs.m_Type && m_NameID == rhs.m_NameID && m_InstanceID == rhs.m_InstanceID && m_BufferPointer == rhs.m_BufferPointer
            && m_MipLevel == rhs.m_MipLevel && m_CubeFace == rhs.m_CubeFace && m_DepthSlice == rhs.m_DepthSlice;

        public override bool Equals(object obj) => obj is RenderTargetIdentifier other && Equals(other);

        public static bool operator ==(RenderTargetIdentifier lhs, RenderTargetIdentifier rhs) => lhs.Equals(rhs);
        public static bool operator !=(RenderTargetIdentifier lhs, RenderTargetIdentifier rhs) => !lhs.Equals(rhs);
    }

    /// <summary>
    /// A list of rendering commands, recorded and executed later (Graphics.ExecuteCommandBuffer, a
    /// camera event, a light event). Each command runs on the stream when executed, with targets
    /// resolved then.
    /// </summary>
    public partial class CommandBuffer : IDisposable
    {
        /// <summary>What a command needs when it runs.</summary>
        internal sealed class Context
        {
            /// <summary>The camera rendering (null for Graphics.ExecuteCommandBuffer).</summary>
            internal Camera Camera;
            /// <summary>The camera's target: its render texture, or null for the screen.</summary>
            internal RenderTexture CameraTarget;
        }

        /// <summary>Temporaries made by GetTemporaryRT, by property id, until released or the end
        /// of the camera's rendering (or of Graphics.ExecuteCommandBuffer).</summary>
        internal static readonly Dictionary<int, RenderTexture> NamedTemporaries = new Dictionary<int, RenderTexture>();

        readonly List<Action<Context>> m_Commands = new List<Action<Context>>();
        string m_Name = "";
        static bool s_WarnedScissor, s_WarnedPluginEvent;

        public CommandBuffer() { }

        public string name { get => m_Name; set => m_Name = value ?? ""; }

        public int sizeInBytes => m_Commands.Count * 64;

        public void Clear()
        {
            m_Commands.Clear();
            ReleaseOwnedArrays();
        }

        public void Dispose() => Clear();

        public void Release() => Dispose();

        void Add(Action<Context> command) => m_Commands.Add(command);

        /// <summary>Runs the commands now (Graphics.ExecuteCommandBuffer): temporaries it does not
        /// release are released after.</summary>
        internal void Execute(Camera camera)
        {
            ExecuteIn(new Context { Camera = camera, CameraTarget = camera != null ? camera.targetTexture : null });
            if (camera == null) ReleaseNamedTemporaries();
        }

        /// <summary>Runs the commands now within a camera's rendering.</summary>
        internal void ExecuteIn(Context context)
        {
            foreach (var command in m_Commands)
            {
                try { command(context); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        internal static void ReleaseNamedTemporaries()
        {
            foreach (var t in NamedTemporaries.Values) RenderTexture.ReleaseTemporary(t);
            NamedTemporaries.Clear();
        }

        /// <summary>The texture an identifier names when the command runs (null: the screen).</summary>
        internal static Texture Resolve(in RenderTargetIdentifier id, Context context)
        {
            switch (id.Type)
            {
                case BuiltinRenderTextureType.CameraTarget:
                // The camera target's depth buffer: as a target it is the camera target's own.
                case BuiltinRenderTextureType.Depth:
                    return context.CameraTarget;
                case BuiltinRenderTextureType.CurrentActive:
                    return RenderTexture.active;
                case BuiltinRenderTextureType.PropertyName:
                    if (NamedTemporaries.TryGetValue(id.NameID, out var temporary)) return temporary;
                    return Shader.GetGlobalTexture(id.NameID);
                case BuiltinRenderTextureType.RenderTexture:
                case BuiltinRenderTextureType.BindableTexture:
                    return ObjectRegistry.FindById(id.InstanceID) as Texture;
                case BuiltinRenderTextureType.None:
                    return null;
                default:
                    Debug.LogWarning($"Unbound: the built-in render target {id.Type} is not supported yet; the camera target stands in.");
                    return context.CameraTarget;
            }
        }

        static ulong HandleOf(in RenderTargetIdentifier id, Context context) => TextureHandles.Of(Resolve(id, context));

        // ------------------------------------------------------------ targets

        public void SetRenderTarget(RenderTargetIdentifier rt) => SetRenderTarget(rt, 0, CubemapFace.Unknown, 0);
        public void SetRenderTarget(RenderTargetIdentifier rt, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction) => SetRenderTarget(rt);
        public void SetRenderTarget(RenderTargetIdentifier rt, RenderBufferLoadAction colorLoadAction, RenderBufferStoreAction colorStoreAction,
            RenderBufferLoadAction depthLoadAction, RenderBufferStoreAction depthStoreAction) => SetRenderTarget(rt);
        public void SetRenderTarget(RenderTargetIdentifier rt, int mipLevel) => SetRenderTarget(rt, mipLevel, CubemapFace.Unknown, 0);
        public void SetRenderTarget(RenderTargetIdentifier rt, int mipLevel, CubemapFace cubemapFace) => SetRenderTarget(rt, mipLevel, cubemapFace, 0);

        public void SetRenderTarget(RenderTargetIdentifier rt, int mipLevel, CubemapFace cubemapFace, int depthSlice) => Add(c =>
        {
            var color = Resolve(rt, c);
            ulong handle = TextureHandles.Of(color);
            RenderTexture.SetActiveQuietly(color as RenderTexture);
            GfxStream.SetTarget(handle, 0, mipLevel, cubemapFace != CubemapFace.Unknown ? (int)cubemapFace : depthSlice);
        });

        public void SetRenderTarget(RenderTargetIdentifier color, RenderTargetIdentifier depth) => SetRenderTarget(color, depth, 0, CubemapFace.Unknown, 0);
        public void SetRenderTarget(RenderTargetIdentifier color, RenderTargetIdentifier depth, int mipLevel) => SetRenderTarget(color, depth, mipLevel, CubemapFace.Unknown, 0);
        public void SetRenderTarget(RenderTargetIdentifier color, RenderTargetIdentifier depth, int mipLevel, CubemapFace cubemapFace) => SetRenderTarget(color, depth, mipLevel, cubemapFace, 0);

        public void SetRenderTarget(RenderTargetIdentifier color, RenderTargetIdentifier depth, int mipLevel, CubemapFace cubemapFace, int depthSlice) => Add(c =>
        {
            var colorTexture = Resolve(color, c);
            ulong colorHandle = TextureHandles.Of(colorTexture);
            ulong depthHandle = depth == color ? 0 : TextureHandles.Of(Resolve(depth, c));
            RenderTexture.SetActiveQuietly(colorTexture as RenderTexture);
            GfxStream.SetTarget(colorHandle, depthHandle, mipLevel, cubemapFace != CubemapFace.Unknown ? (int)cubemapFace : depthSlice);
        });

        public void SetRenderTarget(RenderTargetIdentifier color, RenderBufferLoadAction colorLoadAction, RenderBufferStoreAction colorStoreAction,
            RenderTargetIdentifier depth, RenderBufferLoadAction depthLoadAction, RenderBufferStoreAction depthStoreAction) => SetRenderTarget(color, depth);

        public void SetRenderTarget(RenderTargetIdentifier[] colors, RenderTargetIdentifier depth) =>
            SetRenderTarget(colors, depth, 0, CubemapFace.Unknown, 0);

        /// <summary>Several colors at once (the fragment program's outputs in order) and a depth buffer.</summary>
        public void SetRenderTarget(RenderTargetIdentifier[] colors, RenderTargetIdentifier depth, int mipLevel, CubemapFace cubemapFace, int depthSlice)
        {
            if (colors == null || colors.Length == 0) throw new ArgumentException("colors must not be empty");
            var copy = (RenderTargetIdentifier[])colors.Clone();
            Add(c =>
            {
                var first = Resolve(copy[0], c);
                var handles = new ulong[copy.Length];
                for (int i = 0; i < copy.Length; i++) handles[i] = TextureHandles.Of(Resolve(copy[i], c));
                ulong depthHandle = depth == copy[0] ? 0 : TextureHandles.Of(Resolve(depth, c));
                RenderTexture.SetActiveQuietly(first as RenderTexture);
                GfxStream.SetTargets(handles, depthHandle, mipLevel, cubemapFace != CubemapFace.Unknown ? (int)cubemapFace : depthSlice);
            });
        }

        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor) => ClearRenderTarget(clearDepth, clearColor, backgroundColor, 1f, 0);
        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor, float depth) => ClearRenderTarget(clearDepth, clearColor, backgroundColor, depth, 0);

        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor, [Internal.DefaultValue("1.0f")] float depth, [Internal.DefaultValue("0")] uint stencil) =>
            ClearRenderTarget((clearColor ? RTClearFlags.Color : 0) | (clearDepth ? RTClearFlags.DepthStencil : 0), backgroundColor, depth, stencil);

        public void ClearRenderTarget(RTClearFlags clearFlags, Color backgroundColor, [Internal.DefaultValue("1.0f")] float depth, [Internal.DefaultValue("0")] uint stencil) => Add(c =>
        {
            byte flags = (byte)(((clearFlags & RTClearFlags.Color) != 0 ? GfxStream.ClearColor : 0)
                | ((clearFlags & RTClearFlags.Depth) != 0 ? GfxStream.ClearDepth : 0)
                | ((clearFlags & RTClearFlags.Stencil) != 0 ? GfxStream.ClearStencil : 0));
            if (flags != 0) GfxStream.Clear(flags, backgroundColor, depth, stencil);
        });

        public void ClearRenderTarget(RTClearFlags clearFlags, Color[] backgroundColors, [Internal.DefaultValue("1.0f")] float depth, [Internal.DefaultValue("0")] uint stencil) =>
            ClearRenderTarget(clearFlags, backgroundColors != null && backgroundColors.Length > 0 ? backgroundColors[0] : Color.black, depth, stencil);

        public void SetViewport(Rect pixelRect) => Add(c => GfxStream.Viewport(pixelRect));

        public void EnableScissorRect(Rect scissor) => Add(c =>
        {
            if (s_WarnedScissor) return;
            s_WarnedScissor = true;
            Debug.LogWarning("Unbound: CommandBuffer.EnableScissorRect is not supported yet; draws are not clipped.");
        });

        [Unbound.EditorFaithful("no-op is equivalent: EnableScissorRect is not supported (it warns), so draws are never clipped")]
        public void DisableScissorRect() { }

        public void SetViewProjectionMatrices(Matrix4x4 view, Matrix4x4 proj) => Add(c => GfxStream.Camera(view, proj, GfxStream.Current.Flags));
        public void SetViewMatrix(Matrix4x4 view) => Add(c => GfxStream.Camera(view, GfxStream.Current.Projection, GfxStream.Current.Flags));
        public void SetProjectionMatrix(Matrix4x4 proj) => Add(c => GfxStream.Camera(GfxStream.Current.View, proj, GfxStream.Current.Flags));

        // ------------------------------------------------------------ temporaries

        /// <summary>A negative width or height divides the camera's pixel size.</summary>
        public void GetTemporaryRT(int nameID, RenderTextureDescriptor desc, [Internal.DefaultValue("FilterMode.Point")] FilterMode filter) => Add(c =>
        {
            var d = desc;
            if (d.width < 0) d.width = Math.Max(1, (c.Camera != null ? c.Camera.pixelWidth : Screen.width) / -d.width);
            if (d.height < 0) d.height = Math.Max(1, (c.Camera != null ? c.Camera.pixelHeight : Screen.height) / -d.height);
            if (NamedTemporaries.TryGetValue(nameID, out var previous)) RenderTexture.ReleaseTemporary(previous);
            var rt = RenderTexture.GetTemporary(d);
            rt.filterMode = filter;
            NamedTemporaries[nameID] = rt;
            Shader.SetGlobalTexture(nameID, rt);
        });

        public void GetTemporaryRT(int nameID, RenderTextureDescriptor desc) => GetTemporaryRT(nameID, desc, FilterMode.Point);

        public void GetTemporaryRT(int nameID, int width, int height, [Internal.DefaultValue("0")] int depthBuffer, [Internal.DefaultValue("FilterMode.Point")] FilterMode filter,
            [Internal.DefaultValue("RenderTextureFormat.Default")] RenderTextureFormat format, [Internal.DefaultValue("RenderTextureReadWrite.Default")] RenderTextureReadWrite readWrite,
            [Internal.DefaultValue("1")] int antiAliasing, [Internal.DefaultValue("false")] bool enableRandomWrite, [Internal.DefaultValue("RenderTextureMemoryless.None")] RenderTextureMemoryless memorylessMode,
            [Internal.DefaultValue("false")] bool useDynamicScale)
        {
            var desc = new RenderTextureDescriptor(width, height, format, depthBuffer, 1, readWrite)
            {
                msaaSamples = Math.Max(1, antiAliasing),
                enableRandomWrite = enableRandomWrite,
                memoryless = memorylessMode,
                useDynamicScale = useDynamicScale,
            };
            GetTemporaryRT(nameID, desc, filter);
        }

        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, RenderTextureFormat format, RenderTextureReadWrite readWrite, int antiAliasing, bool enableRandomWrite, RenderTextureMemoryless memorylessMode) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, readWrite, antiAliasing, enableRandomWrite, memorylessMode, false);
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, RenderTextureFormat format, RenderTextureReadWrite readWrite, int antiAliasing, bool enableRandomWrite) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, readWrite, antiAliasing, enableRandomWrite, RenderTextureMemoryless.None, false);
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, RenderTextureFormat format, RenderTextureReadWrite readWrite, int antiAliasing) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, readWrite, antiAliasing, false, RenderTextureMemoryless.None, false);
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, RenderTextureFormat format, RenderTextureReadWrite readWrite) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, readWrite, 1, false, RenderTextureMemoryless.None, false);
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, RenderTextureFormat format) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, RenderTextureReadWrite.Default, 1, false, RenderTextureMemoryless.None, false);
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1, false, RenderTextureMemoryless.None, false);
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1, false, RenderTextureMemoryless.None, false);
        public void GetTemporaryRT(int nameID, int width, int height) =>
            GetTemporaryRT(nameID, width, height, 0, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1, false, RenderTextureMemoryless.None, false);

        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, GraphicsFormat format, int antiAliasing, bool enableRandomWrite, RenderTextureMemoryless memorylessMode, bool useDynamicScale)
        {
            var desc = new RenderTextureDescriptor(width, height, format, depthBuffer)
            {
                msaaSamples = Math.Max(1, antiAliasing),
                enableRandomWrite = enableRandomWrite,
                memoryless = memorylessMode,
                useDynamicScale = useDynamicScale,
            };
            GetTemporaryRT(nameID, desc, filter);
        }

        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, GraphicsFormat format, int antiAliasing, bool enableRandomWrite, RenderTextureMemoryless memorylessMode) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, antiAliasing, enableRandomWrite, memorylessMode, false);
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, GraphicsFormat format, int antiAliasing, bool enableRandomWrite) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, antiAliasing, enableRandomWrite, RenderTextureMemoryless.None, false);
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, GraphicsFormat format, int antiAliasing) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, antiAliasing, false, RenderTextureMemoryless.None, false);
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, GraphicsFormat format) =>
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, 1, false, RenderTextureMemoryless.None, false);

        public void ReleaseTemporaryRT(int nameID) => Add(c =>
        {
            if (NamedTemporaries.TryGetValue(nameID, out var rt))
            {
                RenderTexture.ReleaseTemporary(rt);
                NamedTemporaries.Remove(nameID);
            }
        });

        // ------------------------------------------------------------ drawing

        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass, MaterialPropertyBlock properties)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            var values = properties != null && !properties.isEmpty ? new Dictionary<int, object>(properties.m_Values) : null;
            Add(c =>
            {
                ulong meshHandle = mesh.Sync();
                ulong materialHandle = material.Sync();
                GfxStream.Draw(meshHandle, submeshIndex, materialHandle, shaderPass < 0 ? GfxStream.AllPasses : shaderPass, matrix, Color.white, properties: values);
            });
        }

        /// <summary>The first <paramref name="count"/> meshes, each with its matrix and submesh, in one
        /// material (the 2D renderer's light batches): as many draws.</summary>
        public void DrawMultipleMeshes(Matrix4x4[] matrices, Mesh[] meshes, int[] subsetIndices, int count, Material material, int shaderPass, MaterialPropertyBlock properties)
        {
            if (matrices == null) throw new ArgumentNullException(nameof(matrices));
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));
            if (subsetIndices == null) throw new ArgumentNullException(nameof(subsetIndices));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (count < 0 || count > matrices.Length || count > meshes.Length || count > subsetIndices.Length)
                throw new ArgumentOutOfRangeException(nameof(count), "count exceeds the arrays' lengths.");
            for (int i = 0; i < count; i++)
                if (meshes[i] != null) DrawMesh(meshes[i], matrices[i], material, subsetIndices[i], shaderPass, properties);
        }

        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass) => DrawMesh(mesh, matrix, material, submeshIndex, shaderPass, null);
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex) => DrawMesh(mesh, matrix, material, submeshIndex, -1, null);
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material) => DrawMesh(mesh, matrix, material, 0, -1, null);

        public void DrawRenderer(Renderer renderer, Material material, [Internal.DefaultValue("0")] int submeshIndex, [Internal.DefaultValue("-1")] int shaderPass)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (material == null) throw new ArgumentNullException(nameof(material));
            Add(c =>
            {
                int pass = shaderPass < 0 ? GfxStream.AllPasses : shaderPass;
                var mesh = renderer.DrawGeometry(out var model);
                if (mesh != null)
                {
                    GfxStream.Draw(mesh.Sync(), submeshIndex, material.Sync(), pass, model, Color.white,
                        properties: renderer.BlockFor(submeshIndex)?.m_Values);
                    return;
                }
                // Other renderers (sprites, lines, ...) draw the geometry they draw for a camera,
                // with their texture and color, in the given material (URP's 2D shadows unshadow
                // a caster's sprite so).
                var camera = Camera.current ?? Camera.main;
                if (camera == null) return;
                var items = new List<Unbound.Runtime.FrameBuilder.DrawItem>();
                renderer.CollectDraws(Unbound.Runtime.FrameBuilder.ContextOf(camera), items);
                ulong handle = material.Sync();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (items.Count > 1 && i != submeshIndex) continue;
                    GfxStream.Draw(item.Mesh, item.Submesh, handle, pass, item.Model, item.Color, item.MainTexture, item.ClipEnabled ? item.Clip : (Rect?)null, item.Properties);
                }
            });
        }

        public void DrawRenderer(Renderer renderer, Material material, int submeshIndex) => DrawRenderer(renderer, material, submeshIndex, -1);
        public void DrawRenderer(Renderer renderer, Material material) => DrawRenderer(renderer, material, 0, -1);

        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");
        static readonly int s_MainTexST = Shader.PropertyToID("_MainTex_ST");

        void BlitCommand(RenderTargetIdentifier? source, Texture sourceTexture, RenderTargetIdentifier dest, Material mat, int pass, Vector2 scale, Vector2 offset) => Add(c =>
        {
            var texture = sourceTexture ?? (source.HasValue ? Resolve(source.Value, c) : null);
            var properties = new Dictionary<int, object>();
            if (texture != null) properties[s_MainTex] = texture;
            if (scale != Vector2.one || offset != Vector2.zero) properties[s_MainTexST] = new Vector4(scale.x, scale.y, offset.x, offset.y);
            var target = Resolve(dest, c);
            Immediate.BlitWith(TextureHandles.Of(target), mat ?? Immediate.BlitCopy, pass, properties);
            RenderTexture.SetActiveQuietly(target as RenderTexture);
        });

        public void Blit(Texture source, RenderTargetIdentifier dest) => BlitCommand(null, source, dest, null, -1, Vector2.one, Vector2.zero);
        public void Blit(Texture source, RenderTargetIdentifier dest, Vector2 scale, Vector2 offset) => BlitCommand(null, source, dest, null, -1, scale, offset);
        public void Blit(Texture source, RenderTargetIdentifier dest, Material mat) => BlitCommand(null, source, dest, mat, -1, Vector2.one, Vector2.zero);
        public void Blit(Texture source, RenderTargetIdentifier dest, Material mat, int pass) => BlitCommand(null, source, dest, mat, pass, Vector2.one, Vector2.zero);
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest) => BlitCommand(source, null, dest, null, -1, Vector2.one, Vector2.zero);
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Vector2 scale, Vector2 offset) => BlitCommand(source, null, dest, null, -1, scale, offset);
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Material mat) => BlitCommand(source, null, dest, mat, -1, Vector2.one, Vector2.zero);
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Material mat, int pass) => BlitCommand(source, null, dest, mat, pass, Vector2.one, Vector2.zero);
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, int sourceDepthSlice, int destDepthSlice) => Blit(source, dest);
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Vector2 scale, Vector2 offset, int sourceDepthSlice, int destDepthSlice) => Blit(source, dest, scale, offset);
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Material mat, int pass, int destDepthSlice) => Blit(source, dest, mat, pass);

        public void CopyTexture(RenderTargetIdentifier src, RenderTargetIdentifier dst) => CopyTexture(src, 0, 0, -1, -1, -1, -1, dst, 0, 0, 0, 0);
        public void CopyTexture(RenderTargetIdentifier src, int srcElement, RenderTargetIdentifier dst, int dstElement) => CopyTexture(src, srcElement, 0, -1, -1, -1, -1, dst, dstElement, 0, 0, 0);
        public void CopyTexture(RenderTargetIdentifier src, int srcElement, int srcMip, RenderTargetIdentifier dst, int dstElement, int dstMip) =>
            CopyTexture(src, srcElement, srcMip, -1, -1, -1, -1, dst, dstElement, dstMip, 0, 0);

        public void CopyTexture(RenderTargetIdentifier src, int srcElement, int srcMip, int srcX, int srcY, int srcWidth, int srcHeight,
            RenderTargetIdentifier dst, int dstElement, int dstMip, int dstX, int dstY) => Add(c =>
        {
            ulong source = HandleOf(src, c), destination = HandleOf(dst, c);
            GfxStream.CopyTexture(source, srcElement, srcMip, new RectInt(srcX, srcY, srcWidth, srcHeight), destination, dstElement, dstMip, dstX, dstY);
        });

        // ------------------------------------------------------------ compute

        public void SetComputeFloatParam(ComputeShader computeShader, int nameID, float val) => Add(c => computeShader.SetFloat(nameID, val));
        public void SetComputeFloatParam(ComputeShader computeShader, string name, float val) => SetComputeFloatParam(computeShader, Shader.PropertyToID(name), val);
        public void SetComputeIntParam(ComputeShader computeShader, int nameID, int val) => Add(c => computeShader.SetInt(nameID, val));
        public void SetComputeIntParam(ComputeShader computeShader, string name, int val) => SetComputeIntParam(computeShader, Shader.PropertyToID(name), val);
        public void SetComputeVectorParam(ComputeShader computeShader, int nameID, Vector4 val) => Add(c => computeShader.SetVector(nameID, val));
        public void SetComputeVectorParam(ComputeShader computeShader, string name, Vector4 val) => SetComputeVectorParam(computeShader, Shader.PropertyToID(name), val);
        public void SetComputeMatrixParam(ComputeShader computeShader, int nameID, Matrix4x4 val) => Add(c => computeShader.SetMatrix(nameID, val));
        public void SetComputeMatrixParam(ComputeShader computeShader, string name, Matrix4x4 val) => SetComputeMatrixParam(computeShader, Shader.PropertyToID(name), val);
        public void SetComputeFloatParams(ComputeShader computeShader, int nameID, params float[] values)
        {
            var copy = (float[])values.Clone();
            Add(c => computeShader.SetFloats(nameID, copy));
        }
        public void SetComputeFloatParams(ComputeShader computeShader, string name, params float[] values) => SetComputeFloatParams(computeShader, Shader.PropertyToID(name), values);
        public void SetComputeIntParams(ComputeShader computeShader, int nameID, params int[] values)
        {
            var copy = (int[])values.Clone();
            Add(c => computeShader.SetInts(nameID, copy));
        }
        public void SetComputeIntParams(ComputeShader computeShader, string name, params int[] values) => SetComputeIntParams(computeShader, Shader.PropertyToID(name), values);
        public void SetComputeVectorArrayParam(ComputeShader computeShader, int nameID, Vector4[] values)
        {
            var copy = (Vector4[])values.Clone();
            Add(c => computeShader.SetVectorArray(nameID, copy));
        }
        public void SetComputeVectorArrayParam(ComputeShader computeShader, string name, Vector4[] values) => SetComputeVectorArrayParam(computeShader, Shader.PropertyToID(name), values);
        public void SetComputeMatrixArrayParam(ComputeShader computeShader, int nameID, Matrix4x4[] values)
        {
            var copy = (Matrix4x4[])values.Clone();
            Add(c => computeShader.SetMatrixArray(nameID, copy));
        }
        public void SetComputeMatrixArrayParam(ComputeShader computeShader, string name, Matrix4x4[] values) => SetComputeMatrixArrayParam(computeShader, Shader.PropertyToID(name), values);

        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel) =>
            Add(c => computeShader.SetTexture(kernelIndex, nameID, Resolve(rt, c), mipLevel));
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt) => SetComputeTextureParam(computeShader, kernelIndex, nameID, rt, 0);
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel, RenderTextureSubElement element) =>
            SetComputeTextureParam(computeShader, kernelIndex, nameID, rt, mipLevel);
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, string name, RenderTargetIdentifier rt) =>
            SetComputeTextureParam(computeShader, kernelIndex, Shader.PropertyToID(name), rt, 0);
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, string name, RenderTargetIdentifier rt, int mipLevel) =>
            SetComputeTextureParam(computeShader, kernelIndex, Shader.PropertyToID(name), rt, mipLevel);
        public void SetComputeTextureParam(ComputeShader computeShader, int kernelIndex, string name, RenderTargetIdentifier rt, int mipLevel, RenderTextureSubElement element) =>
            SetComputeTextureParam(computeShader, kernelIndex, Shader.PropertyToID(name), rt, mipLevel);

        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, ComputeBuffer buffer) => Add(c => computeShader.SetBuffer(kernelIndex, nameID, buffer));
        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, string name, ComputeBuffer buffer) => SetComputeBufferParam(computeShader, kernelIndex, Shader.PropertyToID(name), buffer);
        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, int nameID, GraphicsBuffer buffer) => Add(c => computeShader.SetBuffer(kernelIndex, nameID, buffer));
        public void SetComputeBufferParam(ComputeShader computeShader, int kernelIndex, string name, GraphicsBuffer buffer) => SetComputeBufferParam(computeShader, kernelIndex, Shader.PropertyToID(name), buffer);

        public void DispatchCompute(ComputeShader computeShader, int kernelIndex, int threadGroupsX, int threadGroupsY, int threadGroupsZ) =>
            Add(c => computeShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ));

        public void SetGlobalBuffer(int nameID, ComputeBuffer value) => Add(c => Shader.SetGlobalBuffer(nameID, value));
        public void SetGlobalBuffer(string name, ComputeBuffer value) => SetGlobalBuffer(Shader.PropertyToID(name), value);
        public void SetGlobalBuffer(int nameID, GraphicsBuffer value) => Add(c => Shader.SetGlobalBuffer(nameID, value));
        public void SetGlobalBuffer(string name, GraphicsBuffer value) => SetGlobalBuffer(Shader.PropertyToID(name), value);
        public void SetGlobalConstantBuffer(ComputeBuffer buffer, int nameID, int offset, int size) => Add(c => Shader.SetGlobalConstantBuffer(nameID, buffer, offset, size));
        public void SetGlobalConstantBuffer(ComputeBuffer buffer, string name, int offset, int size) => SetGlobalConstantBuffer(buffer, Shader.PropertyToID(name), offset, size);
        public void SetGlobalConstantBuffer(GraphicsBuffer buffer, int nameID, int offset, int size) => Add(c => Shader.SetGlobalConstantBuffer(nameID, buffer, offset, size));
        public void SetGlobalConstantBuffer(GraphicsBuffer buffer, string name, int offset, int size) => SetGlobalConstantBuffer(buffer, Shader.PropertyToID(name), offset, size);

        public void SetBufferData(GraphicsBuffer buffer, Array data)
        {
            var copy = (Array)data.Clone();
            Add(c => buffer.SetData(copy));
        }
        public void SetBufferData(GraphicsBuffer buffer, Array data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count)
        {
            var copy = (Array)data.Clone();
            Add(c => buffer.SetData(copy, managedBufferStartIndex, graphicsBufferStartIndex, count));
        }
        public void SetBufferData(ComputeBuffer buffer, Array data)
        {
            var copy = (Array)data.Clone();
            Add(c => buffer.SetData(copy));
        }
        public void SetBufferData(ComputeBuffer buffer, Array data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count)
        {
            var copy = (Array)data.Clone();
            Add(c => buffer.SetData(copy, managedBufferStartIndex, graphicsBufferStartIndex, count));
        }

        // ------------------------------------------------------------ global state

        public void SetGlobalFloat(int nameID, float value) => Add(c => Shader.SetGlobalFloat(nameID, value));
        public void SetGlobalFloat(string name, float value) => SetGlobalFloat(Shader.PropertyToID(name), value);
        public void SetGlobalInt(int nameID, int value) => Add(c => Shader.SetGlobalInt(nameID, value));
        public void SetGlobalInt(string name, int value) => SetGlobalInt(Shader.PropertyToID(name), value);
        public void SetGlobalInteger(int nameID, int value) => Add(c => Shader.SetGlobalInteger(nameID, value));
        public void SetGlobalInteger(string name, int value) => SetGlobalInteger(Shader.PropertyToID(name), value);
        public void SetGlobalVector(int nameID, Vector4 value) => Add(c => Shader.SetGlobalVector(nameID, value));
        public void SetGlobalVector(string name, Vector4 value) => SetGlobalVector(Shader.PropertyToID(name), value);
        public void SetGlobalColor(int nameID, Color value) => Add(c => Shader.SetGlobalColor(nameID, value));
        public void SetGlobalColor(string name, Color value) => SetGlobalColor(Shader.PropertyToID(name), value);
        public void SetGlobalMatrix(int nameID, Matrix4x4 value) => Add(c => Shader.SetGlobalMatrix(nameID, value));
        public void SetGlobalMatrix(string name, Matrix4x4 value) => SetGlobalMatrix(Shader.PropertyToID(name), value);
        public void SetGlobalFloatArray(int nameID, float[] values) => Add(c => Shader.SetGlobalFloatArray(nameID, values));
        public void SetGlobalFloatArray(string propertyName, float[] values) => SetGlobalFloatArray(Shader.PropertyToID(propertyName), values);
        public void SetGlobalFloatArray(int nameID, List<float> values) => SetGlobalFloatArray(nameID, values.ToArray());
        public void SetGlobalFloatArray(string propertyName, List<float> values) => SetGlobalFloatArray(Shader.PropertyToID(propertyName), values.ToArray());
        public void SetGlobalVectorArray(int nameID, Vector4[] values) => Add(c => Shader.SetGlobalVectorArray(nameID, values));
        public void SetGlobalVectorArray(string propertyName, Vector4[] values) => SetGlobalVectorArray(Shader.PropertyToID(propertyName), values);
        public void SetGlobalVectorArray(int nameID, List<Vector4> values) => SetGlobalVectorArray(nameID, values.ToArray());
        public void SetGlobalVectorArray(string propertyName, List<Vector4> values) => SetGlobalVectorArray(Shader.PropertyToID(propertyName), values.ToArray());
        public void SetGlobalMatrixArray(int nameID, Matrix4x4[] values) => Add(c => Shader.SetGlobalMatrixArray(nameID, values));
        public void SetGlobalMatrixArray(string propertyName, Matrix4x4[] values) => SetGlobalMatrixArray(Shader.PropertyToID(propertyName), values);
        public void SetGlobalMatrixArray(int nameID, List<Matrix4x4> values) => SetGlobalMatrixArray(nameID, values.ToArray());
        public void SetGlobalMatrixArray(string propertyName, List<Matrix4x4> values) => SetGlobalMatrixArray(Shader.PropertyToID(propertyName), values.ToArray());

        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value) => Add(c => Shader.SetGlobalTexture(nameID, Resolve(value, c)));
        public void SetGlobalTexture(string name, RenderTargetIdentifier value) => SetGlobalTexture(Shader.PropertyToID(name), value);
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value, RenderTextureSubElement element) => SetGlobalTexture(nameID, value);
        public void SetGlobalTexture(string name, RenderTargetIdentifier value, RenderTextureSubElement element) => SetGlobalTexture(Shader.PropertyToID(name), value);

        public void EnableShaderKeyword(string keyword) => Add(c => Shader.EnableKeyword(keyword));
        public void DisableShaderKeyword(string keyword) => Add(c => Shader.DisableKeyword(keyword));

        public void SetGlobalDepthBias(float bias, float slopeBias) => Add(c => GfxStream.DepthBias(bias, slopeBias));

        public void SetInvertCulling(bool invertCulling) => Add(c => GL.invertCulling = invertCulling);
        public void SetWireframe(bool enable) => Add(c => GL.wireframe = enable);
        [Unbound.EditorFaithful("no-op is equivalent: the flags only restrict which commands a buffer may hold (async compute validation)")]
        public void SetExecutionFlags(CommandBufferExecutionFlags flags) { }
        public void SetInstanceMultiplier(uint multiplier) => Stubs.Ignored("CommandBuffer.SetInstanceMultiplier", "is not honoured yet: instanced draws keep their instance count");
        [Unbound.EditorFaithful("no-op is equivalent: random write targets are never bound (SetRandomWriteTarget is not implemented and warns)")]
        public void ClearRandomWriteTargets() { }
        [Unbound.EditorFaithful("profiler sample: nothing a game observes (no profiler is attached)")]
        public void BeginSample(string name) { }
        [Unbound.EditorFaithful("profiler sample: nothing a game observes (no profiler is attached)")]
        public void EndSample(string name) { }

        public void IssuePluginEvent(IntPtr callback, int eventID) => Add(c =>
        {
            if (s_WarnedPluginEvent) return;
            s_WarnedPluginEvent = true;
            Debug.LogWarning("Unbound: native rendering plugin events (CommandBuffer.IssuePluginEvent) are not supported.");
        });

        public void IssuePluginEventAndData(IntPtr callback, int eventID, IntPtr data) => IssuePluginEvent(callback, eventID);
    }
}
