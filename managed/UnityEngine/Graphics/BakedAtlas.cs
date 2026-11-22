using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// The first parts of Unbound's own baker of baked lightmaps (Q677): where a bake in Edit mode
    /// (Lightmapping.Bake) puts each renderer of a scene whose Lighting Settings have Baked Global
    /// Illumination on, laid out natively as the Unity 6000.0.71f1 Editor lays out its baked
    /// lightmaps (tests/projects/bakedatlas): which lightmap, its lightmap scale and offset, and
    /// each lightmap's size. The bake does not make the lightmaps yet, so it gives renderers
    /// nothing (they keep what they had; said once per scene by the bake) and writes the layout
    /// to <c>Library/Unbound/BakedGI/&lt;scene GUID&gt;/atlas.json</c> for the next parts, the
    /// renderers in the order the Editor's bake numbers them. The native side also rasterizes
    /// each lightmap's texels as the Editor does (tests/projects/bakedbuffers), so far only to
    /// write them out where <c>UNBOUND_BAKED_GI_BUFFERS</c> names a folder.
    /// <para>The renderers laid out are those the Editor bakes: enabled MeshRenderers of
    /// active GameObjects with Contribute Global Illumination, made from the scene file, whose
    /// meshes have normals (the others are left out, said as the Editor says it). Those lit
    /// by light probes (Receive Global Illumination) go after the others into atlases of their own
    /// that are no lightmaps. A renderer that cannot be laid out (no mesh, a
    /// submesh that is not triangles) is left out and named; Lightmap Parameters that cannot be
    /// read, a built-in one other than Default-Medium or one with Limit Lightmap Count, and a
    /// lightmapped Terrain, and lightmap UVs that span no area (a mesh without UVs) or are not
    /// numbers leave the scene without a layout (said once). A renderer with Scale In Lightmap 0
    /// takes its place in the layout without a lightmap (index 0xFFFE). A layout an earlier bake
    /// wrote is removed first.</para>
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

        /// <summary>The scene's MeshRenderers made from its file, by their file IDs (the bake puts
        /// them in the Editor's order natively).</summary>
        static List<(MeshRenderer Renderer, long FileId)> FromFile(LoadedFile file)
        {
            var found = new List<(MeshRenderer, long)>();
            foreach (var pair in file.Objects)
                if (pair.Value is MeshRenderer r) found.Add((r, pair.Key));
            found.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return found;
        }

        /// <summary>Reads the native bake's answer for the renderers sent (their paths and file IDs
        /// in the order sent): the lightmaps' sizes into <paramref name="lightmaps"/>, and the
        /// bake's renderers in its order, each with its lightmap and scale and offset. A mesh
        /// without normals is left out of the bake, which is said as the Editor says it, once a
        /// bake (tests/projects/bakedbuffers Bare).</summary>
        internal static List<object> Read(string answer, IReadOnlyList<(string Path, long FileId)> sent, List<int[]> lightmaps)
        {
            var root = JsonDocument.Parse(answer).RootElement;
            if (root.GetProperty("invalidNormals").GetBoolean())
                Debug.LogWarning("Some meshes have invalid normals and will be ignored by the Global Illumination system.");
            foreach (var size in root.GetProperty("lightmaps").EnumerateArray())
                lightmaps.Add(new[] { size[0].GetInt32(), size[1].GetInt32() });
            var placements = root.GetProperty("placements");
            if (placements.GetArrayLength() != sent.Count) throw new InvalidOperationException($"{placements.GetArrayLength()} placements for {sent.Count} renderers");
            var entries = new List<object>();
            foreach (var which in root.GetProperty("order").EnumerateArray())
            {
                var (path, fileId) = sent[which.GetInt32()];
                var p = placements[which.GetInt32()];
                var st = p.GetProperty("scaleOffset");
                entries.Add(new
                {
                    path,
                    fileId,
                    lightmap = p.GetProperty("lightmap").GetInt32(),
                    scaleOffset = new[] { st[0].GetSingle(), st[1].GetSingle(), st[2].GetSingle(), st[3].GetSingle() },
                });
            }
            return entries;
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

        /// <summary>An array's values as they are in memory (little-endian), in base64: every float
        /// arrives natively as it is.</summary>
        static string Encoded<T>(T[] values) where T : unmanaged =>
            Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<T>(values ?? Array.Empty<T>())));

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
            var meshes = new List<object>();
            var meshIndex = new Dictionary<Mesh, int>();
            using (Mesh.EngineReads())
            {
                foreach (var (renderer, fileId) in FromFile(file))
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
                    if (!meshIndex.TryGetValue(mesh, out int index))
                    {
                        meshIndex[mesh] = index = meshes.Count;
                        var submeshes = new string[mesh.subMeshCount];
                        for (int sub = 0; sub < submeshes.Length; sub++) submeshes[sub] = Encoded<int>(mesh.GetTriangles(sub));
                        meshes.Add(new
                        {
                            positions = Encoded<Vector3>(mesh.vertices),
                            normals = Encoded<Vector3>(mesh.normals),
                            uv = Encoded<Vector2>(mesh.uv),
                            uv2 = Encoded<Vector2>(mesh.uv2),
                            submeshes,
                        });
                    }
                    var m = renderer.transform.localToWorldMatrix;
                    var matrix = new float[16];
                    for (int c = 0; c < 4; c++)
                        for (int r = 0; r < 4; r++) matrix[4 * c + r] = m[r, c];
                    renderers.Add(new
                    {
                        name = renderer.name,
                        fileId,
                        mesh = index,
                        localToWorld = matrix,
                        scaleInLightmap = renderer.scaleInLightmap,
                        lodLevel = levels.TryGetValue(renderer, out var level) ? (int?)level : null,
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
            List<object> entries;
            try
            {
                var request = new
                {
                    maxSize = settings.lightmapMaxSize,
                    padding = settings.lightmapPadding,
                    resolution = settings.lightmapResolution,
                    fixedSize,
                    scene = guid,
                    meshes,
                    renderers,
                };
                // The request, the bake (a renderer it refuses, named) and the reading of its
                // answer all fail the scene's layout, said.
                var paths = new List<(string, long)>();
                foreach (var (renderer, fileId) in placed) paths.Add((HierarchyPath(renderer.transform), fileId));
                entries = Read(Native.Call("baked-gi-bake", JsonSerializer.Serialize(request)), paths, lightmaps);
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
