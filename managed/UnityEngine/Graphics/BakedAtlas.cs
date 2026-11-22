using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// The first part of Unbound's own baker of baked lightmaps (Q677): where a bake in Edit mode
    /// (Lightmapping.Bake) puts each renderer of a scene whose Lighting Settings have Baked Global
    /// Illumination on, laid out natively as the Unity 6000.0.71f1 Editor lays out its baked
    /// lightmaps (tests/projects/bakedatlas): which lightmap, its lightmap scale and offset, and
    /// each lightmap's size. The bake does not make the lightmaps yet, so it gives renderers
    /// nothing (they keep what they had; said once per scene by the bake) and writes the layout
    /// to <c>Library/Unbound/BakedGI/&lt;scene GUID&gt;/atlas.json</c> for the next parts.
    /// <para>The renderers laid out are those the Editor lightmaps: enabled MeshRenderers of
    /// active GameObjects with Contribute Global Illumination, Receive Global Illumination
    /// Lightmaps and a Scale In Lightmap above 0, made from the scene file, in hierarchy order. A renderer that cannot be laid out (no mesh, a
    /// submesh that is not triangles) is left out and named; Lightmap Parameters that cannot be
    /// read, a built-in one other than Default-Medium or one with Limit Lightmap Count leave the
    /// scene without a layout (said once).</para>
    /// </summary>
    internal static class BakedAtlas
    {
        const uint ContributeGI = 1;
        const long DefaultParametersFileId = 15204;
        const string BuiltinExtraGuid = "0000000000000000f000000000000000";

        static readonly HashSet<string> s_Warned = new HashSet<string>();

        static void Warn(string key, string message)
        {
            if (s_Warned.Add(key)) Debug.LogWarning("Unbound: " + message);
        }

        /// <summary>The fields of Lightmap Parameters that keep baked atlases apart.</summary>
        struct Parameters
        {
            internal float BackFaceTolerance, Pushoff;
            internal int AntiAliasingSamples, BakedLightmapTag, MaxLightmapCount;

            internal static Parameters DefaultMedium => new Parameters
            {
                BackFaceTolerance = 0.9f, AntiAliasingSamples = 8, Pushoff = 0.0001f, BakedLightmapTag = -1, MaxLightmapCount = 0,
            };
        }

        static readonly Dictionary<(string, long), Parameters?> s_Parameters = new Dictionary<(string, long), Parameters?>();

        /// <summary>A renderer's Lightmap Parameters (its own, else the scene's, else the built-in
        /// Default-Medium), or null with why they cannot be taken.</summary>
        static Parameters? ParametersOf(ObjectRef reference, LightingSettings settings, out string why)
        {
            why = null;
            if (reference.IsNull) reference = settings.DefaultLightmapParameters;
            if (reference.IsNull || (reference.Guid == BuiltinExtraGuid && reference.FileId == DefaultParametersFileId))
                return Parameters.DefaultMedium;
            if (reference.Guid == BuiltinExtraGuid)
            {
                why = "built-in Lightmap Parameters other than Default-Medium (not measured)";
                return null;
            }
            var key = (reference.Guid, reference.FileId);
            if (!s_Parameters.TryGetValue(key, out var read))
            {
                try
                {
                    foreach (var doc in Native.LoadDocuments(reference.Guid))
                    {
                        if (doc.ClassId != 1113 || doc.FileId != reference.FileId) continue;
                        var b = doc.Body;
                        read = new Parameters
                        {
                            BackFaceTolerance = b.Float("backFaceTolerance", 0.9f),
                            AntiAliasingSamples = b.Int("antiAliasingSamples", 8),
                            Pushoff = b.Float("pushoff", 0.0001f),
                            BakedLightmapTag = b.Int("bakedLightmapTag", -1),
                            MaxLightmapCount = b.Int("limitLightmapCount", 0) != 0 ? b.Int("maxLightmapCount", 1) : 0,
                        };
                    }
                }
                catch (Exception) { read = null; }
                s_Parameters[key] = read;
            }
            if (read == null) why = "Lightmap Parameters that cannot be read";
            else if (read.Value.MaxLightmapCount != 0) { why = "Lightmap Parameters with Limit Lightmap Count (not supported)"; return null; }
            return read;
        }

        /// <summary>Each renderer of the scene's LOD Groups: the most detailed level it is in.</summary>
        static Dictionary<Renderer, int> LodLevels(LoadedFile file)
        {
            var levels = new Dictionary<Renderer, int>();
            foreach (var obj in file.Objects.Values)
            {
                if (!(obj is LODGroup group) || group == null) continue;
                var lods = group.GetLODs();
                for (int level = 0; level < lods.Length; level++)
                    foreach (var r in lods[level].renderers)
                        if (r != null && (!levels.TryGetValue(r, out var known) || level < known)) levels[r] = level;
            }
            return levels;
        }

        /// <summary>The scene's MeshRenderers made from its file in hierarchy order (its roots in
        /// order, each depth first): the order the Editor numbers them in, which keeps renderers of
        /// one Morton code and size in it (tests/projects/bakedatlas Ties).</summary>
        static List<(MeshRenderer Renderer, long FileId)> InHierarchyOrder(LoadedFile file)
        {
            var made = new Dictionary<Object, long>();
            foreach (var pair in file.Objects)
                if (pair.Value is MeshRenderer) made[pair.Value] = pair.Key;
            var ordered = new List<(MeshRenderer, long)>();
            void Visit(Transform t)
            {
                if (t.GetComponent<MeshRenderer>() is MeshRenderer r && made.TryGetValue(r, out var id)) ordered.Add((r, id));
                for (int i = 0; i < t.childCount; i++) Visit(t.GetChild(i));
            }
            foreach (var root in (IEnumerable<GameObject>)file.SceneRoots ?? file.Roots)
                if (root != null) Visit(root.transform);
            return ordered;
        }

        static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Lays out the baked lightmaps of the scene made from <paramref name="file"/> (Lightmapping.Bake in Edit
        /// mode) and writes the layout; nothing when its settings have baked global illumination off.</summary>
        internal static void Lay(LoadedFile file, LightingSettings settings)
        {
            settings ??= UnityEditor.Lightmapping.lightingSettingsDefaults;
            if (!settings.bakedGI) return;
            string path = file.Path;
            string guid = string.IsNullOrEmpty(file.Guid) ? UnityEditor.AssetDatabase.AssetPathToGUID(path) : file.Guid;
            if (string.IsNullOrEmpty(guid))
            {
                Warn("guid:" + path, $"{path}: a scene without a GUID; Unbound does not lay out its baked lightmaps (Lightmapping.Bake).");
                return;
            }
            var levels = LodLevels(file);
            var renderers = new List<object>();
            var placed = new List<(MeshRenderer renderer, long fileId)>();
            using (Mesh.EngineReads())
            {
                foreach (var (renderer, fileId) in InHierarchyOrder(file))
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    // Scale In Lightmap 0: "the object is not lightmapped" (Manual 6000.0, Mesh Renderer).
                    if ((renderer.gameObject.m_StaticEditorFlags & ContributeGI) == 0 || renderer.receiveGI != ReceiveGI.Lightmaps
                        || !(renderer.scaleInLightmap > 0f)) continue;
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    string left = null;
                    if (mesh == null) left = "it has no mesh";
                    else
                        for (int sub = 0; sub < mesh.subMeshCount && left == null; sub++)
                            if (mesh.GetTopology(sub) != MeshTopology.Triangles) left = $"a submesh of {mesh.GetTopology(sub)} (not supported)";
                    if (left != null)
                    {
                        Warn("left:" + path + fileId, $"{path}: '{renderer.name}' is left out of Unbound's layout of the baked lightmaps (Lightmapping.Bake): {left}.");
                        continue;
                    }
                    if (!(ParametersOf(renderer.m_LightmapParameters, settings, out var why) is Parameters parameters))
                    {
                        Warn("parameters:" + path, $"{path}: '{renderer.name}' has {why}; Unbound does not lay out its baked lightmaps (Lightmapping.Bake).");
                        return;
                    }
                    var vertices = mesh.vertices;
                    var uvSource = mesh.uv2;
                    if (uvSource.Length != vertices.Length) uvSource = mesh.uv;
                    var positions = new float[3 * vertices.Length];
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        positions[3 * v] = vertices[v].x; positions[3 * v + 1] = vertices[v].y; positions[3 * v + 2] = vertices[v].z;
                    }
                    var uvs = new float[2 * uvSource.Length];
                    for (int v = 0; v < uvSource.Length; v++)
                    {
                        uvs[2 * v] = uvSource[v].x; uvs[2 * v + 1] = uvSource[v].y;
                    }
                    var m = renderer.transform.localToWorldMatrix;
                    var matrix = new float[16];
                    for (int c = 0; c < 4; c++)
                        for (int r = 0; r < 4; r++) matrix[4 * c + r] = m[r, c];
                    renderers.Add(new
                    {
                        positions,
                        triangles = mesh.triangles,
                        uvs,
                        localToWorld = matrix,
                        scaleInLightmap = renderer.scaleInLightmap,
                        lodLevel = levels.TryGetValue(renderer, out var level) ? (int?)level : null,
                        scene = guid,
                        parameters = new
                        {
                            backFaceTolerance = parameters.BackFaceTolerance,
                            antiAliasingSamples = parameters.AntiAliasingSamples,
                            pushoff = parameters.Pushoff,
                            bakedLightmapTag = parameters.BakedLightmapTag,
                            maxLightmapCount = parameters.MaxLightmapCount,
                        },
                    });
                    placed.Add((renderer, fileId));
                }
            }
            bool fixedSize = settings.LightmapSizeFixed
                || UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.entities.graphics") != null
                || UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.rendering.hybrid") != null;
            JsonElement root;
            try
            {
                var request = new
                {
                    maxSize = settings.lightmapMaxSize,
                    padding = settings.lightmapPadding,
                    resolution = settings.lightmapResolution,
                    fixedSize,
                    renderers,
                };
                root = JsonDocument.Parse(Native.Call("baked-gi-atlas", JsonSerializer.Serialize(request))).RootElement;
            }
            catch (Exception e)
            {
                Warn("failed:" + path, $"{path}: Unbound's layout of its baked lightmaps (Lightmapping.Bake) failed: {e.Message}");
                return;
            }
            var lightmaps = new List<int[]>();
            foreach (var size in root.GetProperty("lightmaps").EnumerateArray())
                lightmaps.Add(new[] { size[0].GetInt32(), size[1].GetInt32() });
            var entries = new List<object>();
            int k = 0;
            foreach (var p in root.GetProperty("placements").EnumerateArray())
            {
                var st = p.GetProperty("scaleOffset");
                var (renderer, fileId) = placed[k++];
                entries.Add(new
                {
                    path = HierarchyPath(renderer.transform),
                    fileId,
                    lightmap = p.GetProperty("lightmap").GetInt32(),
                    scaleOffset = new[] { st[0].GetSingle(), st[1].GetSingle(), st[2].GetSingle(), st[3].GetSingle() },
                });
            }
            try
            {
                var folder = Path.Combine(ProjectInfo.ProjectPath, "Library", "Unbound", "BakedGI", guid);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "atlas.json"),
                    JsonSerializer.Serialize(new { scene = path, lightmaps, renderers = entries }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                Warn("write:" + path, $"{path}: Unbound could not write the layout of its baked lightmaps: {e.Message}");
            }
        }
    }
}
