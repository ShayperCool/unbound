using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

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

        /// <summary>The default blit of a slice of a texture array (measured, tests/projects/droppedparams
        /// slice.*.array.*: the source slice is read into the destination slice). The renderer binds
        /// a blit's source as a 2D texture, so the slice is copied texel for texel, which gives the
        /// Editor's result between targets of the same size and format without scale or offset;
        /// another such blit is refused (warns) rather than drawn wrong. So is a copy the renderer
        /// cannot make: a slice past the source's slices or the destination's layers, or a
        /// multisampled side. True when it was handled (the source is an array).</summary>
        internal static bool BlitArraySlice(UnityEngine.Texture source, UnityEngine.Texture destination, int sourceSlice, int destinationSlice,
            UnityEngine.Vector2 scale, UnityEngine.Vector2 offset)
        {
            int slices = ArraySlices(source);
            if (slices == 0) return false;
            if (destination == null || destination.width != source.width || destination.height != source.height
                || destination.graphicsFormat != source.graphicsFormat || scale != UnityEngine.Vector2.one || offset != UnityEngine.Vector2.zero)
            {
                Stubs.Ignored("Blit from a texture array",
                    "is done only into a target of the array's size and format, without scale or offset: this blit is skipped");
                return true;
            }
            if (sourceSlice < 0 || sourceSlice >= slices)
            {
                Stubs.Ignored("Blit from a texture array slice past its slices",
                    "is not supported: this blit is skipped");
                return true;
            }
            if (destinationSlice < 0 || destinationSlice >= GpuLayers(destination))
            {
                Stubs.Ignored("Blit from a texture array into a slice past the target's layers",
                    "is outside the destination: this blit is skipped");
                return true;
            }
            if (source is UnityEngine.RenderTexture { antiAliasing: > 1 } || destination is UnityEngine.RenderTexture { antiAliasing: > 1 })
            {
                Stubs.Ignored("Blit from a texture array with multisampling",
                    "is not supported: this blit is skipped");
                return true;
            }
            ulong from = source is UnityEngine.RenderTexture rs ? rs.EnsureCreated() : TextureHandles.Of(source);
            ulong to = destination is UnityEngine.RenderTexture rd ? rd.EnsureCreated() : TextureHandles.Of(destination);
            GfxStream.CopyTexture(from, sourceSlice, 0, new UnityEngine.RectInt(0, 0, -1, -1), to, destinationSlice, 0, 0, 0);
            return true;
        }

        /// <summary>The layers the renderer holds for a texture: an array's slices, a cube's six
        /// faces (six per cube of a cubemap array), or a volume's depth.</summary>
        internal static int GpuLayers(UnityEngine.Texture texture) => texture switch
        {
            UnityEngine.RenderTexture rt => rt.GpuLayers,
            UnityEngine.Texture2DArray array => array.depth,
            UnityEngine.CubemapArray cubes => 6 * cubes.cubemapCount,
            UnityEngine.Cubemap => 6,
            UnityEngine.Texture3D volume => volume.depth,
            _ => 1,
        };

        /// <summary>A texture array's slice count (0: not an array).</summary>
        internal static int ArraySlices(UnityEngine.Texture texture) => texture switch
        {
            UnityEngine.RenderTexture rt when rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray => Math.Max(1, rt.volumeDepth),
            UnityEngine.Texture2DArray array => array.depth,
            _ => 0,
        };

        static UnityEngine.Texture2D s_HalfGray;

        /// <summary>0.5 in every channel, alpha too: what a CommandBuffer blit of an array without
        /// slices reads (measured, slice.cmd.copy.array).</summary>
        internal static UnityEngine.Texture2D HalfGray
        {
            get
            {
                if (s_HalfGray) return s_HalfGray;
                using (ObjectRegistry.AsAssets())
                    s_HalfGray = new UnityEngine.Texture2D(1, 1, UnityEngine.TextureFormat.RGBA32, false, true) { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
                s_HalfGray.SetPixel(0, 0, new UnityEngine.Color32(128, 128, 128, 128));
                s_HalfGray.Apply(false, false);
                return s_HalfGray;
            }
        }

        static readonly int s_MainTex = UnityEngine.Shader.PropertyToID("_MainTex");
        static readonly Dictionary<int, object> s_BlitProperties = new Dictionary<int, object>();

        /// <summary>Graphics.Blit: the source as <c>_MainTex</c>, a unit quad over the destination (the
        /// screen when null) with the material's passes (all when <paramref name="pass"/> is -1); the
        /// destination becomes the active render texture.
        /// <para>The slices: the array slice the default copy reads (<see cref="BlitArraySlice"/>),
        /// and the destination's slice drawn into (measured, tests/projects/droppedparams slice.*).</para></summary>
        internal static void Blit(UnityEngine.Texture source, UnityEngine.RenderTexture destination, UnityEngine.Material material, int pass,
            UnityEngine.Vector2 scale, UnityEngine.Vector2 offset, int sourceSlice = 0, int destinationSlice = 0)
        {
            // The screen, while a screen camera draws into its stand-in, is the stand-in: what is
            // drawn there is presented with the camera's image.
            destination ??= UnityEngine.RenderTexture.ScreenStandIn;
            if (material == null && BlitArraySlice(source, destination, sourceSlice, destinationSlice, scale, offset))
            {
                UnityEngine.RenderTexture.SetActiveQuietly(destination);
                return;
            }
            s_BlitProperties.Clear();
            material ??= BlitCopy;
            if (source != null) material.SetTexture(s_MainTex, source);
            if (scale != UnityEngine.Vector2.one || offset != UnityEngine.Vector2.zero)
                s_BlitProperties[UnityEngine.Shader.PropertyToID("_MainTex_ST")] = new UnityEngine.Vector4(scale.x, scale.y, offset.x, offset.y);
            BlitWith(destination != null ? destination.EnsureCreated() : 0, material, pass, s_BlitProperties, destinationSlice);
            UnityEngine.RenderTexture.SetActiveQuietly(destination);
        }

        /// <summary>A copy of <paramref name="source"/> stretched over <paramref name="viewport"/>
        /// (pixels) of the destination (the screen when null) instead of all of it; the
        /// destination becomes the active render texture.</summary>
        internal static void BlitInto(UnityEngine.Texture source, UnityEngine.RenderTexture destination, UnityEngine.Rect viewport)
        {
            destination ??= UnityEngine.RenderTexture.ScreenStandIn;
            var material = BlitCopy;
            material.SetTexture(s_MainTex, source);
            BlitWith(destination != null ? destination.EnsureCreated() : 0, material, -1, null, 0, viewport);
            UnityEngine.RenderTexture.SetActiveQuietly(destination);
        }

        /// <summary>A unit quad over a target (its slice <paramref name="slice"/>; only
        /// <paramref name="viewport"/>, in pixels, when given) with a material. The destination
        /// stays active; the current view and projection are restored after.</summary>
        internal static void BlitWith(ulong target, UnityEngine.Material material, int pass, Dictionary<int, object> properties, int slice = 0,
            UnityEngine.Rect? viewport = null)
        {
            ulong mesh = BlitQuad.Sync();
            ulong handle = material.Sync();
            var saved = GfxStream.Current;
            GfxStream.SetTarget(target, 0, 0, slice);
            if (viewport is { } pixels) GfxStream.Viewport(pixels);
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
            GfxStream.Draw(meshHandle, submesh, material, PassIndex, model, UnityEngine.Color.white, rawWinding: Compat.ImmediateMeshKeepsWinding);
            GfxStream.Camera(saved.View, saved.Projection, saved.Flags);
        }

        /// <summary>Immediate drawing goes into the active render texture (the screen when none).</summary>
        internal static void EnsureActiveTarget()
        {
            // The screen's stand-in, while a screen camera draws, is the screen.
            var active = UnityEngine.RenderTexture.ActiveTarget;
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
            /// <summary>Instances or GPU-read counts (DrawMeshInstancedIndirect, ...); null for one.</summary>
            internal DrawSetup? Setup;
            /// <summary>How the draw is lit and shadowed.</summary>
            internal ImmediateLighting Lighting;
            /// <summary>A RenderParams draw's worldBounds, a legacy draw's bounds argument, or an instanced call's group bounds; null when unset.</summary>
            internal UnityEngine.Bounds? WorldBounds;
            /// <summary>What a scriptable pipeline's renderer lists filter it by: a RenderParams
            /// draw's renderingLayerMask, else the default rendering layer.</summary>
            internal uint RenderingLayerMask;
        }

        /// <summary>Meshes queued with Graphics.DrawMesh, drawn by this frame's cameras.</summary>
        internal static readonly List<QueuedMesh> Queued = new List<QueuedMesh>();

        internal static void Queue(UnityEngine.Mesh mesh, UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int layer,
            UnityEngine.Camera camera, int submesh, UnityEngine.MaterialPropertyBlock properties) =>
            Queue(mesh, matrix, material, layer, camera, submesh, properties, (DrawSetup?)null);

        internal static void Queue(UnityEngine.Mesh mesh, UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int layer,
            UnityEngine.Camera camera, int submesh, UnityEngine.MaterialPropertyBlock properties, DrawSetup? setup) =>
            Queue(mesh, matrix, material, layer, camera, submesh, properties, setup, ImmediateLighting.Default);

        internal static void Queue(UnityEngine.Mesh mesh, UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int layer,
            UnityEngine.Camera camera, int submesh, UnityEngine.MaterialPropertyBlock properties, ImmediateLighting lighting) =>
            Queue(mesh, matrix, material, layer, camera, submesh, properties, null, lighting);

        internal static void Queue(UnityEngine.Mesh mesh, UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int layer,
            UnityEngine.Camera camera, int submesh, UnityEngine.MaterialPropertyBlock properties, DrawSetup? setup, ImmediateLighting lighting) =>
            Queue(mesh, matrix, material, layer, camera, submesh, Values(properties), setup, lighting);

        /// <summary>A block's values as a queued draw keeps them (null when it has none).</summary>
        internal static Dictionary<int, object> Values(UnityEngine.MaterialPropertyBlock properties) =>
            properties != null && !properties.isEmpty ? new Dictionary<int, object>(properties.m_Values) : null;

        /// <summary>A draw with these values, which it shares with the other draws of its call
        /// (the frame's draws copy before they add to them).</summary>
        internal static void Queue(UnityEngine.Mesh mesh, UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int layer,
            UnityEngine.Camera camera, int submesh, Dictionary<int, object> properties, DrawSetup? setup, ImmediateLighting lighting)
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
                Properties = properties,
                Setup = setup,
                Lighting = lighting ?? ImmediateLighting.Default,
                RenderingLayerMask = UnityEngine.Rendering.GraphicsSettings.defaultRenderingLayerMask,
            });
        }

        /// <summary>Removes the draws queued for <paramref name="camera"/> alone: its render took
        /// them (a draw queued for every camera stays until the frame ends).</summary>
        internal static void TakeCameraDraws(UnityEngine.Camera camera)
        {
            if (camera == null) return;
            int kept = 0;
            for (int i = 0; i < Queued.Count; i++)
            {
                var q = Queued[i];
                if (q.Camera != null && q.Camera == camera) continue;
                Queued[kept++] = q;
            }
            Queued.RemoveRange(kept, Queued.Count - kept);
        }
    }

    /// <summary>How a Graphics.DrawMesh / RenderMesh draw is lit, as a mesh renderer with these
    /// settings would be (measured: tests/projects/builtinrp draw_mesh): the forward lights, its
    /// shadows cast and received, light and reflection probes at its bounds (or anchor).</summary>
    internal sealed class ImmediateLighting
    {
        internal ShadowCastingMode Cast = ShadowCastingMode.On;
        internal bool Receive = true;
        internal LightProbeUsage Probes = LightProbeUsage.BlendProbes;
        internal UnityEngine.Transform Anchor;
        /// <summary>Where blended probes are taken when not at the draw's own bounds: an instanced
        /// batch's bounds centre.</summary>
        internal UnityEngine.Vector3? ProbePosition;
        internal ReflectionProbeUsage Reflections = ReflectionProbeUsage.BlendProbes;

        /// <summary>Graphics.DrawMesh's defaults: casts and receives shadows, light probes.</summary>
        internal static readonly ImmediateLighting Default = new ImmediateLighting();

        internal static ImmediateLighting Of(ShadowCastingMode cast, bool receive, LightProbeUsage probes = LightProbeUsage.BlendProbes,
            UnityEngine.Transform anchor = null, ReflectionProbeUsage reflections = ReflectionProbeUsage.BlendProbes)
        {
            if (cast == ShadowCastingMode.On && receive && probes == LightProbeUsage.BlendProbes && anchor == null && reflections == ReflectionProbeUsage.BlendProbes)
                return Default;
            return new ImmediateLighting { Cast = cast, Receive = receive, Probes = probes, Anchor = anchor, Reflections = reflections };
        }

        internal static ImmediateLighting Of(bool cast, bool receive, bool probes = true) =>
            Of(cast ? ShadowCastingMode.On : ShadowCastingMode.Off, receive, probes ? LightProbeUsage.BlendProbes : LightProbeUsage.Off);
    }
}

namespace UnityEngine
{
    public partial class Graphics
    {
        /// <summary>A copy; from a texture array, of each slice into the destination's slice of
        /// the same index (measured, tests/projects/droppedparams slice.graphics.copy.array: all
        /// three slices of an array into an array; into a 2D target that is the first).</summary>
        public static void Blit(Texture source, RenderTexture dest)
        {
            int slices = Immediate.ArraySlices(source);
            if (slices == 0)
            {
                Immediate.Blit(source, dest, null, -1, Vector2.one, Vector2.zero);
                return;
            }
            int count = Math.Min(slices, dest != null ? GfxStream.LayersOf(dest) : 1);
            for (int slice = 0; slice < count; slice++) Immediate.Blit(source, dest, null, -1, Vector2.one, Vector2.zero, slice, slice);
        }
        public static void Blit(Texture source, RenderTexture dest, int sourceDepthSlice, int destDepthSlice) =>
            Immediate.Blit(source, dest, null, -1, Vector2.one, Vector2.zero, sourceDepthSlice, destDepthSlice);
        public static void Blit(Texture source, RenderTexture dest, Vector2 scale, Vector2 offset) => Immediate.Blit(source, dest, null, -1, scale, offset);
        public static void Blit(Texture source, RenderTexture dest, Vector2 scale, Vector2 offset, int sourceDepthSlice, int destDepthSlice) =>
            Immediate.Blit(source, dest, null, -1, scale, offset, sourceDepthSlice, destDepthSlice);
        public static void Blit(Texture source, RenderTexture dest, Material mat, [Internal.DefaultValue("-1")] int pass) => Immediate.Blit(source, dest, mat, pass, Vector2.one, Vector2.zero);
        public static void Blit(Texture source, RenderTexture dest, Material mat, int pass, int destDepthSlice) =>
            Immediate.Blit(source, dest, mat, pass, Vector2.one, Vector2.zero, 0, destDepthSlice);
        public static void Blit(Texture source, RenderTexture dest, Material mat) => Blit(source, dest, mat, -1);
        public static void Blit(Texture source, Material mat, [Internal.DefaultValue("-1")] int pass) => Immediate.Blit(source, null, mat, pass, Vector2.one, Vector2.zero);
        // Into the active render texture (the screen when none), its first slice whatever
        // destDepthSlice says (measured, tests/projects/droppedparams
        // slice.graphics.active.material.slice.1).
        public static void Blit(Texture source, Material mat, int pass, int destDepthSlice) =>
            Immediate.Blit(source, RenderTexture.ActiveTarget, mat, pass, Vector2.one, Vector2.zero);
        public static void Blit(Texture source, Material mat) => Blit(source, mat, -1);

        /// <summary>Runs the buffer now; the active render texture is the one before it, whatever
        /// targets the buffer set, as in the Unity 6000.0.71f1 Editor (tests/projects/nativegfx
        /// order.active).</summary>
        public static void ExecuteCommandBuffer(CommandBuffer buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            var active = RenderTexture.ActiveTarget;
            buffer.Execute(null);
            RenderTexture.SetActiveQuietly(active);
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

        [global::System.@ObsoleteAttribute("Method DrawMesh has been deprecated. Use Graphics.DrawMeshNow instead (UnityUpgradable) -> DrawMeshNow(*)", true)] [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)]
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation) => DrawMeshNow(mesh, position, rotation);
        [global::System.@ObsoleteAttribute("Method DrawMesh has been deprecated. Use Graphics.DrawMeshNow instead (UnityUpgradable) -> DrawMeshNow(*)", true)] [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)]
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, int materialIndex) => DrawMeshNow(mesh, position, rotation, materialIndex);
        [global::System.@ObsoleteAttribute("Method DrawMesh has been deprecated. Use Graphics.DrawMeshNow instead (UnityUpgradable) -> DrawMeshNow(*)", true)] [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)]
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix) => DrawMeshNow(mesh, matrix);
        [global::System.@ObsoleteAttribute("Method DrawMesh has been deprecated. Use Graphics.DrawMeshNow instead (UnityUpgradable) -> DrawMeshNow(*)", true)] [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)]
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, int materialIndex) => DrawMeshNow(mesh, matrix, materialIndex);
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, Transform probeAnchor, LightProbeUsage lightProbeUsage) =>
            Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, receiveShadows, lightProbeUsage, probeAnchor));
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, Transform probeAnchor, bool useLightProbes) =>
            Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties,
                ImmediateLighting.Of(castShadows, receiveShadows, useLightProbes ? LightProbeUsage.BlendProbes : LightProbeUsage.Off, probeAnchor));
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, Transform probeAnchor) =>
            Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, receiveShadows, LightProbeUsage.BlendProbes, probeAnchor));
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, receiveShadows));
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, true));
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows, bool receiveShadows, bool useLightProbes) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, receiveShadows, useLightProbes));
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows, bool receiveShadows) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, receiveShadows));
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows) => Immediate.Queue(mesh, matrix, material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, true));
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
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties,
                ImmediateLighting.Of(castShadows, receiveShadows, useLightProbes ? LightProbeUsage.BlendProbes : LightProbeUsage.Off, probeAnchor));
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, Transform probeAnchor) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, receiveShadows, LightProbeUsage.BlendProbes, probeAnchor));
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, receiveShadows));
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, true));
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows, bool receiveShadows, bool useLightProbes) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, receiveShadows, useLightProbes));
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows, bool receiveShadows) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, receiveShadows));
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer, Camera camera, int submeshIndex, MaterialPropertyBlock properties,
            bool castShadows) =>
            Immediate.Queue(mesh, Matrix4x4.TRS(position, rotation, Vector3.one), material, layer, camera, submeshIndex, properties, ImmediateLighting.Of(castShadows, true));
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
            GfxStream.SetTarget(rt != null ? rt.EnsureCreated() : 0, 0, mipLevel, face == CubemapFace.Unknown ? depthSlice : (int)face, GfxStream.LayersOf(rt));
        }

        public static void CopyTexture(Texture src, Texture dst)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            if (src is Texture2D b1 && dst is Texture2D b2
                && (BlockFormats.BlockBytes(b1.format) > 0 || BlockFormats.BlockBytes(b2.format) > 0))
            {
                Texture2D.CopyBlocks(b1, 0, 0, 0, -1, -1, b2, 0, 0, 0,
                    () => GfxStream.CopyTexture(TextureHandles.Of(src), 0, 0, new RectInt(-1, -1, -1, -1), TextureHandles.Of(dst), 0, 0, 0, 0), allLevels: true);
                return;
            }
            if (src.dimension != TextureDimension.Tex3D && dst.dimension != TextureDimension.Tex3D)
            {
                CopyTexture(src, 0, 0, -1, -1, -1, -1, dst, 0, 0, 0, 0);
                return;
            }
            int depth = Immediate.GpuLayers(src);
            if (src.dimension != dst.dimension || src.width != dst.width || src.height != dst.height
                || depth != Immediate.GpuLayers(dst) || src.mipmapCount != dst.mipmapCount)
            {
                Debug.LogError("Graphics.CopyTexture: whole volume copies require matching dimensions, sizes and mip counts.");
                return;
            }
            for (int mip = 0; mip < src.mipmapCount; mip++)
                for (int slice = 0; slice < Math.Max(1, depth >> mip); slice++)
                    CopyTexture(src, slice, mip, -1, -1, -1, -1, dst, slice, mip, 0, 0);
        }
        public static void CopyTexture(Texture src, int srcElement, Texture dst, int dstElement) => CopyTexture(src, srcElement, 0, -1, -1, -1, -1, dst, dstElement, 0, 0, 0);
        public static void CopyTexture(Texture src, int srcElement, int srcMip, Texture dst, int dstElement, int dstMip) =>
            CopyTexture(src, srcElement, srcMip, -1, -1, -1, -1, dst, dstElement, dstMip, 0, 0);

        /// <summary>Copies on the GPU; between Texture2Ds the retained CPU pixels too, independent of readability.</summary>
        public static void CopyTexture(Texture src, int srcElement, int srcMip, int srcX, int srcY, int srcWidth, int srcHeight, Texture dst, int dstElement, int dstMip, int dstX, int dstY)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            if (src is Texture2D b1 && dst is Texture2D b2
                && (BlockFormats.BlockBytes(b1.format) > 0 || BlockFormats.BlockBytes(b2.format) > 0))
            {
                Texture2D.CopyBlocks(b1, srcMip, srcX, srcY, srcWidth, srcHeight, b2, dstMip, dstX, dstY,
                    () => GfxStream.CopyTexture(TextureHandles.Of(src), srcElement, srcMip, new RectInt(srcX, srcY, srcWidth, srcHeight), TextureHandles.Of(dst), dstElement, dstMip, dstX, dstY));
                return;
            }
            if (src is Texture2D s2 && dst is Texture2D d2 && srcMip == 0 && dstMip == 0)
            {
                int w = srcWidth < 0 ? s2.width : srcWidth, h = srcHeight < 0 ? s2.height : srcHeight;
                s2.CopyRetainedPixels(d2, srcWidth < 0 ? 0 : srcX, srcWidth < 0 ? 0 : srcY, w, h, dstX, dstY);
            }
            ulong source = TextureHandles.Of(src), destination = TextureHandles.Of(dst);
            GfxStream.CopyTexture(source, srcElement, srcMip, new RectInt(srcX, srcY, srcWidth, srcHeight), destination, dstElement, dstMip, dstX, dstY);
        }

        /// <summary>Graphics.ConvertTexture (measured, tests/projects/cmdbuffers convert.*): a 2D
        /// source (a render texture too) blitted into a temporary of the destination's size and
        /// format, which is copied into the destination's element (an array slice or cube face).
        /// A cubemap converts (its first face) into a cubemap's face only. A render texture or non-renderable
        /// destination is refused with the Editor's errors.</summary>
        public static bool ConvertTexture(Texture src, Texture dst) => ConvertTexture(src, 0, dst, 0);

        public static bool ConvertTexture(Texture src, int srcElement, Texture dst, int dstElement)
        {
            if (src == null) { Debug.LogError("Graphics.ConvertTexture called with null source texture"); return false; }
            if (dst == null) { Debug.LogError("Graphics.ConvertTexture called with null destination texture"); return false; }
            if (dst is RenderTexture)
            {
                Debug.LogError("Graphics.ConvertTexture called with a render target destination.  Please use Graphics.Blit instead.");
                return false;
            }
            // A cubemap face converts into a cubemap only (cube into 2D or array is refused).
            bool face = src.dimension == TextureDimension.Cube;
            if (!(face ? dst.dimension == TextureDimension.Cube
                    : src.dimension == TextureDimension.Tex2D && dst.dimension is TextureDimension.Tex2D or TextureDimension.Tex2DArray
                        or TextureDimension.Cube or TextureDimension.CubeArray))
            {
                Debug.LogError($"Graphics.ConvertTexture called with unsupported source and destination texture types (src={(int)src.dimension} dst={(int)dst.dimension})");
                return false;
            }
            var format = dst.graphicsFormat;
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render))
            {
                Debug.LogError($"Unsupported destination format in Graphics.ConvertTexture ({(int)format}). This is likely because the device does not support this format as a rendertarget format.");
                return false;
            }
            int layers = Immediate.GpuLayers(dst);
            if (dstElement < 0 || dstElement >= layers)
            {
                Debug.LogError($"Graphics.CopyTexture called with invalid destination element index (got {dstElement}, have {layers} elements/layers)");
                return true;
            }
            var active = RenderTexture.ActiveTarget;
            // The face is copied out first (blits read 2D sources): the first face, whatever
            // srcElement says (measured, convert.cubetocube*).
            RenderTexture faceCopy = null;
            if (face)
            {
                faceCopy = RenderTexture.GetTemporary(new RenderTextureDescriptor(src.width, src.height, src.graphicsFormat, 0));
                faceCopy.filterMode = src.filterMode;
                GfxStream.CopyTexture(TextureHandles.Of(src), 0, 0, new RectInt(0, 0, -1, -1), faceCopy.EnsureCreated(), 0, 0, 0, 0);
            }
            var temporary = RenderTexture.GetTemporary(new RenderTextureDescriptor(dst.width, dst.height, format, 0));
            Immediate.Blit(faceCopy != null ? faceCopy : src, temporary, null, -1, Vector2.one, Vector2.zero);
            GfxStream.CopyTexture(temporary.EnsureCreated(), 0, 0, new RectInt(0, 0, -1, -1), TextureHandles.Of(dst), dstElement, 0, 0, 0);
            RenderTexture.SetActiveQuietly(active);
            RenderTexture.ReleaseTemporary(temporary);
            if (faceCopy != null) RenderTexture.ReleaseTemporary(faceCopy);
            return true;
        }
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
        [Unbound.EditorFaithful("a barrier for framebuffer fetch on tile GPUs: nothing to wait for on the Editor's OpenGLCore device")]
        public static void RenderTargetBarrier() { }
        [Unbound.EditorFaithful("no-op is equivalent: no native plugin shares the renderer's cached state")]
        public static void InvalidateState() { }
        /// <summary>A native plugin's rendering callback, in order with the rendering around it
        /// (<see cref="GfxStream.PluginEvent"/>).</summary>
        public static void IssuePluginEvent(IntPtr callback, int eventID)
        {
            if (callback == IntPtr.Zero) throw new ArgumentException("Null callback specified.", "callback");
            // Like every GL call, it finds the active render texture bound.
            Immediate.EnsureActiveTarget();
            GfxStream.PluginEvent(callback, eventID);
        }

        /// <summary>Every loaded plugin's <c>UnityRenderEvent</c> export.</summary>
        [global::System.@ObsoleteAttribute("IssuePluginEvent(eventID) is deprecated. Use IssuePluginEvent(callback, eventID) instead.", false)]
        public static void IssuePluginEvent(int eventID) => NativePlugins.IssueLegacyEvent(eventID);

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
            var active = RenderTexture.ActiveTarget;
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

        /// <summary>The projection as the run's device draws with it (OpenGL Core: the projection;
        /// Vulkan: reversed depth from 0 to 1, y flipped into render textures). Unbound's renderer
        /// takes the API's projections and applies the same itself.</summary>
        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 proj, bool renderIntoTexture) =>
            Unbound.Runtime.GraphicsDeviceProfile.Current.GpuProjection(proj, renderIntoTexture);

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

        /// <summary>Triangles drawn after it draw as their edges (measured:
        /// tests/projects/builtinfx wireframe).</summary>
        public static bool wireframe
        {
            get => s_Wireframe;
            set
            {
                s_Wireframe = value;
                GfxStream.SetGlFlag(GfxStream.CameraWireframe, value);
            }
        }

        /// <summary>Whether draws into sRGB render textures convert the colors (on, the default);
        /// off, the shader's output is stored as it is (measured: tests/projects/builtinfx).</summary>
        public static bool sRGBWrite
        {
            get => s_SrgbWrite;
            set
            {
                s_SrgbWrite = value;
                GfxStream.SetGlFlag(GfxStream.CameraNoSrgbWrite, !value);
            }
        }

        /// <summary>Draws after it cull the other faces (mirror cameras); it stays set across
        /// cameras until set back (measured: tests/projects/builtinfx cmd_invert_*).</summary>
        public static bool invertCulling
        {
            get => s_InvertCulling;
            set
            {
                s_InvertCulling = value;
                GfxStream.SetGlFlag(GfxStream.CameraInvertCulling, value);
            }
        }

        static bool s_Wireframe, s_SrgbWrite = true, s_InvertCulling;
    }
}
