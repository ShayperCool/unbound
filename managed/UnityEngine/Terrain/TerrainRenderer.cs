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
                if (realtimeLightmapIndex >= 0 && realtimeLightmapIndex < 0xFFFE)
                    Unbound.Runtime.Stubs.Ignored("Terrain.realtimeLightmapIndex",
                        "realtime GI terrain charts and dynamic lightmap textures are not supported yet; only baked lightmaps are drawn");
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
            foreach (var part in m_Trees)
            {
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
        }

        void BuildTrees(Terrain terrain, TerrainData data, Vector3 origin)
        {
            foreach (var part in m_Trees) part.Renderer.OnEngineDestroy();
            m_Trees.Clear();
            m_TreesData = data;
            m_TreesVersion = data.m_TreesVersion;
            m_TreesOrigin = origin;
            var prototypes = data.treePrototypes;
            var size = data.size;
            foreach (var prototype in prototypes)
                if (prototype?.prefab != null && NamesBillboardShader(prototype.prefab))
                    Warn($"tree prototype '{prototype.prefab.name}' has materials made for billboards: Unbound draws it as its meshes at every distance, without billboards past treeBillboardDistance.");
            foreach (var tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length) continue;
                var prefab = prototypes[tree.prototypeIndex]?.prefab;
                if (prefab == null) continue;
                var root = prefab.transform;
                var world = origin + Vector3.Scale(tree.position, size);
                var placement = Matrix4x4.TRS(world, Quaternion.AngleAxis(tree.rotation * Mathf.Rad2Deg, Vector3.up), new Vector3(tree.widthScale, tree.heightScale, tree.widthScale))
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

        void UpdateMaterials(Terrain terrain, TerrainData data)
        {
            var template = terrain.materialTemplate;
            if (template == null) template = BuiltinObjects.DefaultTerrainMaterial;
            if (template == null) return;
            if (template == m_Template && m_SplatData == data && m_SplatVersion == data.m_SplatVersion + data.m_HeightsVersion * 7919) return;
            m_Template = template;
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
        }

        /// <summary>A terrain with holes cuts them out of every material it draws with.</summary>
        void SetHoles(Material material, TerrainData data)
        {
            if (!data.HasHoles) return;
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetTexture("_TerrainHolesTexture", HolesTexture(data));
        }

        /// <summary>A pass's four layers: the control (splat) map, each layer's textures, tiling
        /// (the terrain's size over the tile size, the offset over it) and surface values.</summary>
        internal static void SetLayers(Material material, TerrainData data, TerrainLayer[] layers, int first, Texture2D control)
        {
            var size = data.size;
            material.SetTexture("_Control", control);
            bool normals = false;
            if (layers.Length == 0 && first == 0)
            {
                // No layers: the built-in grey checker (two by two squares) in 2 m tiles, as the
                // Editor shows such a terrain whatever its size (measured).
                material.SetTexture("_Control", DefaultTextures.Red);
                material.SetTexture("_Splat0", BuiltinObjects.DefaultCheckerGray);
                material.SetTextureScale("_Splat0", new Vector2(size.x / 2f, size.z / 2f));
                material.SetTextureOffset("_Splat0", Vector2.zero);
                material.SetFloat("_Metallic0", 0f);
                material.SetFloat("_Smoothness0", 0f);
                material.SetFloat("_NormalScale0", 1f);
                material.SetTexture("_Mask0", null);
                material.SetVector("_DiffuseRemapScale0", Vector4.one);
                material.SetVector("_MaskMapRemapOffset0", Vector4.zero);
                material.SetVector("_MaskMapRemapScale0", Vector4.one);
                for (int k = 1; k < 4; k++) material.SetTexture("_Splat" + k, null);
                material.DisableKeyword("_NORMALMAP");
                return;
            }
            for (int k = 0; k < 4; k++)
            {
                var layer = first + k < layers.Length ? layers[first + k] : null;
                string n = k.ToString();
                if (layer == null)
                {
                    material.SetTexture("_Splat" + n, null);
                    continue;
                }
                var tile = new Vector2(Mathf.Max(layer.tileSize.x, 1e-4f), Mathf.Max(layer.tileSize.y, 1e-4f));
                material.SetTexture("_Splat" + n, layer.diffuseTexture);
                material.SetTextureScale("_Splat" + n, new Vector2(size.x / tile.x, size.z / tile.y));
                material.SetTextureOffset("_Splat" + n, new Vector2(layer.tileOffset.x / tile.x, layer.tileOffset.y / tile.y));
                if (layer.normalMapTexture != null)
                {
                    normals = true;
                    material.SetTexture("_Normal" + n, layer.normalMapTexture);
                }
                material.SetFloat("_NormalScale" + n, layer.normalScale);
                material.SetTexture("_Mask" + n, layer.maskMapTexture);
                // Already in the working color space: the material passes it on as it is, whatever
                // the shader's [Gamma] flags (URP's terrain shaders flag _Metallic0-3; unmeasured
                // under URP).
                int metallic = Shader.PropertyToID("_Metallic" + n);
                material.SetFloat(metallic, LayerMetallic(layer));
                material.m_RawBindings.Add(metallic);
                material.SetFloat("_Smoothness" + n, LayerSmoothness(layer));
                material.SetVector("_DiffuseRemapScale" + n, layer.diffuseRemapMax - layer.diffuseRemapMin);
                material.SetVector("_MaskMapRemapOffset" + n, layer.maskMapRemapMin);
                material.SetVector("_MaskMapRemapScale" + n, layer.maskMapRemapMax - layer.maskMapRemapMin);
            }
            if (normals) material.EnableKeyword("_NORMALMAP");
            else material.DisableKeyword("_NORMALMAP");
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
        internal static float LayerSmoothness(TerrainLayer layer)
        {
            bool fromAlpha = layer.smoothnessSource == TerrainLayerSmoothnessSource.DiffuseAlphaChannel
                && layer.diffuseTexture is Texture2D diffuse && GraphicsFormatUtility.HasAlphaChannel(diffuse.format);
            return Mathf.Max(layer.smoothness, fromAlpha ? 1f : 0f);
        }

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
