using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Trees whose prototype materials' shaders name a BillboardShader (the Soft Occlusion and Tree
    /// Creator shaders; prefabs without a LOD group), drawn as the Unity 6000.0.71f1 Editor draws
    /// them (tests/projects/treebillboards):
    /// <list type="bullet">
    /// <item>as their meshes up to the billboard start plus the cross fade (the start the lesser of
    /// treeBillboardDistance and treeDistance; the fade treeCrossFadeLength, at most what is left
    /// to treeDistance), distances taken across the ground (x and z) from the camera, unrotated, the
    /// tree's scales and color given to the shaders (_TreeInstanceScale, _TreeInstanceColor), no
    /// bending without wind (_TerrainEngineBendTree the identity), and squashed towards a plane
    /// facing the camera across the fade (_SquashAmount from 1 to 0.04 by a smoothstep);</item>
    /// <item>past that as billboards: camera-facing quads cut from an atlas into which each
    /// prototype is drawn with its BillboardShader copies, seen orthographically from the camera's
    /// pitch and yaw, redrawn when either turns by more than 0.1 degree or its lights change; drawn back to front with
    /// Hidden/TerrainEngine/BillboardTree, whose vertex program drops those past treeDistance.</item>
    /// </list>
    /// Unmeasured: wind zones (warned; trees drawn unbent), several lights (the first four
    /// directional lights light the atlas), more than 500 trees (the Editor batches them by area,
    /// drawing far areas in another order). treeMaximumFullLODCount changes nothing (measured,
    /// tests/projects/treebillboards maxmesh3).
    /// </summary>
    internal sealed partial class TerrainRenderer
    {
        /// <summary>A prototype drawn as legacy trees: its mesh and materials, the copies its atlas
        /// cell is drawn with, and the billboard's size from the mesh's bounds.</summary>
        sealed class LegacyPrototype
        {
            internal Mesh Mesh;
            internal Material[] ImposterMaterials;
            /// <summary>The prefab's own scale (its root's lossy scale).</summary>
            internal Vector3 Scale;
            /// <summary>The bounds' height, top, and width (the farthest the bounds reach from
            /// the tree's axis across the ground, twice, padded); width over height.</summary>
            internal float Height, Top, Width, Ratio;
            /// <summary>The prototype's cell of the atlas (u from x to x + width, all v).</summary>
            internal float CellX, CellWidth;
        }

        /// <summary>A legacy tree's draw state: what its shaders get, kept between frames so the
        /// values are boxed again only when they change.</summary>
        sealed class LegacyTree
        {
            internal LegacyPrototype Prototype;
            internal float WidthScale, HeightScale;
            internal Color32 Color;
            internal readonly Dictionary<int, object> Properties = new Dictionary<int, object>(6);
            internal readonly BoxedVectors Boxes = new BoxedVectors(4);
            internal object SquashBox;
            internal float Squash = float.NaN;
        }

        /// <summary>The width of a legacy tree's billboard over its bounds' widest reach (both
        /// sides, padded).</summary>
        const float BillboardWidthScale = 1.9f;
        /// <summary>The atlas's height, and the width of a prototype's cell per unit of its ratio
        /// (at most 1), plus a texel each.</summary>
        const int AtlasHeight = 256;
        const int MaxAtlasWidth = 2048;
        /// <summary>How far, in degrees, the camera turns before the atlas is drawn again.</summary>
        const float AtlasTurn = 0.1f;
        /// <summary>The squash a tree reaches at the end of the fade, and the fade length under
        /// which there is none.</summary>
        const float FullSquash = 0.04f;
        const float NoFade = 1e-6f;
        /// <summary>The atlas's clear color and the cameras' default clip planes it is drawn with.</summary>
        static readonly Color s_AtlasClear = new Color(0.2f, 0.2f, 0.2f, 0f);
        const float AtlasNear = 0.3f, AtlasFar = 1000f;

        static readonly int s_TerrainEngineBendTree = Shader.PropertyToID("_TerrainEngineBendTree");
        static readonly int s_TreeInstanceColor = Shader.PropertyToID("_TreeInstanceColor");
        static readonly int s_TreeInstanceScale = Shader.PropertyToID("_TreeInstanceScale");
        static readonly int s_SquashPlaneNormal = Shader.PropertyToID("_SquashPlaneNormal");
        static readonly int s_SquashAmount = Shader.PropertyToID("_SquashAmount");
        static readonly int s_Wind = Shader.PropertyToID("_Wind");
        static readonly int s_BillboardRight = Shader.PropertyToID("_TreeBillboardCameraRight");
        static readonly int s_BillboardUp = Shader.PropertyToID("_TreeBillboardCameraUp");
        static readonly int s_BillboardFront = Shader.PropertyToID("_TreeBillboardCameraFront");
        static readonly int s_BillboardPos = Shader.PropertyToID("_TreeBillboardCameraPos");
        static readonly int s_BillboardDistances = Shader.PropertyToID("_TreeBillboardDistances");
        static readonly int s_TreeLightDirections = Shader.PropertyToID("_TerrainTreeLightDirections");
        static readonly int s_TreeLightColors = Shader.PropertyToID("_TerrainTreeLightColors");
        static readonly int s_Color = Shader.PropertyToID("_Color");
        static readonly int s_Cutoff = Shader.PropertyToID("_Cutoff");
        static readonly int s_HalfOverCutoff = Shader.PropertyToID("_HalfOverCutoff");
        static readonly object s_Identity = Matrix4x4.identity;
        static readonly object s_NoWind = Vector4.zero;

        LegacyPrototype[] m_Legacy = Array.Empty<LegacyPrototype>();
        RenderTexture m_Atlas;
        Material m_BillboardMaterial;
        TreeRenderer m_BillboardRenderer;
        Mesh m_BillboardMesh;
        /// <summary>The camera angles (degrees) the atlas was last drawn from; NaN: not drawn.</summary>
        float m_AtlasPitch = float.NaN, m_AtlasYaw = float.NaN;
        Matrix4x4 m_AtlasCameraToWorld = Matrix4x4.identity;
        readonly Dictionary<int, object> m_BillboardProperties = new Dictionary<int, object>(5);
        readonly BoxedVectors m_BillboardBoxes = new BoxedVectors(5);
        readonly List<(float Distance, int Index)> m_Billboards = new List<(float, int)>();
        int[] m_BillboardIndexArray = Array.Empty<int>();
        /// <summary>The trees the billboard mesh holds, in its order, and the color space it was
        /// filled in.</summary>
        readonly List<int> m_BillboardOrder = new List<int>();
        bool m_BillboardsLinear;
        static readonly Vector4[] s_LightDirections = new Vector4[4], s_LightColors = new Vector4[4];
        /// <summary>The tree lights the atlas was last drawn with.</summary>
        readonly Vector4[] m_AtlasLightDirections = new Vector4[4], m_AtlasLightColors = new Vector4[4];
        static readonly Comparison<(float Distance, int Index)> s_FarFirst =
            (a, b) => a.Distance != b.Distance ? b.Distance.CompareTo(a.Distance) : a.Index.CompareTo(b.Index);

        /// <summary>The legacy prototype of a tree prototype (null when its trees are not legacy).</summary>
        LegacyPrototype LegacyOf(TreePrototype prototype)
        {
            var prefab = prototype?.prefab;
            if (prefab == null || !NamesBillboardShader(prefab)) return null;
            var renderer = prefab.GetComponent<MeshRenderer>();
            var filter = prefab.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (renderer == null || mesh == null)
            {
                Warn($"tree prototype '{prefab.name}' has materials made for billboards but no mesh on its root: Unbound draws it as its meshes at every distance, without billboards.");
                return null;
            }
            var bounds = mesh.bounds;
            Vector3 c = bounds.center, e = bounds.extents;
            float reach = Mathf.Max(Mathf.Max(Mathf.Abs(c.x + e.x), Mathf.Abs(c.x - e.x)), Mathf.Max(Mathf.Abs(c.z + e.z), Mathf.Abs(c.z - e.z)));
            var materials = renderer.sharedMaterials;
            var legacy = new LegacyPrototype
            {
                Mesh = mesh,
                Scale = prefab.transform.lossyScale,
                Height = 2f * e.y,
                Top = c.y + e.y,
                Width = reach * BillboardWidthScale,
                ImposterMaterials = new Material[materials.Length],
            };
            legacy.Ratio = legacy.Width / legacy.Height;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                // A copy drawn with the shader's BillboardShader, given the material's color and
                // its cutoff as 0.5 / cutoff, unbent.
                var copy = new Material(materials[i]) { hideFlags = HideFlags.HideAndDontSave };
                var billboard = materials[i].shader != null ? materials[i].shader.GetDependency("BillboardShader") : null;
                if (billboard != null)
                {
                    copy.shader = billboard;
                    copy.SetColor(s_Color, materials[i].HasProperty(s_Color) ? materials[i].GetColor(s_Color) : Color.white);
                    copy.SetFloat(s_HalfOverCutoff, materials[i].HasProperty(s_Cutoff) ? 0.5f / materials[i].GetFloat(s_Cutoff) : 1f);
                    copy.SetMatrix(s_TerrainEngineBendTree, Matrix4x4.identity);
                }
                legacy.ImposterMaterials[i] = copy;
            }
            return legacy;
        }

        /// <summary>A legacy tree's part: its prototype's mesh at the tree's place, unturned (the
        /// shaders scale it), its color the tree's color times its lightmap color.</summary>
        TreePart LegacyPart(Terrain terrain, LegacyPrototype legacy, GameObject prefab, TreeInstance tree, Vector3 world, Vector3 origin)
        {
            var source = prefab.GetComponent<MeshRenderer>();
            var renderer = (TreeRenderer)ComponentFactory.Create(typeof(TreeRenderer), terrain.gameObject);
            renderer.hideFlags = HideFlags.HideAndDontSave;
            renderer.m_Materials = source.sharedMaterials;
            renderer.shadowCastingMode = source.shadowCastingMode;
            renderer.receiveShadows = source.receiveShadows;
            var scale = new Vector3(legacy.Scale.x * tree.widthScale, legacy.Scale.y * tree.heightScale, legacy.Scale.z * tree.widthScale);
            var bounds = legacy.Mesh.bounds;
            renderer.m_LocalBounds = new Bounds(Vector3.Scale(bounds.center, scale) + (world - origin), Vector3.Scale(bounds.size, Abs(scale)));
            return new TreePart
            {
                Renderer = renderer,
                Mesh = legacy.Mesh,
                Model = Matrix4x4.Translate(world),
                Position = world,
                Shadows = source.shadowCastingMode,
                Legacy = new LegacyTree
                {
                    Prototype = legacy,
                    WidthScale = tree.widthScale,
                    HeightScale = tree.heightScale,
                    Color = Tinted(tree.color, tree.lightmapColor),
                },
            };
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>A tree's color times its lightmap color, per 8-bit channel (c × (l + 1) / 256).</summary>
        internal static Color32 Tinted(Color32 color, Color32 lightmap) => new Color32(
            (byte)(color.r * (lightmap.r + 1) >> 8), (byte)(color.g * (lightmap.g + 1) >> 8),
            (byte)(color.b * (lightmap.b + 1) >> 8), (byte)(color.a * (lightmap.a + 1) >> 8));

        /// <summary>Lays the billboard prototypes out in the atlas, each a cell as wide as its
        /// ratio (at most 1) of the atlas's height, a texel apart; the atlas as wide as all of them
        /// rounded to the nearest power of two (at most 2048).</summary>
        void LayOutAtlas()
        {
            float total = 0f;
            foreach (var p in m_Legacy)
                if (p != null) total += Mathf.Min(p.Ratio, 1f) * AtlasHeight + 1f;
            total = Mathf.Max(total, 1f);
            int width = Math.Min(NearestPowerOfTwo((int)(total + 0.5f)), MaxAtlasWidth);
            float texel = 1f / width, cursor = 0f;
            foreach (var p in m_Legacy)
            {
                if (p == null) continue;
                float share = Mathf.Min(p.Ratio, 1f) * AtlasHeight / total;
                p.CellX = cursor + texel;
                p.CellWidth = Mathf.Max(share - 2f * texel, 0f);
                cursor += share;
            }
            if (m_Atlas == null || m_Atlas.width != width)
            {
                if (m_Atlas != null) DestroyImmediate(m_Atlas);
                m_Atlas = new RenderTexture(width, AtlasHeight, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "Tree Imposter Texture",
                    hideFlags = HideFlags.HideAndDontSave,
                    useMipMap = true,
                    autoGenerateMips = false,
                    mipMapBias = -1f,
                };
            }
            m_AtlasPitch = m_AtlasYaw = float.NaN;
        }

        /// <summary>The power of two nearest to <paramref name="n"/> (the higher one on a tie).</summary>
        internal static int NearestPowerOfTwo(int n)
        {
            if (n <= 1) return 1;
            int upper = Mathf.NextPowerOfTwo(n), lower = upper >> 1;
            return n - lower < upper - n ? lower : upper;
        }

        /// <summary>Draws each billboard prototype into its atlas cell, seen from the camera's pitch
        /// and yaw (no roll), orthographically, from twice its height and width back from its middle,
        /// lit by the first four directional lights.</summary>
        void DrawAtlas(float pitch, float yaw)
        {
            m_AtlasPitch = pitch;
            m_AtlasYaw = yaw;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            var forward = rotation * Vector3.forward;
            Array.Copy(s_LightDirections, m_AtlasLightDirections, 4);
            Array.Copy(s_LightColors, m_AtlasLightColors, 4);
            Shader.SetGlobalVectorArray(s_TreeLightDirections, s_LightDirections);
            Shader.SetGlobalVectorArray(s_TreeLightColors, s_LightColors);
            var saved = GfxStream.Current;
            GfxStream.SetTarget(m_Atlas.EnsureCreated());
            // A camera's clear color: made linear in a linear-space project.
            var clear = QualitySettings.activeColorSpace == ColorSpace.Linear ? s_AtlasClear.linear : s_AtlasClear;
            GfxStream.Clear(GfxStream.ClearColor | GfxStream.ClearDepth | GfxStream.ClearStencil, clear, 1f);
            foreach (var p in m_Legacy)
            {
                if (p == null || p.CellWidth <= 0f) continue;
                var position = new Vector3(0f, p.Top - p.Height * 0.5f, 0f) - forward * (2f * (p.Height + p.Width));
                var cameraToWorld = Matrix4x4.TRS(position, rotation, Vector3.one);
                m_AtlasCameraToWorld = cameraToWorld;
                var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * cameraToWorld.inverseDoublePrecision;
                float size = Mathf.Max(p.Width, p.Height) * 0.5f;
                var pixels = new Rect(p.CellX * m_Atlas.width, 0f, p.CellWidth * m_Atlas.width, AtlasHeight);
                // The cell's width over its height, whatever its pixels: the billboard's ratio (at most 1).
                float aspect = Mathf.Min(p.Ratio, 1f);
                GfxStream.Viewport(pixels);
                GfxStream.Camera(view, Matrix4x4.Ortho(-size * aspect, size * aspect, -size, size, AtlasNear, AtlasFar));
                ulong mesh = p.Mesh.Sync();
                for (int i = 0; i < p.ImposterMaterials.Length; i++)
                {
                    var material = p.ImposterMaterials[i];
                    if (material == null || i >= p.Mesh.subMeshCount) continue;
                    ulong handle = material.Sync();
                    for (int pass = 0; pass < material.passCount; pass++)
                        GfxStream.Draw(mesh, i, handle, pass, Matrix4x4.identity, Color.white);
                }
            }
            GfxStream.Restore(saved);
            m_Atlas.GenerateMips();
        }

        /// <summary>The tree lights (_TerrainTreeLightDirections, _TerrainTreeLightColors): the
        /// first four enabled directional lights, each the direction towards it and its final
        /// color (as the vertex lights get it), the rest zero.</summary>
        static void GatherTreeLights()
        {
            int n = 0;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            foreach (var light in Light.s_Enabled)
            {
                if (n == 4) break;
                if (light == null || light.type != LightType.Directional) continue;
                s_LightDirections[n] = -(light.transform.rotation * Vector3.forward);
                s_LightColors[n] = (Vector4)light.FinalColor(linear);
                n++;
            }
            for (int i = n; i < 4; i++) s_LightDirections[i] = s_LightColors[i] = Vector4.zero;
        }

        /// <summary>Whether the tree lights differ from those the atlas was drawn with.</summary>
        bool TreeLightsChanged()
        {
            for (int i = 0; i < 4; i++)
                if (!FloatBits.Same(s_LightDirections[i], m_AtlasLightDirections[i]) || !FloatBits.Same(s_LightColors[i], m_AtlasLightColors[i]))
                    return true;
            return false;
        }

        /// <summary>The billboard's turn factors for a pitch (degrees): how far its height leans
        /// to its width (1 - cos pitch), and how far it is pushed towards the camera (at least 0.1,
        /// rising by a smoothstep from 40 to 90 degrees).</summary>
        internal static void BillboardFactors(float pitch, out float angle, out float offset)
        {
            if (pitch > 90f) pitch -= 360f;
            angle = 1f - Mathf.Cos(pitch * Mathf.Deg2Rad);
            float t = Mathf.Clamp01((Mathf.Abs(pitch) - 40f) / 50f);
            offset = Mathf.Max(0.1f, Mathf.Abs(pitch) >= 40f ? 3f * t * t - 2f * t * t * t : 0f);
        }

        /// <summary>The squash of a mesh tree at <paramref name="distance"/> (across the ground):
        /// 1 up to the billboard start, falling by a smoothstep to <see cref="FullSquash"/> at the end
        /// of the fade.</summary>
        internal static float SquashAt(float distance, float start, float fade)
        {
            if (!(fade > NoFade)) return 1f;
            float t = Mathf.Clamp01((start + fade - distance) / fade);
            float s = 3f * t * t - 2f * t * t * t;
            return (1f - s) * FullSquash + s;
        }

        /// <summary>A legacy tree drawn as its mesh: its scales, color, squash and an unbent bend.</summary>
        void AddLegacyMesh(in TreePart part, float distance, float start, float fade, Vector3 viewDirection, float offsetFactor,
            FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var tree = part.Legacy;
            var p = tree.Prototype;
            float squash = SquashAt(distance, start, fade);
            var props = tree.Properties;
            props[s_TerrainEngineBendTree] = s_Identity;
            props[s_Wind] = s_NoWind;
            // A color: made linear in a linear-space project.
            var tint = (Color)tree.Color;
            props[s_TreeInstanceColor] = tree.Boxes.Box(0, QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint);
            props[s_TreeInstanceScale] = tree.Boxes.Box(1, new Vector4(p.Scale.x * tree.WidthScale, p.Scale.y * tree.HeightScale, p.Scale.z * tree.WidthScale, 1f));
            props[s_SquashPlaneNormal] = tree.Boxes.Box(2, new Vector4(viewDirection.x, viewDirection.y, viewDirection.z,
                p.Width * tree.WidthScale * 0.5f * offsetFactor));
            if (tree.SquashBox == null || tree.Squash != squash)
            {
                tree.Squash = squash;
                tree.SquashBox = squash;
            }
            props[s_SquashAmount] = tree.SquashBox;
            // A tree squashing into its billboard casts no shadow.
            part.Renderer.shadowCastingMode = squash < 1f ? Rendering.ShadowCastingMode.Off : part.Shadows;
            FrameBuilder.AddMeshDraws(part.Renderer, part.Mesh, part.Model, Color.white, camera, into, properties: props);
        }

        /// <summary>The billboards of the frame, back to front, in one mesh drawn with
        /// Hidden/TerrainEngine/BillboardTree: each a quad at its tree's place, as wide as its
        /// prototype's billboard and as high as the larger of its width and height, cut from its
        /// atlas cell, colored by the tree's color.</summary>
        void AddBillboards(Terrain terrain, FrameBuilder.CameraContext camera, float treeDistance, List<FrameBuilder.DrawItem> into)
        {
            if (m_Billboards.Count == 0) return;
            var cameraObject = camera.Camera;
            var euler = cameraObject != null ? cameraObject.transform.eulerAngles : Quaternion.LookRotationSilent(camera.Forward).eulerAngles;
            // Drawn again when the camera turns or the lights it was lit by change (measured: a
            // sun's intensity alone, tests/projects/treebillboards sunonly).
            GatherTreeLights();
            if (float.IsNaN(m_AtlasPitch) || Mathf.Abs(Mathf.DeltaAngle(euler.x, m_AtlasPitch)) >= AtlasTurn
                || Mathf.Abs(Mathf.DeltaAngle(euler.y, m_AtlasYaw)) >= AtlasTurn || !m_Atlas.IsCreated() || TreeLightsChanged())
                DrawAtlas(euler.x, euler.y);
            if (m_BillboardMaterial == null)
            {
                var shader = Shader.Find("Hidden/TerrainEngine/BillboardTree");
                if (shader == null)
                {
                    Warn("Hidden/TerrainEngine/BillboardTree is missing: terrain trees past their billboard start are not drawn.");
                    return;
                }
                m_BillboardMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                m_BillboardMesh = new Mesh { name = "Tree billboards", hideFlags = HideFlags.HideAndDontSave };
                m_BillboardMesh.MarkDynamic();
                m_BillboardRenderer = (TreeRenderer)ComponentFactory.Create(typeof(TreeRenderer), terrain.gameObject);
                m_BillboardRenderer.hideFlags = HideFlags.HideAndDontSave;
                m_BillboardRenderer.m_Materials = new[] { m_BillboardMaterial };
            }
            m_BillboardMaterial.mainTexture = m_Atlas;
            m_Billboards.Sort(s_FarFirst);
            // The mesh changes only with the billboards drawn and their order.
            if (!SameBillboards()) FillBillboardMesh();
            BillboardFactors(m_AtlasPitch, out float angle, out float offset);
            var c2w = m_AtlasCameraToWorld;
            var pos = camera.Position;
            m_BillboardProperties[s_BillboardRight] = m_BillboardBoxes.Box(0, new Vector4(c2w.m00, c2w.m10, c2w.m20, 0f));
            m_BillboardProperties[s_BillboardUp] = m_BillboardBoxes.Box(1, new Vector4(c2w.m01, c2w.m11, c2w.m21, offset));
            m_BillboardProperties[s_BillboardFront] = m_BillboardBoxes.Box(2, new Vector4(-c2w.m02, -c2w.m12, -c2w.m22, 0f));
            m_BillboardProperties[s_BillboardPos] = m_BillboardBoxes.Box(3, new Vector4(pos.x, pos.y, pos.z, angle));
            m_BillboardProperties[s_BillboardDistances] = m_BillboardBoxes.Box(4, new Vector4(treeDistance * treeDistance, 0f, 0f, 0f));
            m_BillboardRenderer.m_LocalBounds = m_BillboardMesh.bounds;
            m_BillboardRenderer.m_LocalBounds.center -= terrain.GetPosition();
            m_BillboardRenderer.renderingLayerMask = renderingLayerMask;
            FrameBuilder.AddMeshDraws(m_BillboardRenderer, m_BillboardMesh, Matrix4x4.identity, Color.white, camera, into, properties: m_BillboardProperties);
        }

        /// <summary>Whether the billboards to draw are the ones the mesh holds, in its order (and
        /// the color space it was filled in); remembers them when not.</summary>
        bool SameBillboards()
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            bool same = m_BillboardsLinear == linear && m_BillboardOrder.Count == m_Billboards.Count;
            for (int i = 0; same && i < m_Billboards.Count; i++) same = m_BillboardOrder[i] == m_Billboards[i].Index;
            if (same) return true;
            m_BillboardsLinear = linear;
            m_BillboardOrder.Clear();
            foreach (var (_, index) in m_Billboards) m_BillboardOrder.Add(index);
            return false;
        }

        /// <summary>Writes the billboards into the mesh's channel arrays, kept at a capacity
        /// (streamed: only the billboards' share is uploaded), with bounds that hold every
        /// billboard however it turns.</summary>
        void FillBillboardMesh()
        {
            var mesh = m_BillboardMesh;
            int vertices = m_Billboards.Count * 4, indices = m_Billboards.Count * 6;
            int length = Mesh.StreamCapacity(mesh.m_Vertices?.Length ?? 0, vertices);
            if (mesh.m_Vertices == null || mesh.m_Vertices.Length != length)
            {
                mesh.AdoptVertices(new Vector3[length]);
                mesh.m_Colors = new Color32[length];
                mesh.m_UVs[0] = new Vector4[length];
                mesh.m_UVs[1] = new Vector4[length];
                mesh.m_UVDimensions[0] = mesh.m_UVDimensions[1] = 2;
            }
            if (m_BillboardIndexArray.Length < indices) m_BillboardIndexArray = new int[Mesh.StreamCapacity(m_BillboardIndexArray.Length, indices)];
            Vector3[] positions = mesh.m_Vertices;
            Color32[] colors = mesh.m_Colors;
            Vector4[] uv = mesh.m_UVs[0], offsets = mesh.m_UVs[1];
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            int v = 0, k = 0;
            foreach (var (_, index) in m_Billboards)
            {
                var part = m_Trees[index];
                var tree = part.Legacy;
                var p = tree.Prototype;
                float sx = p.Scale.x * tree.WidthScale, sy = p.Scale.y * tree.HeightScale;
                float larger = Mathf.Max(p.Width, p.Height);
                float pad = (larger - p.Height) * 0.5f;
                float halfWidth = p.Width * sx * 0.5f;
                float bottom = -((p.Height - p.Top) + pad) * sy, top = (p.Top + pad) * sy;
                float radius = sx * larger * 0.5f;
                Color32 color = tree.Color;
                if (linear)
                {
                    var c = ((Color)color).linear;
                    color = new Color32((byte)(Mathf.Clamp01(c.r) * 255f + 0.5f), (byte)(Mathf.Clamp01(c.g) * 255f + 0.5f), (byte)(Mathf.Clamp01(c.b) * 255f + 0.5f), color.a);
                }
                for (int corner = 0; corner < 4; corner++)
                {
                    bool right = (corner & 1) != 0, upper = corner >= 2;
                    positions[v + corner] = part.Position;
                    colors[v + corner] = color;
                    uv[v + corner] = new Vector4(right ? p.CellX + p.CellWidth : p.CellX, upper ? radius : -radius, 0f, 0f);
                    offsets[v + corner] = new Vector4(right ? halfWidth : -halfWidth, upper ? top : bottom, 0f, 0f);
                }
                var indexArray = m_BillboardIndexArray;
                indexArray[k] = v;
                indexArray[k + 1] = v + 1;
                indexArray[k + 2] = v + 2;
                indexArray[k + 3] = v + 2;
                indexArray[k + 4] = v + 1;
                indexArray[k + 5] = v + 3;
                // However it turns, the quad stays within its width (across and the push towards
                // the camera) plus its largest height offset of its place.
                float reach = 2f * Mathf.Abs(halfWidth) + Mathf.Max(Mathf.Max(Mathf.Abs(bottom), Mathf.Abs(top)), Mathf.Abs(radius));
                min = Vector3.Min(min, part.Position - new Vector3(reach, reach, reach));
                max = Vector3.Max(max, part.Position + new Vector3(reach, reach, reach));
                v += 4;
                k += 6;
            }
            mesh.WriteStreamedTriangles(m_BillboardIndexArray, indices, vertices > ushort.MaxValue, streaming: true);
            mesh.FinishStreamedWrite(vertices, indices, streaming: true);
            if (vertices > 0) mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
        }

        void DestroyBillboards()
        {
            foreach (var p in m_Legacy)
                if (p != null)
                    foreach (var m in p.ImposterMaterials)
                        if (m != null) DestroyImmediate(m);
            m_Legacy = Array.Empty<LegacyPrototype>();
            if (m_Atlas != null) DestroyImmediate(m_Atlas);
            m_Atlas = null;
            if (m_BillboardMaterial != null) DestroyImmediate(m_BillboardMaterial);
            m_BillboardMaterial = null;
            if (m_BillboardMesh != null) DestroyImmediate(m_BillboardMesh);
            m_BillboardMesh = null;
            m_BillboardRenderer?.OnEngineDestroy();
            m_BillboardRenderer = null;
            m_BillboardOrder.Clear();
            m_AtlasPitch = m_AtlasYaw = float.NaN;
        }
    }
}
