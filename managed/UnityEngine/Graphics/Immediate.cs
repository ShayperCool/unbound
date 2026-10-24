using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// The immediate rendering state of the built-in pipeline: GL's matrices and the pass
    /// <c>Material.SetPass</c> activated, the meshes GL and blits draw, and the meshes
    /// <c>Graphics.DrawMesh</c> queued for this frame's cameras.
    /// </summary>
    internal static class Immediate
    {
        internal static UnityEngine.Matrix4x4 Projection = UnityEngine.Matrix4x4.identity;
        internal static UnityEngine.Matrix4x4 ModelView = UnityEngine.Matrix4x4.identity;
        internal static readonly Stack<(UnityEngine.Matrix4x4, UnityEngine.Matrix4x4)> MatrixStack = new Stack<(UnityEngine.Matrix4x4, UnityEngine.Matrix4x4)>();
        /// <summary>The material and pass of the last <c>Material.SetPass</c>.</summary>
        internal static UnityEngine.Material PassMaterial;
        internal static int PassIndex;

        /// <summary>Blit's projection: the unit square, as GL.LoadOrtho.</summary>
        internal static readonly UnityEngine.Matrix4x4 Ortho = UnityEngine.Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 100f);

        static UnityEngine.Mesh s_BlitQuad;
        static UnityEngine.Material s_BlitCopy;

        /// <summary>A unit quad (0..1) with uvs 0..1, as Unity's blits draw.</summary>
        internal static UnityEngine.Mesh BlitQuad
        {
            get
            {
                if (s_BlitQuad) return s_BlitQuad;
                using (ObjectRegistry.AsAssets())
                    s_BlitQuad = new UnityEngine.Mesh { name = "Blit quad", hideFlags = UnityEngine.HideFlags.HideAndDontSave };
                s_BlitQuad.vertices = new[] { new UnityEngine.Vector3(0, 0, 0.1f), new UnityEngine.Vector3(0, 1, 0.1f), new UnityEngine.Vector3(1, 1, 0.1f), new UnityEngine.Vector3(1, 0, 0.1f) };
                s_BlitQuad.uv = new[] { new UnityEngine.Vector2(0, 0), new UnityEngine.Vector2(0, 1), new UnityEngine.Vector2(1, 1), new UnityEngine.Vector2(1, 0) };
                s_BlitQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                return s_BlitQuad;
            }
        }

        /// <summary>Unity's default blit material (Hidden/BlitCopy).</summary>
        internal static UnityEngine.Material BlitCopy
        {
            get
            {
                if (s_BlitCopy) return s_BlitCopy;
                using (ObjectRegistry.AsAssets())
                    s_BlitCopy = new UnityEngine.Material(UnityEngine.Shader.Find("Hidden/BlitCopy")) { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
                return s_BlitCopy;
            }
        }

        static readonly int s_MainTex = UnityEngine.Shader.PropertyToID("_MainTex");
        static readonly Dictionary<int, object> s_BlitProperties = new Dictionary<int, object>();

        /// <summary>Graphics.Blit: the source as <c>_MainTex</c>, a unit quad over the destination (the
        /// screen when null) with the material's passes (all when <paramref name="pass"/> is -1); the
        /// destination becomes the active render texture.</summary>
        internal static void Blit(UnityEngine.Texture source, UnityEngine.RenderTexture destination, UnityEngine.Material material, int pass,
            UnityEngine.Vector2 scale, UnityEngine.Vector2 offset)
        {
            material ??= BlitCopy;
            if (source != null) material.SetTexture(s_MainTex, source);
            s_BlitProperties.Clear();
            if (scale != UnityEngine.Vector2.one || offset != UnityEngine.Vector2.zero)
                s_BlitProperties[UnityEngine.Shader.PropertyToID("_MainTex_ST")] = new UnityEngine.Vector4(scale.x, scale.y, offset.x, offset.y);
            BlitWith(destination != null ? destination.EnsureCreated() : 0, material, pass, s_BlitProperties);
            UnityEngine.RenderTexture.SetActiveQuietly(destination);
        }

        /// <summary>A unit quad over a target with a material, as blits draw it; the current target,
        /// viewport and matrices are restored after.</summary>
        internal static void BlitWith(ulong target, UnityEngine.Material material, int pass, Dictionary<int, object> properties)
        {
            ulong mesh = BlitQuad.Sync();
            ulong handle = material.Sync();
            var saved = GfxStream.Current;
            GfxStream.SetTarget(target);
            GfxStream.Camera(UnityEngine.Matrix4x4.identity, Ortho);
            GfxStream.Draw(mesh, 0, handle, pass < 0 ? GfxStream.AllPasses : pass, UnityEngine.Matrix4x4.identity, UnityEngine.Color.white,
                properties: properties != null && properties.Count > 0 ? properties : null);
            GfxStream.Camera(saved.View, saved.Projection, saved.Flags);
        }

        /// <summary>A mesh with the active pass (<c>Material.SetPass</c>) under GL's matrices.</summary>
        internal static void DrawNow(UnityEngine.Mesh mesh, int submesh, in UnityEngine.Matrix4x4 model)
        {
            if (PassMaterial == null)
            {
                UnityEngine.Debug.LogError("Graphics.DrawMeshNow and GL drawing need a pass set with Material.SetPass first.");
                return;
            }
            ulong meshHandle = mesh.Sync();
            ulong material = PassMaterial.Sync();
            EnsureActiveTarget();
            var saved = GfxStream.Current;
            GfxStream.Camera(ModelView, Projection, saved.Flags);
            GfxStream.Draw(meshHandle, submesh, material, PassIndex, model, UnityEngine.Color.white);
            GfxStream.Camera(saved.View, saved.Projection, saved.Flags);
        }

        /// <summary>Immediate drawing goes into the active render texture (the screen when none).</summary>
        internal static void EnsureActiveTarget()
        {
            var active = UnityEngine.RenderTexture.active;
            ulong handle = active != null ? active.EnsureCreated() : 0;
            if (GfxStream.Current.Color != handle) GfxStream.SetTarget(handle);
        }

        // ------------------------------------------------------------ GL.Begin / End

        internal static int Mode = -1;
        static readonly List<UnityEngine.Vector3> s_Vertices = new List<UnityEngine.Vector3>();
        static readonly List<UnityEngine.Color> s_Colors = new List<UnityEngine.Color>();
        static readonly List<UnityEngine.Vector4>[] s_Uvs = { new List<UnityEngine.Vector4>(), new List<UnityEngine.Vector4>(), new List<UnityEngine.Vector4>(), new List<UnityEngine.Vector4>() };
        internal static UnityEngine.Color Color = UnityEngine.Color.white;
        internal static readonly UnityEngine.Vector4[] TexCoords = new UnityEngine.Vector4[4];
        static readonly List<UnityEngine.Mesh> s_GlMeshes = new List<UnityEngine.Mesh>();
        static int s_GlMeshesUsed, s_GlFrame = -1;

        internal static void Vertex(UnityEngine.Vector3 v)
        {
            if (Mode < 0) return;
            s_Vertices.Add(v);
            s_Colors.Add(Color);
            for (int i = 0; i < 4; i++) s_Uvs[i].Add(TexCoords[i]);
        }

        internal static void Begin(int mode)
        {
            Mode = mode;
            s_Vertices.Clear();
            s_Colors.Clear();
            foreach (var uv in s_Uvs) uv.Clear();
        }

        internal static void End()
        {
            int mode = Mode;
            Mode = -1;
            int n = s_Vertices.Count;
            if (n == 0) return;
            var indices = new List<int>();
            var topology = UnityEngine.MeshTopology.Triangles;
            switch (mode)
            {
                case UnityEngine.GL.TRIANGLES:
                    for (int i = 0; i + 2 < n; i += 3) { indices.Add(i); indices.Add(i + 1); indices.Add(i + 2); }
                    break;
                case UnityEngine.GL.TRIANGLE_STRIP:
                    for (int i = 0; i + 2 < n; i++)
                    {
                        if ((i & 1) == 0) { indices.Add(i); indices.Add(i + 1); indices.Add(i + 2); }
                        else { indices.Add(i + 1); indices.Add(i); indices.Add(i + 2); }
                    }
                    break;
                case UnityEngine.GL.QUADS:
                    for (int i = 0; i + 3 < n; i += 4) { indices.Add(i); indices.Add(i + 1); indices.Add(i + 2); indices.Add(i); indices.Add(i + 2); indices.Add(i + 3); }
                    break;
                case UnityEngine.GL.LINES:
                    topology = UnityEngine.MeshTopology.Lines;
                    for (int i = 0; i + 1 < n; i += 2) { indices.Add(i); indices.Add(i + 1); }
                    break;
                case UnityEngine.GL.LINE_STRIP:
                    topology = UnityEngine.MeshTopology.LineStrip;
                    for (int i = 0; i < n; i++) indices.Add(i);
                    break;
                default:
                    return;
            }
            if (indices.Count == 0) return;
            // A mesh per GL.End in a frame: an earlier one may not have executed yet.
            if (s_GlFrame != UnityEngine.Time.frameCount) { s_GlFrame = UnityEngine.Time.frameCount; s_GlMeshesUsed = 0; }
            if (s_GlMeshesUsed == s_GlMeshes.Count)
            {
                using (ObjectRegistry.AsAssets())
                    s_GlMeshes.Add(new UnityEngine.Mesh { name = "GL", hideFlags = UnityEngine.HideFlags.HideAndDontSave });
            }
            var mesh = s_GlMeshes[s_GlMeshesUsed++];
            mesh.Clear();
            mesh.SetVertices(s_Vertices);
            mesh.SetColors(s_Colors);
            for (int i = 0; i < 4; i++) mesh.SetUVs(i, s_Uvs[i]);
            mesh.SetIndices(indices.ToArray(), topology, 0);
            DrawNow(mesh, 0, UnityEngine.Matrix4x4.identity);
        }

        // ------------------------------------------------------------ Graphics.DrawMesh

        internal struct QueuedMesh
        {
            internal UnityEngine.Mesh Mesh;
            internal UnityEngine.Matrix4x4 Matrix;
            internal UnityEngine.Material Material;
            internal int Layer;
            internal UnityEngine.Camera Camera;
            internal int Submesh;
            internal Dictionary<int, object> Properties;
        }

        /// <summary>Meshes queued with Graphics.DrawMesh, drawn by this frame's cameras.</summary>
        internal static readonly List<QueuedMesh> Queued = new List<QueuedMesh>();

        internal static void Queue(UnityEngine.Mesh mesh, UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int layer,
            UnityEngine.Camera camera, int submesh, UnityEngine.MaterialPropertyBlock properties)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) return;
            Queued.Add(new QueuedMesh
            {
                Mesh = mesh,
                Matrix = matrix,
                Material = material,
                Layer = layer,
                Camera = camera,
                Submesh = submesh,
                Properties = properties != null && !properties.isEmpty ? new Dictionary<int, object>(properties.m_Values) : null,
            });
        }
    }
}

namespace UnityEngine
{
    public partial class Graphics
    {
        public static void Blit(Texture source, RenderTexture dest) => Immediate.Blit(source, dest, null, -1, Vector2.one, Vector2.zero);
        public static void Blit(Texture source, RenderTexture dest, int sourceDepthSlice, int destDepthSlice) => Blit(source, dest);
        public static void Blit(Texture source, RenderTexture dest, Vector2 scale, Vector2 offset) => Immediate.Blit(source, dest, null, -1, scale, offset);
        public static void Blit(Texture source, RenderTexture dest, Vector2 scale, Vector2 offset, int sourceDepthSlice, int destDepthSlice) => Blit(source, dest, scale, offset);
        public static void Blit(Texture source, RenderTexture dest, Material mat, [Internal.DefaultValue("-1")] int pass) => Immediate.Blit(source, dest, mat, pass, Vector2.one, Vector2.zero);
        public static void Blit(Texture source, RenderTexture dest, Material mat, int pass, int destDepthSlice) => Blit(source, dest, mat, pass);
        public static void Blit(Texture source, RenderTexture dest, Material mat) => Blit(source, dest, mat, -1);
        public static void Blit(Texture source, Material mat, [Internal.DefaultValue("-1")] int pass) => Immediate.Blit(source, null, mat, pass, Vector2.one, Vector2.zero);
        public static void Blit(Texture source, Material mat, int pass, int destDepthSlice) => Blit(source, mat, pass);
        public static void Blit(Texture source, Material mat) => Blit(source, mat, -1);

        public static void ExecuteCommandBuffer(CommandBuffer buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Execute(null);
        }

        public static void DrawMeshNow(Mesh mesh, Vector3 position, Quaternion rotation, int materialIndex) =>
            DrawMeshNow(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), materialIndex);
        public static void DrawMeshNow(Mesh mesh, Matrix4x4 matrix, int materialIndex)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            Immediate.DrawNow(mesh, materialIndex < 0 ? 0 : materialIndex, matrix);
        }
        public static void DrawMeshNow(Mesh mesh, Vector3 position, Quaternion rotation) => DrawMeshNow(mesh, position, rotation, -1);
        public static void DrawMeshNow(Mesh mesh, Matrix4x4 matrix) => DrawMeshNow(mesh, matrix, -1);

        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, Transform probeAnchor, LightProbeUsage lightProbeUsage) =>
            Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, Transform probeAnchor, bool useLightProbes) =>
            Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, Transform probeAnchor) =>
            Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows, bool receiveShadows, bool useLightProbes) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows, bool receiveShadows) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties) =>
            Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex) =>
            Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, null);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera) =>
            Immediate.Queue(mesh, matrix, material, layer, camera, 0, null);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer) =>
            Immediate.Queue(mesh, matrix, material, layer, null, 0, null);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, Transform probeAnchor, bool useLightProbes) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, Transform probeAnchor) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows, bool receiveShadows, bool useLightProbes) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows, bool receiveShadows) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, null);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, 0, null);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, null, 0, null);

        public static void SetRenderTarget(RenderTexture rt) => RenderTexture.active = rt;
        public static void SetRenderTarget(RenderTexture rt, int mipLevel) => SetRenderTarget(rt, mipLevel, CubemapFace.Unknown, 0);
        public static void SetRenderTarget(RenderTexture rt, int mipLevel, CubemapFace face) => SetRenderTarget(rt, mipLevel, face, 0);
        public static void SetRenderTarget(RenderTexture rt, [Internal.DefaultValue("0")] int mipLevel, [Internal.DefaultValue("CubemapFace.Unknown")] CubemapFace face, [Internal.DefaultValue("0")] int depthSlice)
        {
            RenderTexture.SetActiveQuietly(rt);
            GfxStream.SetTarget(rt != null ? rt.EnsureCreated() : 0, 0, mipLevel, face == CubemapFace.Unknown ? depthSlice : (int)face);
        }

        public static void ClearRandomWriteTargets() { }

        public static void CopyTexture(Texture src, Texture dst) => CopyTexture(src, 0, 0, -1, -1, -1, -1, dst, 0, 0, 0, 0);
        public static void CopyTexture(Texture src, int srcElement, Texture dst, int dstElement) => CopyTexture(src, srcElement, 0, -1, -1, -1, -1, dst, dstElement, 0, 0, 0);
        public static void CopyTexture(Texture src, int srcElement, int srcMip, Texture dst, int dstElement, int dstMip) =>
            CopyTexture(src, srcElement, srcMip, -1, -1, -1, -1, dst, dstElement, dstMip, 0, 0);

        /// <summary>Copies on the GPU; between two readable Texture2Ds the CPU pixels too, as Unity.</summary>
        public static void CopyTexture(Texture src, int srcElement, int srcMip, int srcX, int srcY, int srcWidth, int srcHeight, Texture dst, int dstElement, int dstMip, int dstX, int dstY)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            if (src is Texture2D s2 && dst is Texture2D d2 && s2.isReadable && d2.isReadable && srcMip == 0 && dstMip == 0)
            {
                int w = srcWidth < 0 ? s2.width : srcWidth, h = srcHeight < 0 ? s2.height : srcHeight;
                var pixels = s2.GetPixels(srcWidth < 0 ? 0 : srcX, srcWidth < 0 ? 0 : srcY, w, h);
                d2.SetPixels(dstX, dstY, w, h, pixels);
            }
            ulong source = TextureHandles.Of(src), destination = TextureHandles.Of(dst);
            GfxStream.CopyTexture(source, srcElement, srcMip, new RectInt(srcX, srcY, srcWidth, srcHeight), destination, dstElement, dstMip, dstX, dstY);
        }

        /// <summary>A blit that converts between formats and sizes.</summary>
        public static bool ConvertTexture(Texture src, Texture dst)
        {
            if (dst is RenderTexture rt)
            {
                Immediate.Blit(src, rt, null, -1, Vector2.one, Vector2.zero);
                return true;
            }
            CopyTexture(src, dst);
            return true;
        }

        public static bool ConvertTexture(Texture src, int srcElement, Texture dst, int dstElement) => ConvertTexture(src, dst);
    }

    public sealed partial class GL
    {
        public const int TRIANGLES = 4;
        public const int TRIANGLE_STRIP = 5;
        public const int QUADS = 7;
        public const int LINES = 1;
        public const int LINE_STRIP = 2;

        public static void Vertex3(float x, float y, float z) => Immediate.Vertex(new Vector3(x, y, z));
        public static void Vertex(Vector3 v) => Immediate.Vertex(v);
        public static void TexCoord3(float x, float y, float z) => Immediate.TexCoords[0] = new Vector4(x, y, z, 0f);
        public static void TexCoord(Vector3 v) => Immediate.TexCoords[0] = v;
        public static void TexCoord2(float x, float y) => Immediate.TexCoords[0] = new Vector4(x, y, 0f, 0f);
        public static void MultiTexCoord3(int unit, float x, float y, float z) { if (unit >= 0 && unit < 4) Immediate.TexCoords[unit] = new Vector4(x, y, z, 0f); }
        public static void MultiTexCoord(int unit, Vector3 v) { if (unit >= 0 && unit < 4) Immediate.TexCoords[unit] = v; }
        public static void MultiTexCoord2(int unit, float x, float y) { if (unit >= 0 && unit < 4) Immediate.TexCoords[unit] = new Vector4(x, y, 0f, 0f); }
        public static void Color(Color c) => Immediate.Color = c;
        public static void Begin(int mode) => Immediate.Begin(mode);
        public static void End() => Immediate.End();

        public static void Flush() => GfxStream.Flush();
        public static void RenderTargetBarrier() { }
        public static void InvalidateState() { }
        public static void IssuePluginEvent(IntPtr callback, int eventID) { }

        public static void PushMatrix() => Immediate.MatrixStack.Push((Immediate.Projection, Immediate.ModelView));
        public static void PopMatrix()
        {
            if (Immediate.MatrixStack.Count == 0) return;
            (Immediate.Projection, Immediate.ModelView) = Immediate.MatrixStack.Pop();
        }
        public static void LoadIdentity() => Immediate.ModelView = Matrix4x4.identity;
        public static void LoadOrtho()
        {
            Immediate.Projection = Immediate.Ortho;
            Immediate.ModelView = Matrix4x4.identity;
        }
        public static void LoadPixelMatrix()
        {
            var active = RenderTexture.active;
            LoadPixelMatrix(0f, active != null ? active.width : Screen.width, 0f, active != null ? active.height : Screen.height);
        }
        public static void LoadPixelMatrix(float left, float right, float bottom, float top)
        {
            Immediate.Projection = Matrix4x4.Ortho(left, right, bottom, top, -1f, 100f);
            Immediate.ModelView = Matrix4x4.identity;
        }
        public static void LoadProjectionMatrix(Matrix4x4 mat) => Immediate.Projection = mat;
        public static void MultMatrix(Matrix4x4 m) => Immediate.ModelView = Immediate.ModelView * m;

        public static Matrix4x4 modelview
        {
            get => Immediate.ModelView;
            set => Immediate.ModelView = value;
        }

        /// <summary>Unbound's renderer takes OpenGL-convention projections (it maps depth itself), so
        /// the GPU projection is the projection.</summary>
        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 proj, bool renderIntoTexture) => proj;

        public static void Clear(bool clearDepth, bool clearColor, Color backgroundColor, [Internal.DefaultValue("1.0f")] float depth)
        {
            Immediate.EnsureActiveTarget();
            byte flags = (byte)((clearColor ? GfxStream.ClearColor : 0) | (clearDepth ? GfxStream.ClearDepth | GfxStream.ClearStencil : 0));
            if (flags != 0) GfxStream.Clear(flags, backgroundColor, depth);
        }

        public static void Clear(bool clearDepth, bool clearColor, Color backgroundColor) => Clear(clearDepth, clearColor, backgroundColor, 1f);

        public static void Viewport(Rect pixelRect)
        {
            Immediate.EnsureActiveTarget();
            GfxStream.Viewport(pixelRect);
        }

        public static void ClearWithSkybox(bool clearDepth, Camera camera) => Clear(clearDepth, true, camera != null ? camera.backgroundColor : UnityEngine.Color.black);

        public static bool wireframe { get; set; }
        public static bool sRGBWrite { get; set; }
        public static bool invertCulling { get; set; }
    }
}
