using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// What draws a terrain: a renderer the camera collects like a mesh renderer's (lit, shadowed,
    /// casting) but no component of its GameObject (a terrain's GameObject has no Renderer). Its
    /// meshes are the heightmap's patches at the camera's level of detail (TerrainPatches.cs),
    /// drawn with a copy of the terrain's material template carrying the splat maps and four
    /// layers, and one material of the template's AddPassShader per four more layers; far patches
    /// use the base map (TerrainBaseMap.cs).
    /// </summary>
    /// <summary>A placed tree's part as the lights and shadows see it: its bounds in the terrain's
    /// space, the prefab renderer's materials and shadow settings.</summary>
    internal sealed class TreeRenderer : Renderer
    {
        internal Bounds m_LocalBounds;

        internal override Bounds GetLocalBounds() => m_LocalBounds;
    }

    internal sealed partial class TerrainRenderer : Renderer
    {
        internal Terrain m_Terrain;
        Material m_Template;
        /// <summary>The template's values and shader the passes were made from: a change of the
        /// template's values draws (measured: tests/projects/propertyblocks `materialChanged`).</summary>
        int m_TemplateVersion = -1;
        Shader m_TemplateShader;
        Material[] m_Passes = Array.Empty<Material>();
        int m_SplatVersion = -1;
        TerrainData m_SplatData;
        Texture2D m_Holes;
        static readonly HashSet<string> s_Warned = new HashSet<string>();

        internal static TerrainRenderer For(Terrain terrain)
        {
            var renderer = (TerrainRenderer)ComponentFactory.Create(typeof(TerrainRenderer), terrain.gameObject);
            renderer.m_Terrain = terrain;
            renderer.hideFlags = HideFlags.HideAndDontSave;
            return renderer;
        }

        internal override Bounds GetLocalBounds()
        {
            var data = m_Terrain != null ? m_Terrain.terrainData : null;
            return data != null ? data.bounds : default;
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var terrain = m_Terrain;
            var data = terrain != null ? terrain.terrainData : null;
            if (data == null) return;
            if (terrain.drawHeightmap)
            {
                shadowCastingMode = terrain.shadowCastingMode;
                receiveShadows = true;
                // Bind per terrain on every collection: indices, STs and LightmapSettings can
                // change live even when TerrainData and the material template stay the same.
                lightmapIndex = terrain.lightmapIndex;
                lightmapScaleOffset = terrain.lightmapScaleOffset;
                realtimeLightmapIndex = terrain.realtimeLightmapIndex;
                realtimeLightmapScaleOffset = terrain.realtimeLightmapScaleOffset;
                m_RealtimeLightmapFromData = terrain.m_RealtimeLightmapFromData;
                m_PropertyBlock = terrain.SplatProperties;
                UpdateMaterials(terrain, data);
                if (m_Passes.Length > 0)
                {
                    UpdateBaseMaps(data);
                    // Each patch draws its single submesh with every splat pass, or with the base
                    // map material (TerrainBaseMap.cs).
                    m_Materials = m_DrawMaterials;
                    CollectPatches(terrain, data, camera, into);
                }
            }
            if (terrain.drawTreesAndFoliage)
            {
                CollectTrees(terrain, data, camera, into);
                CollectDetails(terrain, data, camera, into);
            }
        }

        // ------------------------------------------------------------ trees

        /// <summary>A part of a placed tree: one of its prefab's mesh renderers.</summary>
        struct TreePart
        {
            internal TreeRenderer Renderer;
            internal Mesh Mesh;
            internal Matrix4x4 Model;
            internal Vector3 Position;
            /// <summary>The LOD levels the part is in (bits), 0 without a LOD group.</summary>
            internal int Levels;
            /// <summary>The tree's LOD group (its prefab's) and its world reference and size.</summary>
            internal LODGroup Group;
            internal Vector3 Reference;
            internal float Size;
            /// <summary>A legacy tree's draw state (null for other trees), and its prefab
            /// renderer's shadow casting.</summary>
            internal LegacyTree Legacy;
            internal Rendering.ShadowCastingMode Shadows;
        }

        readonly List<TreePart> m_Trees = new List<TreePart>();
        TerrainData m_TreesData;
        int m_TreesVersion = -1;
        Vector3 m_TreesOrigin;

        /// <summary>The trees within the tree distance, each its prefab's meshes (a LODGroup's first
        /// level) at the tree's place, turned about up by its rotation and scaled by its width and
        /// height scales and the prefab's scale.</summary>
        void CollectTrees(Terrain terrain, TerrainData data, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var origin = terrain.GetPosition();
            if (m_TreesData != data || m_TreesVersion != data.m_TreesVersion || m_TreesOrigin != origin) BuildTrees(terrain, data, origin);
            float distance = terrain.treeDistance;
            // Legacy trees: meshes up to the billboard start and the fade, across the ground.
            float start = Mathf.Min(distance, terrain.treeBillboardDistance);
            float fade = Mathf.Min(terrain.treeCrossFadeLength, distance - start);
            if (!(fade >= 0f)) fade = 0f;
            float meshes = (start + fade) * (start + fade), trees = distance * distance;
            float offsetFactor = 0.1f;
            if (m_Atlas != null && !float.IsNaN(m_AtlasPitch)) BillboardFactors(m_AtlasPitch, out _, out offsetFactor);
            m_Billboards.Clear();
            for (int index = 0; index < m_Trees.Count; index++)
            {
                var part = m_Trees[index];
                if (part.Legacy != null)
                {
                    float dx = part.Position.x - camera.Position.x, dz = part.Position.z - camera.Position.z;
                    float across = dx * dx + dz * dz;
                    if (across >= trees) continue;
                    if (across >= meshes && m_Atlas != null)
                    {
                        // One entry per tree: its first part stands for it.
                        if (part.Mesh != null) m_Billboards.Add((across, index));
                        continue;
                    }
                    part.Renderer.renderingLayerMask = renderingLayerMask;
                    AddLegacyMesh(part, Mathf.Sqrt(across), start, fade, camera.Forward, offsetFactor, camera, into);
                    continue;
                }
                if ((part.Position - camera.Position).sqrMagnitude > distance * distance) continue;
                if (part.Group != null)
                {
                    int level = part.Group.LevelFor(camera.Camera, camera.Position, part.Reference, part.Size);
                    if (level < 0 || (part.Levels & (1 << level)) == 0) continue;
                }
                // Collected under the terrain renderer's filter, the trees take its rendering
                // layers for lighting too (unmeasured: Unbound's choice).
                part.Renderer.renderingLayerMask = renderingLayerMask;
                FrameBuilder.AddMeshDraws(part.Renderer, part.Mesh, part.Model, Color.white, camera, into);
            }
            AddBillboards(terrain, camera, distance, into);
        }

        void BuildTrees(Terrain terrain, TerrainData data, Vector3 origin)
        {
            foreach (var part in m_Trees) part.Renderer.OnEngineDestroy();
            m_Trees.Clear();
            DestroyBillboards();
            m_TreesData = data;
            m_TreesVersion = data.m_TreesVersion;
            m_TreesOrigin = origin;
            var prototypes = data.treePrototypes;
            var size = data.size;
            m_Legacy = new LegacyPrototype[prototypes.Length];
            bool anyLegacy = false;
            for (int i = 0; i < prototypes.Length; i++)
                anyLegacy |= (m_Legacy[i] = TerrainData.IsPersistentPrefab(prototypes[i]?.prefab) ? LegacyOf(prototypes[i]) : null) != null;
            if (anyLegacy) LayOutAtlas();
            if (anyLegacy && Object.FindAnyObjectByType<WindZone>() != null)
                Warn("terrain trees drawn with the Soft Occlusion or Tree Creator shaders are not bent by wind zones in Unbound.");
            foreach (var tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length) continue;
                var prefab = prototypes[tree.prototypeIndex]?.prefab;
                if (!TerrainData.IsPersistentPrefab(prefab)) continue;
                var root = prefab.transform;
                var world = origin + Vector3.Scale(tree.position, size);
                if (m_Legacy[tree.prototypeIndex] is LegacyPrototype legacy)
                {
                    m_Trees.Add(LegacyPart(terrain, legacy, prefab, tree, world, origin));
                    continue;
                }
                var placement = Matrix4x4.TRS(world, QuaternionMath.AngleAxis(tree.rotation * Mathf.Rad2Deg, Vector3.up), new Vector3(tree.widthScale, tree.heightScale, tree.widthScale))
                    * Matrix4x4.TRS(Vector3.zero, root.rotation, root.localScale);
                var group = prefab.GetComponent<LODGroup>();
                var reference = group != null ? placement.MultiplyPoint3x4(group.localReferencePoint) : world;
                float groupSize = group != null ? group.size * Mathf.Max(Mathf.Abs(root.localScale.x * tree.widthScale), Mathf.Abs(root.localScale.y * tree.heightScale)) : 0f;
                foreach (var (source, levels) in PartsOf(prefab))
                {
                    var filter = source.GetComponent<MeshFilter>();
                    var mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null) continue;
                    var model = placement * (root.worldToLocalMatrix * source.transform.localToWorldMatrix);
                    var renderer = (TreeRenderer)ComponentFactory.Create(typeof(TreeRenderer), terrain.gameObject);
                    renderer.hideFlags = HideFlags.HideAndDontSave;
                    renderer.m_Materials = source.sharedMaterials;
                    renderer.shadowCastingMode = source.shadowCastingMode;
                    renderer.receiveShadows = source.receiveShadows;
                    renderer.m_LocalBounds = Transformed(Matrix4x4.Translate(-origin) * model, mesh.bounds);
                    if (UsesTreeInstanceProperties(source.sharedMaterials))
                    {
                        var block = new MaterialPropertyBlock();
                        block.SetColor("_TreeInstanceColor", tree.color);
                        block.SetVector("_TreeInstanceScale", new Vector4(tree.widthScale, tree.heightScale, tree.widthScale, 0f));
                        renderer.m_PropertyBlock = block;
                    }
                    m_Trees.Add(new TreePart
                    {
                        Renderer = renderer,
                        Mesh = mesh,
                        Model = model,
                        Position = world,
                        Levels = levels,
                        Group = group,
                        Reference = reference,
                        Size = groupSize,
                    });
                }
            }
        }

        /// <summary>Whether a tree prefab without a LOD group has materials naming a BillboardShader
        /// (the Soft Occlusion and Tree Creator shaders do): the Editor draws such trees as
        /// billboards from a distance; a LOD group's trees take its levels instead.</summary>
        internal static bool NamesBillboardShader(GameObject prefab)
        {
            if (prefab.GetComponent<LODGroup>() != null) return false;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.shader != null && material.shader.GetDependency("BillboardShader") != null)
                        return true;
            return false;
        }

        /// <summary>A tree prefab's drawn mesh renderers with the LOD levels each is in: its LOD
        /// group's renderers, else its root's (the terrain draws no child renderers of a prefab
        /// without LOD levels).</summary>
        static IEnumerable<(MeshRenderer Renderer, int Levels)> PartsOf(GameObject prefab)
        {
            var group = prefab.GetComponent<LODGroup>();
            if (group != null)
            {
                var levels = new Dictionary<MeshRenderer, int>();
                var lods = group.GetLODs();
                for (int i = 0; i < lods.Length && i < 31; i++)
                    foreach (var r in lods[i].renderers)
                        if (r is MeshRenderer m && m)
                            levels[m] = (levels.TryGetValue(m, out var bits) ? bits : 0) | (1 << i);
                foreach (var pair in levels) yield return (pair.Key, pair.Value);
                yield break;
            }
            var root = prefab.GetComponent<MeshRenderer>();
            if (root != null) yield return (root, 0);
            else Warn($"tree prefab '{prefab.name}' has no MeshRenderer on its root: the terrain draws nothing for it.");
        }

        static bool UsesTreeInstanceProperties(Material[] materials)
        {
            foreach (var m in materials)
                if (m != null && m.shader != null && m.HasProperty("_TreeInstanceColor")) return true;
            return false;
        }

        static Bounds Transformed(Matrix4x4 m, Bounds b)
        {
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            var e = b.extents;
            for (int i = 0; i < 8; i++)
                result.Encapsulate(m.MultiplyPoint3x4(b.center + new Vector3((i & 1) != 0 ? e.x : -e.x, (i & 2) != 0 ? e.y : -e.y, (i & 4) != 0 ? e.z : -e.z)));
            return result;
        }

        internal Material[] PassesForTest => m_Passes;

        internal void UpdateMaterials(Terrain terrain, TerrainData data)
        {
            var template = terrain.materialTemplate;
            if (template == null) template = BuiltinObjects.DefaultTerrainMaterial;
            if (template == null) return;
            bool sameLayout = template == m_Template && template.shader == m_TemplateShader && m_SplatData == data
                && m_SplatVersion == data.m_SplatVersion + data.m_HeightsVersion * 7919;
            if (sameLayout && template.m_Version == m_TemplateVersion) return;
            if (sameLayout)
            {
                RefreshMaterials(template, data);
                return;
            }
            m_Template = template;
            m_TemplateShader = template.shader;
            m_SplatData = data;
            m_SplatVersion = data.m_SplatVersion + data.m_HeightsVersion * 7919;
            var layers = data.terrainLayers;
            var controls = data.AlphaTextures;
            int passes = Math.Max(1, (layers.Length + 3) / 4);
            var addShader = template.shader != null ? template.shader.GetDependency("AddPassShader") : null;
            if (passes > 1 && addShader == null)
            {
                Warn($"shader '{template.shader?.name}' has no AddPassShader: the terrain '{terrain.name}' draws its first four layers only.");
                passes = 1;
            }
            var materials = new Material[passes];
            for (int p = 0; p < passes; p++)
            {
                var material = p == 0 ? new Material(template) : new Material(addShader);
                if (p > 0) material.CopyPropertiesFromMaterial(template);
                material.name = p == 0 ? template.name + " (Terrain)" : addShader.name + " (Terrain)";
                material.hideFlags = HideFlags.HideAndDontSave;
                SetLayers(material, data, layers, p * 4, p < controls.Length ? controls[p] : null);
                SetHoles(material, data);
                materials[p] = material;
            }
            foreach (var old in m_Passes) if (old != null) DestroyImmediate(old);
            m_Passes = materials;
            UpdateBaseMaterials(template, data);
            m_TemplateVersion = template.m_Version;
        }

        /// <summary>The template's values changed (not its shader, the layers or the data): the
        /// materials made from it take them again in place, without new materials, and the base
        /// maps are made again.</summary>
        void RefreshMaterials(Material template, TerrainData data)
        {
            var layers = data.Layers;
            var controls = data.AlphaTextures;
            for (int p = 0; p < m_Passes.Length; p++)
            {
                var material = m_Passes[p];
                material.CopyPropertiesKeepingShader(template);
                SetLayers(material, data, layers, p * 4, p < controls.Length ? controls[p] : null);
                SetHoles(material, data);
            }
            RefreshBaseMaterials(template, data);
            m_TemplateVersion = template.m_Version;
        }

        /// <summary>A terrain with holes cuts them out of every material it draws with.</summary>
        void SetHoles(Material material, TerrainData data)
        {
            if (!data.HasHoles) return;
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetTexture("_TerrainHolesTexture", HolesTexture(data));
        }

        /// <summary>A pass's four layers: the control (splat) map, each layer's textures, tiling
        /// (the terrain's size over the tile size, the offset over it) and surface values; which
        /// layers have a mask map (_LayerHasMask0-3, and the _MASKMAP keyword when any does), and
        /// the terrain's layer count (_NumLayersCount). Matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/urpterrain (masks).</summary>
        internal static void SetLayers(Material material, TerrainData data, TerrainLayer[] layers, int first, Texture2D control)
        {
            var size = data.size;
            material.SetTexture(LayerIds.Control, control);
            bool normals = false, masks = false;
            if (layers.Length == 0 && first == 0)
            {
                // No layers: the built-in grey checker (two by two squares) in 2 m tiles, as the
                // Editor shows such a terrain whatever its size (measured).
                material.SetTexture(LayerIds.Control, DefaultTextures.Red);
                material.SetTexture(LayerIds.Splat[0], BuiltinObjects.DefaultCheckerGray);
                material.SetTextureScale(LayerIds.Splat[0], new Vector2(size.x / 2f, size.z / 2f));
                material.SetTextureOffset(LayerIds.Splat[0], Vector2.zero);
                material.SetFloat(LayerIds.Metallic[0], 0f);
                material.SetFloat(LayerIds.Smoothness[0], 0f);
                material.SetFloat(LayerIds.NormalScale[0], 1f);
                material.SetTexture(LayerIds.Mask[0], null);
                material.SetFloat(LayerIds.LayerHasMask[0], 0f);
                material.SetVector(LayerIds.DiffuseRemapScale[0], Vector4.one);
                material.SetVector(LayerIds.MaskMapRemapOffset[0], Vector4.zero);
                material.SetVector(LayerIds.MaskMapRemapScale[0], Vector4.one);
                for (int k = 1; k < 4; k++) material.SetTexture(LayerIds.Splat[k], null);
                material.DisableKeyword("_NORMALMAP");
                material.DisableKeyword("_MASKMAP");
                return;
            }
            for (int k = 0; k < 4; k++)
            {
                var layer = first + k < layers.Length ? layers[first + k] : null;
                if (layer == null)
                {
                    material.SetTexture(LayerIds.Splat[k], null);
                    material.SetFloat(LayerIds.LayerHasMask[k], 0f);
                    continue;
                }
                var tile = new Vector2(Mathf.Max(layer.tileSize.x, 1e-4f), Mathf.Max(layer.tileSize.y, 1e-4f));
                material.SetTexture(LayerIds.Splat[k], layer.diffuseTexture);
                material.SetTextureScale(LayerIds.Splat[k], new Vector2(size.x / tile.x, size.z / tile.y));
                material.SetTextureOffset(LayerIds.Splat[k], new Vector2(layer.tileOffset.x / tile.x, layer.tileOffset.y / tile.y));
                // A layer without a normal map gets the flat normal texture, linear data
                // (tests/projects/urpterrain masks; unmeasured for shaders whose _NormalN default
                // is not "bump"); one without a mask the black texture (unmeasured: URP's shaders
                // ignore a layer's mask unless _LayerHasMask flags it).
                normals |= layer.normalMapTexture != null;
                material.SetTexture(LayerIds.Normal[k], layer.normalMapTexture != null ? layer.normalMapTexture : Texture2D.normalTexture);
                material.SetFloat(LayerIds.NormalScale[k], layer.normalScale);
                bool hasMask = layer.maskMapTexture != null;
                material.SetTexture(LayerIds.Mask[k], hasMask ? layer.maskMapTexture : Texture2D.blackTexture);
                masks |= hasMask;
                material.SetFloat(LayerIds.LayerHasMask[k], hasMask ? 1f : 0f);
                material.SetFloat(LayerIds.DiffuseHasAlpha[k], DiffuseHasAlpha(layer) ? 1f : 0f);
                // Made linear here, and once more by the material where the shader flags it
                // [Gamma] (URP's terrain shaders flag _Metallic0-3), as in the Unity 6000.0.71f1
                // Editor (tests/projects/urpterrain).
                material.SetFloat(LayerIds.Metallic[k], LayerMetallic(layer));
                material.SetFloat(LayerIds.Smoothness[k], LayerSmoothness(layer));
                material.SetVector(LayerIds.DiffuseRemapOffset[k], layer.diffuseRemapMin);
                material.SetVector(LayerIds.DiffuseRemapScale[k], layer.diffuseRemapMax - layer.diffuseRemapMin);
                material.SetVector(LayerIds.MaskMapRemapOffset[k], layer.maskMapRemapMin);
                material.SetVector(LayerIds.MaskMapRemapScale[k], layer.maskMapRemapMax - layer.maskMapRemapMin);
            }
            // All layers count: URP's shaders turn opacity-as-density and height blending off past
            // four, whichever pass draws.
            material.SetFloat(LayerIds.NumLayersCount, layers.Length);
            if (normals) material.EnableKeyword("_NORMALMAP");
            else material.DisableKeyword("_NORMALMAP");
            if (masks) material.EnableKeyword("_MASKMAP");
            else material.DisableKeyword("_MASKMAP");
        }

        /// <summary>The splat materials' property IDs, per layer of a pass.</summary>
        static class LayerIds
        {
            internal static readonly int Control = Shader.PropertyToID("_Control"), NumLayersCount = Shader.PropertyToID("_NumLayersCount");
            internal static readonly int[] Splat = Of("_Splat"), Normal = Of("_Normal"), NormalScale = Of("_NormalScale"), Mask = Of("_Mask"),
                LayerHasMask = Of("_LayerHasMask"), DiffuseHasAlpha = Of("_DiffuseHasAlpha"), Metallic = Of("_Metallic"), Smoothness = Of("_Smoothness"),
                DiffuseRemapOffset = Of("_DiffuseRemapOffset"), DiffuseRemapScale = Of("_DiffuseRemapScale"),
                MaskMapRemapOffset = Of("_MaskMapRemapOffset"), MaskMapRemapScale = Of("_MaskMapRemapScale");

            static int[] Of(string name) => new[] { Shader.PropertyToID(name + "0"), Shader.PropertyToID(name + "1"), Shader.PropertyToID(name + "2"), Shader.PropertyToID(name + "3") };
        }

        /// <summary>A layer's metallic value as its splat material gets it: the slider read as a
        /// gamma value, made linear in a linear-space project, as in the Unity 6000.0.71f1
        /// Editor (tests/projects/defaultreflection).</summary>
        internal static float LayerMetallic(TerrainLayer layer) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.GammaToLinearSpace(layer.metallic) : layer.metallic;

        /// <summary>A layer's smoothness as its splat material gets it: the shaders multiply it by
        /// the diffuse texture's alpha, so with the smoothness taken from that alpha
        /// (<see cref="TerrainLayerSmoothnessSource.DiffuseAlphaChannel"/>, a new layer's) and a
        /// diffuse format with alpha (<see cref="GraphicsFormatUtility.HasAlphaChannel(TextureFormat)"/>) it
        /// is at least 1 (the alpha alone), else the layer's smoothness; as in the Unity
        /// 6000.0.71f1 Editor (tests/projects/defaultreflection, terrain, terrainlod layers5*).</summary>
        internal static float LayerSmoothness(TerrainLayer layer) => Mathf.Max(layer.smoothness, DiffuseHasAlpha(layer) ? 1f : 0f);

        /// <summary>Whether the layer's smoothness comes from its diffuse texture's alpha: the
        /// source says so and the texture's format has alpha.</summary>
        static bool DiffuseHasAlpha(TerrainLayer layer) =>
            layer.smoothnessSource == TerrainLayerSmoothnessSource.DiffuseAlphaChannel
            && layer.diffuseTexture is Texture2D diffuse && GraphicsFormatUtility.HasAlphaChannel(diffuse.format);

        /// <summary>The holes map the shaders clip with: red 0 over a hole, one texel per cell.</summary>
        Texture2D HolesTexture(TerrainData data)
        {
            int n = data.holesResolution;
            if (m_Holes == null || m_Holes.width != n)
            {
                if (m_Holes != null) DestroyImmediate(m_Holes);
                m_Holes = new Texture2D(n, n, TextureFormat.RGBA32, false, true)
                {
                    name = "Terrain holes",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point,
                };
            }
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    pixels[y * n + x] = data.Solid(x, y) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            m_Holes.SetPixels32(pixels);
            m_Holes.Apply();
            return m_Holes;
        }

        static void Warn(string message)
        {
            if (s_Warned.Add(message)) Debug.LogWarning("Unbound: " + message);
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            foreach (var part in m_Trees) part.Renderer.OnEngineDestroy();
            m_Trees.Clear();
            DestroyBillboards();
            foreach (var m in m_Passes) if (m != null) DestroyImmediate(m);
            m_Passes = Array.Empty<Material>();
            DestroyBaseMaterials();
            ReleaseBaseMaps();
            if (m_Holes != null) DestroyImmediate(m_Holes);
            DestroyDetails();
            DestroyPatches();
            foreach (var m in m_DetailMaterials) if (m != null) DestroyImmediate(m);
            if (m_DetailAtlas != null) DestroyImmediate(m_DetailAtlas);
        }
    }
}
