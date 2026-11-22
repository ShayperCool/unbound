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
    /// <para>The renderers laid out are those the Editor lays out: enabled MeshRenderers of
    /// active GameObjects with Contribute Global Illumination, made from the scene file. Those lit
    /// by light probes (Receive Global Illumination) go after the others into atlases of their own
    /// that are no lightmaps. A renderer that cannot be laid out (no mesh, a
    /// submesh that is not triangles) is left out and named; Lightmap Parameters that cannot be
    /// read, a built-in one other than Default-Medium or one with Limit Lightmap Count, and a
    /// lightmapped Terrain, and lightmap UVs that span no area (a mesh without UVs) leave the scene
    /// without a layout (said once). A renderer with Scale In Lightmap 0 takes its place in the
    /// layout without a lightmap (index 0xFFFE). A layout an earlier bake wrote is removed first.</para>
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
                BackFaceTolerance = 0.9f, AntiAliasingSamples = 2, Pushoff = 0.0001f, BakedLightmapTag = -1, MaxLightmapCount = 0,
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
                            AntiAliasingSamples = b.Int("antiAliasingSamples", 2),
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

        /// <summary>The scene's MeshRenderers made from its file, the highest file ID first: the
        /// order the Editor numbers them in, which keeps renderers of one Morton code and size in
        /// it (tests/projects/bakedatlas Ties: not the hierarchy's order; renderers of prefab
        /// instances are not measured).</summary>
        static List<(MeshRenderer Renderer, long FileId)> InEditorOrder(LoadedFile file)
        {
            var ordered = new List<(MeshRenderer, long)>();
            foreach (var pair in file.Objects)
                if (pair.Value is MeshRenderer r) ordered.Add((r, pair.Key));
            ordered.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return ordered;
        }

        static string LayoutPath(string guid) => Path.Combine(ProjectInfo.ProjectPath, "Library", "Unbound", "BakedGI", guid, "atlas.json");

        /// <summary>Removes the layout an earlier bake of the scene wrote.</summary>
        static void Forget(string path, string guid)
        {
            if (string.IsNullOrEmpty(ProjectInfo.ProjectPath)) return;
            try
            {
                var file = LayoutPath(guid);
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Warn("forget:" + path, $"{path}: Unbound could not remove the layout of an earlier bake of its baked lightmaps: {e.Message}");
            }
        }

        static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Lays out the baked lightmaps of the scene made from <paramref name="file"/> (Lightmapping.Bake in Edit
        /// mode) and writes the layout; nothing when its settings have baked global illumination off or
        /// no renderer of it is lightmapped.</summary>
        internal static void Lay(LoadedFile file, LightingSettings settings)
        {
            settings ??= UnityEditor.Lightmapping.lightingSettingsDefaults;
            string path = file.Path;
            string guid = string.IsNullOrEmpty(file.Guid) && Native.IsReady ? UnityEditor.AssetDatabase.AssetPathToGUID(path) : file.Guid;
            // The layout of an earlier bake is not this one's, whatever this one makes.
            if (!string.IsNullOrEmpty(guid)) Forget(path, guid);
            if (!settings.bakedGI) return;
            foreach (var obj in file.Objects.Values)
                if (obj is Terrain terrain && terrain != null && terrain.enabled && terrain.gameObject.activeInHierarchy
                    && (terrain.gameObject.m_StaticEditorFlags & ContributeGI) != 0)
                {
                    Warn("terrain:" + path, $"{path}: '{terrain.name}' is a lightmapped Terrain, which Unbound's layout of the baked lightmaps (Lightmapping.Bake) does not support; it lays out none for the scene.");
                    return;
                }
            var levels = LodLevels(file);
            var renderers = new List<object>();
            var placed = new List<(MeshRenderer renderer, long fileId)>();
            using (Mesh.EngineReads())
            {
                foreach (var (renderer, fileId) in InEditorOrder(file))
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    // A renderer with Scale In Lightmap 0, and one lit by light probes (in atlases of
                    // its own), is laid out too: it affects the lightmaps but gets none (index
                    // 0xFFFE, set natively).
                    if ((renderer.gameObject.m_StaticEditorFlags & ContributeGI) == 0) continue;
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
                    if (string.IsNullOrEmpty(guid))
                    {
                        Warn("guid:" + path, $"{path}: a scene without a GUID; Unbound does not lay out its baked lightmaps (Lightmapping.Bake).");
                        return;
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
                        name = renderer.name,
                        positions,
                        triangles = mesh.triangles,
                        uvs,
                        localToWorld = matrix,
                        scaleInLightmap = renderer.scaleInLightmap,
                        lodLevel = levels.TryGetValue(renderer, out var level) ? (int?)level : null,
                        scene = guid,
                        probeLit = renderer.receiveGI != ReceiveGI.Lightmaps,
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
            if (renderers.Count == 0) return;
            bool fixedSize = settings.LightmapSizeFixed
                || UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.entities.graphics") != null
                || UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.rendering.hybrid") != null;
            var lightmaps = new List<int[]>();
            var entries = new List<object>();
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
                // The request (a vertex that is not a number cannot be sent), the layout (a
                // renderer it refuses, named) and its reading all fail the scene's layout, said.
                var root = JsonDocument.Parse(Native.Call("baked-gi-atlas", JsonSerializer.Serialize(request))).RootElement;
                foreach (var size in root.GetProperty("lightmaps").EnumerateArray())
                    lightmaps.Add(new[] { size[0].GetInt32(), size[1].GetInt32() });
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
                if (k != placed.Count) throw new InvalidOperationException($"{k} placements for {placed.Count} renderers");
            }
            catch (Exception e)
            {
                Warn("failed:" + path, $"{path}: Unbound's layout of its baked lightmaps (Lightmapping.Bake) failed: {e.Message}");
                return;
            }
            try
            {
                var target = LayoutPath(guid);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllText(target,
                    JsonSerializer.Serialize(new { scene = path, lightmaps, renderers = entries }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                Warn("write:" + path, $"{path}: Unbound could not write the layout of its baked lightmaps: {e.Message}");
            }
        }
    }
}
