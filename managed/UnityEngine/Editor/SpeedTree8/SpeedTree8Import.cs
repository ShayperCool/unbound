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
    /// are <c>4300000 + 2i</c> and the wind asset <c>22800000</c> whatever the kind. The import calls
    /// OnPreprocessAsset, OnPreprocessSpeedTree and OnPostprocessSpeedTree once; the second pass the
    /// Editor makes when the import changes its textures' settings (a tree imported without .meta)
    /// and the texture .meta writes are not made.
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
            var ctx = new AssetImportContext(info.Path);
            var session = new ImportSession(info.Path, ctx, importer);
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
            importer.m_HasImported = true;
            return import.File();
        }

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
                m_Scale = importer.scaleFactor;
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
                group.SetLODs(lods);
                if (m_Importer.enableSmoothLODTransition)
                {
                    group.fadeMode = LODFadeMode.SpeedTree;
                    group.animateCrossFading = m_Importer.animateCrossFading;
                    group.lastLODBillboard = m_Tree.LastLodIsBillboard;
                    // The levels' fade widths stay 0 (measured with animated cross-fading; the
                    // SpeedTree 9 importer keeps 0 without it too).
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
                if (m_External.TryGetValue(new AssetImporter.SourceAssetIdentifier(typeof(Material), identifier), out var remapped))
                    return remapped as Material;
                if (m_ByIdentifier.TryGetValue(identifier, out var made)) return made;
                if (m_External8)
                    throw new NotSupportedException($"{m_Path}: its External material {identifier} is not in its .meta's externalObjects; Unbound does not write the Editor's materials into {m_Importer.materialFolderPath}");
                var shader = Shader.Find("Nature/SpeedTree8");
                if (shader == null) throw new InvalidOperationException("the Nature/SpeedTree8 shader is missing");
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
                {
                    // A map without a texture gives its color (the SpeedTree 9 importer's rule; unmeasured).
                    var c = source.Maps[2];
                    material.SetFloat("_Glossiness", c.R);
                    material.SetFloat("_Metallic", c.G);
                }
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
                material.EnableKeyword("_WINDQUALITY_" + SpeedTreeImporter.windQualityNames[quality].ToUpperInvariant());
                material.enableInstancing = true;
                material.doubleSidedGI = source.TwoSided;
                m_ByIdentifier[identifier] = material;
                m_Materials.Add((identifier, material));
                return material;
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
                    // Ends 0.03 apart make a capsule (measured); only ends that meet make a sphere.
                    if (along == Vector3.zero)
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
