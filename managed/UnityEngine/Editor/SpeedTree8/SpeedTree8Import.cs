using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unbound.SpeedTree
{
    /// <summary>
    /// Imports a SpeedTree 8 (<c>.st</c>) file with its .meta's <see cref="SpeedTreeImporter"/>
    /// settings into the objects the Unity 6000.0.71f1 Editor makes (tests/projects/speedtree8):
    /// <list type="bullet">
    /// <item>the root, named after the file, with a LODGroup (SpeedTree fade mode when transitions are
    /// smooth) and a kinematic Rigidbody without gravity (<c>generateRigidbody</c>);</item>
    /// <item>a child <c>&lt;name&gt;_LOD&lt;i&gt;</c> per level with a MeshFilter, a MeshRenderer and a
    /// Tree whose wind asset holds the file's wind (<see cref="SpeedTreeWindConfig8"/>);</item>
    /// <item>a child <c>CollisionObject&lt;i&gt;</c> per collision object (<c>generateColliders</c>),
    /// a capsule along its two ends (or a sphere where they meet);</item>
    /// <item>a mesh per level and the Nature/SpeedTree8 materials (<see cref="MaterialFor"/>),
    /// unless <c>externalObjects</c> remaps them.</item>
    /// </list>
    /// File ids follow the .meta's <c>fileIDType</c> as a model's do: 2 hierarchy paths under
    /// <c>//RootNode/root</c>, 1 names (the root <c>//RootNode</c>), 0 class number counters; meshes
    /// are <c>4300000 + 2i</c> and the wind asset <c>22800000</c> whatever the kind. Each pass of the
    /// import calls OnPreprocessAsset, OnPreprocessSpeedTree and OnPostprocessSpeedTree. The import
    /// makes the colour maps of the file's materials import with alpha as transparency: when that
    /// changes a map, its .meta is written, it imports again and so does the tree (a second pass);
    /// then, as Unity's own SpeedTree postprocessor does for every imported .st (Unity's C#
    /// SpeedTreePostProcessor), the extra maps of its materials import without sRGB, which needs no
    /// other pass (tests/projects/speedtree8 profile fresh). (The Editor imports every new tree of a
    /// refresh before any of their maps change, so a tree whose maps another tree's import changed
    /// first imports twice there; here it imports once.)
    /// </summary>
    internal static class SpeedTree8Imports
    {
        /// <summary>The kind of the asset database's SpeedTree 8 assets.</summary>
        internal const string Kind = "speedtree";

        internal const long MeshFileId = 4300000, WindFileId = 22800000;
        const string RootPath = "//RootNode/root", RootKey = "//RootNode";

        internal static LoadedFile Import(AssetDatabaseRuntime.AssetInfo info)
        {
            if (!(AssetImporter.GetAtPath(info.Path) is SpeedTreeImporter importer))
                throw new InvalidOperationException($"{info.Path}: its .meta names no SpeedTreeImporter");
            var tree = Read(info.Path);
            var import = Pass(info, importer, tree);
            if (ColourMapsTransparent(import.ColourMapPaths()))
            {
                import.Discard();
                import = Pass(info, importer, tree);
            }
            importer.m_HasImported = true;
            var file = import.File();
            ExtraMapsLinear(import.MaterialsUsed());
            return file;
        }

        /// <summary>One pass of the import, with its own postprocessors.</summary>
        static Builder Pass(AssetDatabaseRuntime.AssetInfo info, SpeedTreeImporter importer, SpeedTree8File tree)
        {
            var session = new ImportSession(info.Path, new AssetImportContext(info.Path), importer);
            var import = new Builder(importer, tree, info.Path);
            using (ScriptedImports.Scope())
            using (AssetImporter.Importing(info.Path, importer))
            using (ObjectRegistry.AsAssets())
            {
                using (AssetDatabaseRuntime.ImportCallbacks(info.Guid))
                {
                    AssetPostprocessorHooks.Run(AssetPostprocessing.Hooks("OnPreprocessAsset"), session, Array.Empty<object>());
                    AssetPostprocessorHooks.Run(AssetPostprocessing.Hooks("OnPreprocessSpeedTree"), session, Array.Empty<object>());
                }
                import.Build();
                using (AssetDatabaseRuntime.ImportCallbacks(info.Guid))
                    AssetPostprocessorHooks.Run(AssetPostprocessing.Hooks("OnPostprocessSpeedTree", typeof(GameObject)), session, new object[] { import.Root });
            }
            return import;
        }

        /// <summary>Makes the colour maps import with alpha as transparency, writing the .meta of
        /// each that did not (a map of a read-only package keeps it for the session); whether any
        /// changed.</summary>
        static bool ColourMapsTransparent(IEnumerable<string> maps)
        {
            bool changed = false;
            foreach (var path in maps)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter texture) || texture.alphaIsTransparency) continue;
                texture.alphaIsTransparency = true;
                texture.SaveAndReimport();
                changed = true;
            }
            return changed;
        }

        /// <summary>Unity's SpeedTreePostProcessor (its C#, run for every imported .st): the
        /// <c>_ExtraTex</c> of each material holds no colour, so its texture imports without sRGB.</summary>
        static void ExtraMapsLinear(IEnumerable<Material> materials)
        {
            var reimported = new Dictionary<string, Texture>(StringComparer.Ordinal);
            foreach (var material in materials)
            {
                var texture = material != null && material.HasProperty(ExtraTex) ? material.GetTexture(ExtraTex) : null;
                if (texture == null) continue;
                string path = AssetDatabase.GetAssetPath(texture);
                if (!reimported.ContainsKey(path) && AssetImporter.GetAtPath(path) is TextureImporter importer && importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    importer.SaveAndReimport();
                    reimported[path] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                // A material refers to the asset, which is now the texture imported again.
                if (reimported.TryGetValue(path, out var fresh)) material.SetTexture(ExtraTex, fresh);
            }
        }

        const string ExtraTex = "_ExtraTex";

        static SpeedTree8File Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (!SpeedTree8File.HasSignature(bytes))
                throw new FormatException($"{path} is no SpeedTree 8 file (its first bytes are not \"{SpeedTree8File.Signature}\")");
            return SpeedTree8File.Read(bytes);
        }

        sealed class Builder
        {
            readonly SpeedTreeImporter m_Importer;
            readonly SpeedTree8File m_Tree;
            readonly string m_Path, m_Name, m_Folder;
            readonly float m_Scale;
            readonly SpeedTreeImporter.Level[] m_Levels;
            readonly Dictionary<AssetImporter.SourceAssetIdentifier, Object> m_External;
            readonly bool m_External8;
            readonly List<Mesh> m_Meshes = new List<Mesh>();
            /// <summary>The materials the import makes, by identifier, in the order it makes them.</summary>
            readonly List<(string Identifier, Material Material)> m_Materials = new List<(string, Material)>();
            readonly Dictionary<string, Material> m_ByIdentifier = new Dictionary<string, Material>(StringComparer.Ordinal);
            SpeedTreeWindAsset m_Wind;
            internal GameObject Root;

            internal Builder(SpeedTreeImporter importer, SpeedTree8File tree, string path)
            {
                m_Importer = importer;
                m_Tree = tree;
                m_Path = path;
                m_Name = Path.GetFileNameWithoutExtension(path);
                int slash = path.LastIndexOf('/');
                m_Folder = slash >= 0 ? path.Substring(0, slash + 1) : "";
                m_Scale = importer.ImportScale;
                m_Levels = importer.ImportLevels();
                m_External = importer.GetExternalObjectMap();
                m_External8 = importer.materialLocation == SpeedTreeImporter.MaterialLocation.External;
            }

            internal void Build()
            {
                int lodCount = m_Tree.Lods.Length;
                if (m_Levels.Length != lodCount)
                    throw new NotSupportedException($"{m_Path}: its .meta has {m_Levels.Length} level settings for the file's {lodCount} levels; the Editor's settings for that are not measured");
                m_Wind = new SpeedTreeWindAsset(SpeedTreeWindConfig8.From(m_Tree.WindConfig, m_Scale)) { name = "" };
                Root = new GameObject(m_Name);
                var group = Root.AddComponent<LODGroup>();
                if (m_Importer.generateRigidbody)
                {
                    var body = Root.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.isKinematic = true;
                }
                var lods = new LOD[lodCount];
                for (int i = 0; i < lodCount; i++) lods[i] = new LOD(m_Levels[i].Height, new Renderer[] { CreateLevel(i) });
                if (m_Importer.generateColliders) CreateColliders();
                // Without smooth transitions the group keeps its defaults (no fade mode, no animated
                // cross-fading). With them, cross-fading that is not animated fades the last level
                // over the fade-out width and, before a billboard level, the last mesh level over the
                // billboard transition width (tests/projects/speedtree8 Card_Units0, Field_Embedded).
                bool smooth = m_Importer.enableSmoothLODTransition;
                if (smooth && lodCount > 0 && !m_Importer.animateCrossFading)
                {
                    lods[lodCount - 1].fadeTransitionWidth = m_Importer.fadeOutWidth;
                    if (m_Tree.LastLodIsBillboard && lodCount > 2)
                        lods[lodCount - 2].fadeTransitionWidth = m_Importer.billboardTransitionCrossFadeWidth;
                }
                group.SetLODs(lods);
                if (smooth)
                {
                    group.fadeMode = LODFadeMode.SpeedTree;
                    group.animateCrossFading = m_Importer.animateCrossFading;
                    group.lastLODBillboard = m_Tree.LastLodIsBillboard;
                }
                group.RecalculateBounds();
            }

            bool IsBillboard(int lod) => m_Tree.LastLodIsBillboard && lod == m_Tree.Lods.Length - 1;

            MeshRenderer CreateLevel(int index)
            {
                var lod = m_Tree.Lods[index];
                var level = m_Levels[index];
                var go = new GameObject(m_Name + "_LOD" + index.ToString(CultureInfo.InvariantCulture));
                // Parented as Transform.parent does (the Editor's levels have the local rotation
                // that gives under an unrotated root).
                go.transform.parent = Root.transform;
                // The draws of one material make one submesh, in the order the materials first draw.
                var order = new List<uint>();
                foreach (var draw in lod.DrawCalls)
                {
                    if (draw.MaterialIndex >= m_Tree.Materials.Length)
                        throw new FormatException($"{m_Path}: a draw of level {index} uses material {draw.MaterialIndex} of {m_Tree.Materials.Length}");
                    if (!order.Contains(draw.MaterialIndex)) order.Add(draw.MaterialIndex);
                }
                var mesh = CreateMesh(lod, index, order);
                var materials = new Material[order.Count];
                for (int s = 0; s < order.Count; s++) materials[s] = MaterialFor(m_Tree.Materials[order[s]], index, level);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = level.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = level.ReceiveShadows;
                renderer.lightProbeUsage = level.UseLightProbes ? LightProbeUsage.BlendProbes : LightProbeUsage.Off;
                renderer.reflectionProbeUsage = level.ReflectionProbes;
                renderer.motionVectorGenerationMode = (MotionVectorGenerationMode)m_Importer.MotionVectorMode;
                go.AddComponent<UnityEngine.Tree>().windAsset = m_Wind;
                return renderer;
            }

            Mesh CreateMesh(SpeedTree8File.Lod lod, int index, List<uint> order)
            {
                bool billboard = IsBillboard(index);
                int count = lod.Vertices.Length;
                // Each vertex takes the geometry of the draws that use it.
                var geometry = new SpeedTree8File.GeometryType[count];
                foreach (var draw in lod.DrawCalls)
                    for (uint k = 0; k < draw.IndexCount; k++)
                    {
                        uint vertex = lod.Indices[draw.IndexStart + k];
                        if (vertex >= count) throw new FormatException($"{m_Path}: level {index} indexes vertex {vertex} of {count}");
                        geometry[vertex] = draw.GeometryType;
                    }
                var positions = new Vector3[count];
                var normals = new Vector3[count];
                var tangents = new Vector4[count];
                var colors = new Color32[count];
                List<Vector3> billboardUv = billboard ? new List<Vector3>(count) : null;
                List<Vector4> uv0 = null, uv1 = null, uv2 = null, uv3 = null;
                if (!billboard)
                {
                    uv0 = new List<Vector4>(count);
                    uv1 = new List<Vector4>(count);
                    uv2 = new List<Vector4>(count);
                    uv3 = new List<Vector4>(count);
                }
                float s = m_Scale;
                for (int i = 0; i < count; i++)
                {
                    var v = lod.Vertices[i];
                    // A camera-facing leaf's corners are mirrored in x.
                    float facing = geometry[i] == SpeedTree8File.GeometryType.FacingLeaf ? -1f : 1f;
                    float ax = v.Anchor.X * s, ay = v.Anchor.Y * s, az = v.Anchor.Z * s;
                    positions[i] = new Vector3(ax + facing * v.Offset.X * s, ay + v.Offset.Y * s, az + v.Offset.Z * s);
                    var normal = new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z);
                    normals[i] = normal;
                    var tangent = new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z);
                    float side = Vector3.Dot(Vector3.Cross(normal, tangent), new Vector3(v.Binormal.X, v.Binormal.Y, v.Binormal.Z));
                    tangents[i] = new Vector4(v.Tangent.X, v.Tangent.Y, v.Tangent.Z, side < 0f ? -1f : 1f);
                    // The vertex color counts for nothing; a billboard level is opaque.
                    byte ao = Unit(v.AmbientOcclusion);
                    colors[i] = new Color32(ao, ao, ao, billboard ? (byte)255 : Unit(v.BlendWeight));
                    if (billboard)
                    {
                        // The third component marks the top-down quad (the shader's seam
                        // cross-fade): its faces point up. (The sample's top-down vertices are also
                        // the only ones whose binormal points along x; it cannot tell the rules apart.)
                        billboardUv.Add(new Vector3(v.U, v.V, v.Normal.Y > 0.5f ? 1f : 0f));
                        continue;
                    }
                    uv0.Add(new Vector4(v.U, v.V, v.BranchWindX, v.BranchWindY));
                    uv1.Add(new Vector4(v.LightmapU, v.LightmapV, ax, ay));
                    // The level's position (the level of detail transition's target) and the anchor's z.
                    uv2.Add(new Vector4(ax + facing * v.LodOffset.X * s, ay + v.LodOffset.Y * s, az + v.LodOffset.Z * s, az));
                    uv3.Add(new Vector4(v.LeafWind.X, v.LeafWind.Y, v.LeafWind.Z, (float)geometry[i]));
                }
                var mesh = new Mesh
                {
                    name = m_Name + "_LOD" + index.ToString(CultureInfo.InvariantCulture),
                    indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                    vertices = positions,
                    normals = normals,
                    tangents = tangents,
                    colors32 = colors,
                };
                if (billboard) mesh.SetUVs(0, billboardUv);
                else
                {
                    mesh.SetUVs(0, uv0);
                    mesh.SetUVs(1, uv1);
                    mesh.SetUVs(2, uv2);
                    mesh.SetUVs(3, uv3);
                }
                mesh.subMeshCount = order.Count;
                var indices = new List<int>();
                for (int sub = 0; sub < order.Count; sub++)
                {
                    indices.Clear();
                    foreach (var draw in lod.DrawCalls)
                        if (draw.MaterialIndex == order[sub])
                            for (uint k = 0; k < draw.IndexCount; k++) indices.Add((int)lod.Indices[draw.IndexStart + k]);
                    mesh.SetIndices(indices, MeshTopology.Triangles, sub, calculateBounds: true);
                }
                mesh.UpdateBounds();
                mesh.RecalculateUVDistributionMetrics();
                m_Meshes.Add(mesh);
                return mesh;
            }

            /// <summary>A unit value as a byte, cut (not rounded): 0.897 is 228.</summary>
            static byte Unit(float value) => (byte)(Mathf.Clamp01(value) * 255f);

            /// <summary>
            /// The material level <paramref name="lod"/> draws a file material with. Its identifier is
            /// the file's name without a trailing <c>_Mat</c>, with <c>_LOD&lt;i&gt;</c> for each level
            /// when the materials are External or for a level that overrides the settings; the
            /// identifier's <c>externalObjects</c> entry replaces it, else one Nature/SpeedTree8
            /// material per identifier is made: the maps' textures from the tree's folder (by the
            /// paths the file gives); normal mapping, the extra map and subsurface (on two-sided or
            /// billboard materials) where the file uses the map and the level enables the effect; hue
            /// variation; culling and doubleSidedGI from two-sidedness; the billboard switch; the
            /// level's wind quality; instancing. External materials the .meta does not remap are not
            /// made (the Editor writes them into <see cref="SpeedTreeImporter.materialFolderPath"/>).
            /// </summary>
            Material MaterialFor(SpeedTree8File.Material source, int lod, SpeedTreeImporter.Level level)
            {
                string name = source.Name.EndsWith("_Mat", StringComparison.Ordinal) ? source.Name.Substring(0, source.Name.Length - 4) : source.Name;
                string identifier = m_External8 || level.Overrides ? name + "_LOD" + lod.ToString(CultureInfo.InvariantCulture) : name;
                // A remap whose target is gone leaves the slot empty (tests/projects/speedtree8 Card_Dangling).
                if (m_External.TryGetValue(new AssetImporter.SourceAssetIdentifier(typeof(Material), identifier), out var remapped))
                    return remapped as Material;
                if (m_ByIdentifier.TryGetValue(identifier, out var made)) return made;
                if (m_External8)
                    throw new NotSupportedException($"{m_Path}: its External material {identifier} is not in its .meta's externalObjects; Unbound does not write the Editor's materials into {m_Importer.materialFolderPath}");
                var shader = MaterialShader();
                var material = new Material(shader) { name = identifier };
                material.SetColor("_Color", m_Importer.mainColor);
                var color = Map(source, 0, out _);
                if (color != null) material.SetTexture("_MainTex", color);
                var normal = Map(source, 1, out bool normalUsed);
                if (normal != null) material.SetTexture("_BumpMap", normal);
                Toggle(material, "_NormalMapKwToggle", "EFFECT_BUMP", normalUsed && level.EnableBump);
                var extra = Map(source, 2, out bool extraUsed);
                if (extra != null)
                {
                    material.SetTexture("_ExtraTex", extra);
                    // The shader multiplies the map by these: 1 keeps its values.
                    material.SetFloat("_Glossiness", 1f);
                    material.SetFloat("_Metallic", 1f);
                }
                else if (extraUsed)
                    Stubs.Ignored("SpeedTree 8 extra maps without a texture", "keep the shader's smoothness and metallic: what the Editor sets for them is unmeasured");
                if (extraUsed) material.EnableKeyword("EFFECT_EXTRA_TEX");
                if (source.TwoSided || source.Billboard)
                {
                    var subsurface = Map(source, 3, out bool subsurfaceUsed);
                    if (subsurface != null) material.SetTexture("_SubsurfaceTex", subsurface);
                    Toggle(material, "_SubsurfaceKwToggle", "EFFECT_SUBSURFACE", subsurfaceUsed && level.EnableSubsurface);
                }
                Toggle(material, "_HueVariationKwToggle", "EFFECT_HUE_VARIATION", level.EnableHue);
                material.SetColor("_HueVariationColor", m_Importer.hueVariation);
                Toggle(material, "_BillboardKwToggle", "EFFECT_BILLBOARD", source.Billboard);
                // The cull mode: 0 draws both sides.
                material.SetFloat("_TwoSided", source.TwoSided ? 0f : 2f);
                int quality = Mathf.Clamp(level.WindQuality, 0, SpeedTreeImporter.windQualityNames.Length - 1);
                material.SetFloat("_WindQuality", quality);
                // One wind quality keyword: the level's (not the shader's default None).
                for (int q = 0; q < SpeedTreeImporter.windQualityNames.Length; q++)
                {
                    string keyword = "_WINDQUALITY_" + SpeedTreeImporter.windQualityNames[q].ToUpperInvariant();
                    if (q == quality) material.EnableKeyword(keyword);
                    else material.DisableKeyword(keyword);
                }
                material.enableInstancing = true;
                material.doubleSidedGI = source.TwoSided;
                m_ByIdentifier[identifier] = material;
                m_Materials.Add((identifier, material));
                return material;
            }

            /// <summary>Nature/SpeedTree8 (the measured built-in pipeline's); under a render pipeline
            /// its SpeedTree 8 shader, with the same settings (unmeasured, reported).</summary>
            Shader MaterialShader()
            {
                var pipeline = GraphicsSettings.currentRenderPipeline;
                if (pipeline != null && pipeline.defaultSpeedTree8Shader != null)
                {
                    Stubs.Ignored("SpeedTree 8 materials under a render pipeline", "take the pipeline's SpeedTree 8 shader with the built-in shader's settings: the Editor's materials there are unmeasured");
                    return pipeline.defaultSpeedTree8Shader;
                }
                return Shader.Find("Nature/SpeedTree8") ?? throw new InvalidOperationException("the Nature/SpeedTree8 shader is missing");
            }

            static void Toggle(Material material, string property, string keyword, bool on)
            {
                material.SetFloat(property, on ? 1f : 0f);
                if (on) material.EnableKeyword(keyword);
            }

            /// <summary>The texture of map <paramref name="index"/>: the file's path next to the tree.
            /// A used map whose texture is not there is reported (the Editor's search elsewhere is
            /// unmeasured).</summary>
            Texture2D Map(SpeedTree8File.Material source, int index, out bool used)
            {
                used = false;
                if (index >= source.Maps.Length) return null;
                var map = source.Maps[index];
                used = map.Used;
                if (!used || string.IsNullOrEmpty(map.Path)) return null;
                string path = m_Folder + map.Path;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                    Debug.LogWarning($"Unbound: SpeedTree 8 tree {m_Path} uses {path}, which is not a texture of the project; the material has none there");
                return texture;
            }

            void CreateColliders()
            {
                for (int i = 0; i < m_Tree.Collisions.Length; i++)
                {
                    var collision = m_Tree.Collisions[i];
                    var go = new GameObject("CollisionObject" + i.ToString(CultureInfo.InvariantCulture));
                    go.transform.parent = Root.transform;
                    var a = new Vector3(collision.Position.X, collision.Position.Y, collision.Position.Z) * m_Scale;
                    var b = new Vector3(collision.Position2.X, collision.Position2.Y, collision.Position2.Z) * m_Scale;
                    float radius = collision.Radius * m_Scale;
                    go.transform.localPosition = (a + b) * 0.5f;
                    var along = b - a;
                    // Only ends that meet make a sphere: 0.0003 apart is a capsule (tests/projects/speedtree8
                    // Card_Spheres).
                    if (along.x == 0f && along.y == 0f && along.z == 0f)
                    {
                        go.AddComponent<SphereCollider>().radius = radius;
                        continue;
                    }
                    // A capsule along x from one end to the other.
                    go.transform.localRotation = Quaternion.FromToRotation(Vector3.right, along.normalized);
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.direction = 0;
                    capsule.radius = radius;
                    capsule.height = along.magnitude + 2f * radius;
                }
            }

            /// <summary>The colour maps of the file's materials: a used map 0 next to the tree.</summary>
            internal IEnumerable<string> ColourMapPaths()
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var material in m_Tree.Materials)
                {
                    if (material.Maps.Length == 0 || !material.Maps[0].Used || string.IsNullOrEmpty(material.Maps[0].Path)) continue;
                    string path = m_Folder + material.Maps[0].Path;
                    if (seen.Add(path)) yield return path;
                }
            }

            /// <summary>The materials the import made and those its remaps name.</summary>
            internal IEnumerable<Material> MaterialsUsed()
            {
                foreach (var target in m_External.Values)
                    if (target is Material remapped) yield return remapped;
                foreach (var (_, material) in m_Materials) yield return material;
            }

            /// <summary>Destroys what a pass made (the import makes them again).</summary>
            internal void Discard()
            {
                Object.DestroyImmediate(Root, true);
                foreach (var mesh in m_Meshes) Object.DestroyImmediate(mesh, true);
                foreach (var (_, material) in m_Materials) Object.DestroyImmediate(material, true);
                Object.DestroyImmediate(m_Wind, true);
            }

            /// <summary>The imported objects by file id, flagged as the Editor flags them.</summary>
            internal LoadedFile File()
            {
                var file = new LoadedFile { MainObject = Root, UnknownFileIds = true };
                ScriptedImports.MarkImported(Root);
                foreach (var mesh in m_Meshes) ScriptedImports.MarkImported(mesh);
                foreach (var (_, material) in m_Materials) ScriptedImports.MarkImported(material);
                // The wind asset is hidden, not read-only.
                m_Wind.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                int kind = m_Importer.FileIdType;
                if (kind == 0)
                    Stubs.Ignored("SpeedTree 8 fileIDType 0", "file ids (class number counters, in creation order) are not measured for SpeedTree 8 trees: references to the tree's objects may not resolve");
                var counters = new Dictionary<int, int>();
                long Legacy(Object obj)
                {
                    int classId = EngineTypes.EngineClassIdOf(obj.GetType());
                    counters.TryGetValue(classId, out int n);
                    counters[classId] = n + 1;
                    return classId * 100000L + 2L * n;
                }
                long Hashed(string type, string key) =>
                    (long)ScriptedImports.XxHash64(System.Text.Encoding.UTF8.GetBytes("Type:" + type + "->" + key + "0"));
                void Walk(Transform t, string path, string key)
                {
                    var go = t.gameObject;
                    file.Objects[kind == 0 ? Legacy(go) : Hashed("GameObject", kind == 2 ? path : key)] = go;
                    foreach (var component in go.GetComponents<Component>())
                    {
                        string type = ObjectNames.GetClassName(component);
                        file.Objects[kind == 0 ? Legacy(component) : Hashed(type, kind == 2 ? path + "/" + type : key)] = component;
                    }
                    for (int c = 0; c < t.childCount; c++)
                    {
                        var child = t.GetChild(c);
                        Walk(child, path + "/" + child.gameObject.m_Name, child.gameObject.m_Name);
                    }
                }
                Walk(Root.transform, RootPath, RootKey);
                for (int i = 0; i < m_Meshes.Count; i++) file.Objects[MeshFileId + 2 * i] = m_Meshes[i];
                foreach (var (identifier, material) in m_Materials)
                    file.Objects[kind == 0 ? Legacy(material) : Hashed("Material", identifier)] = material;
                file.Objects[WindFileId] = m_Wind;
                return file;
            }
        }
    }
}
