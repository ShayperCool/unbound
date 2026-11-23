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
    /// to <c>LibraryUnbound/BakedGI/&lt;scene GUID&gt;/atlas.json</c> for the next parts, the
    /// renderers in the order the Editor's bake numbers them. The native side also rasterizes
    /// each lightmap's texels and gathers the direct light of the scene's baked lights
    /// (<see cref="Lights"/>) as the Editor does (tests/projects/bakedbuffers), so far only to
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
        /// them in the order of the Editor's bake natively).</summary>
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
            var entries = new List<object>();
            foreach (var (path, fileId, lightmap, scaleOffset) in Placed(root, sent))
                entries.Add(new { path, fileId, lightmap, scaleOffset });
            return entries;
        }

        /// <summary>The bake's renderers in its order (of those sent: their paths and file IDs),
        /// each with its lightmap and scale and offset, from the native bake's answer.</summary>
        internal static List<(string Path, long FileId, int Lightmap, float[] ScaleOffset)> Placed(JsonElement root, IReadOnlyList<(string Path, long FileId)> sent)
        {
            var placements = root.GetProperty("placements");
            if (placements.GetArrayLength() != sent.Count) throw new InvalidOperationException($"{placements.GetArrayLength()} placements for {sent.Count} renderers");
            var placed = new List<(string, long, int, float[])>();
            foreach (var which in root.GetProperty("order").EnumerateArray())
            {
                var (path, fileId) = sent[which.GetInt32()];
                var p = placements[which.GetInt32()];
                var st = p.GetProperty("scaleOffset");
                placed.Add((path, fileId, p.GetProperty("lightmap").GetInt32(),
                    new[] { st[0].GetSingle(), st[1].GetSingle(), st[2].GetSingle(), st[3].GetSingle() }));
            }
            return placed;
        }

        /// <summary>The folder the bake's texel buffers are written to, when set
        /// (tests/projects/bakedbuffers).</summary>
        const string BuffersVariable = "UNBOUND_BAKED_GI_BUFFERS";

        /// <summary>The side of the faces of the sky a bake takes
        /// (tests/projects/bakedbuffers sky).</summary>
        const int SkyFaceSize = 128;

        /// <summary>Where a Unity installation keeps the blue noise data its bakes sample with.</summary>
        const string BlueNoiseData = "Resources/GI/SamplingData/bluenoisedata";

        /// <summary>A material's emission is baked when its flags have it emit for baked or
        /// for realtime lighting, unless they say it is black (measured: a material with
        /// RealtimeEmissive alone lights the baked lightmaps too, one with neither flag does
        /// not, tests/projects/bakedbuffers Glow; one with both an emissive flag and
        /// EmissiveIsBlack is not measured).</summary>
        internal static bool Emits(Material material) =>
            (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) != 0
            && (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;

        static readonly Func<Material, bool> s_Emits = Emits;

        static readonly Action<string, string> s_Warn = Approximated;

        /// <summary>The sky a bake takes: the ambient source's six faces of
        /// <see cref="SkyFaceSize"/> texels a side, black where the environment's light goes to
        /// the realtime lightmaps instead (Realtime Global Illumination on, and
        /// LightingSettings.realtimeEnvironmentLighting: matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/bakedbuffers SkyRealtime, SkyRealtimeBaked).</summary>
        internal static float[] SkyOf(LightingSettings settings) =>
            settings.realtimeEnvironmentLighting
                ? new float[6 * SkyFaceSize * SkyFaceSize * 3]
                : RealtimeGI.SkyFaces(SkyFaceSize);

        /// <summary>What the bake's material images draw a submesh whose material slot is empty
        /// with: white as Default-Material draws it (matches the Unity 6000.0.71f1 Editor in the
        /// built-in render pipeline, tests/projects/bakedbuffers SpareEmpty; a project with a
        /// render pipeline asset is not measured). The submesh stops no ray all the same.</summary>
        static Material EmptySlotMaterial() =>
            BuiltinShaders.Resolve(BuiltinShaders.ExtraGuid, BuiltinShaders.DefaultMaterialFileId) as Material;

        /// <summary>
        /// What the bake's indirect light is made with: the Lighting Settings' Indirect Samples
        /// and bounces, the installation's blue noise data, and each renderer's albedo and
        /// emission images, drawn by its materials' meta passes over the bounds of its lightmap
        /// UVs at the size the layout (<paramref name="layout"/>, the bake's answer) gives it.
        /// Null, said, where they cannot be made: the direct light is baked alone.
        /// </summary>
        static object Indirect(string path, LightingSettings settings, List<(MeshRenderer renderer, long fileId)> placed, string layout)
        {
            string blueNoise = Path.Combine(UnityEditor.EditorApplication.applicationContentsPath ?? "", BlueNoiseData);
            if (!File.Exists(blueNoise))
            {
                Approximated("bluenoise:" + path, $"{path}: the Unity installation has no {BlueNoiseData} ({blueNoise}); Unbound's bake (Lightmapping.Bake) makes no indirect light without it.");
                return null;
            }
            var placements = JsonDocument.Parse(layout).RootElement.GetProperty("placements");
            if (placements.GetArrayLength() != placed.Count) throw new InvalidOperationException($"{placements.GetArrayLength()} placements for {placed.Count} renderers");
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var materials = new List<object>();
            var withUVs = new Dictionary<Mesh, Mesh>();
            try
            {
                for (int i = 0; i < placed.Count; i++)
                {
                    var renderer = placed[i].renderer;
                    var p = placements[i];
                    // A renderer the bake leaves out (a mesh without normals) has no images.
                    int width = 1, height = 1;
                    var scaleOffset = new Vector4(1f, 1f, 0f, 0f);
                    if (p.ValueKind == JsonValueKind.Object)
                    {
                        width = p.GetProperty("image")[0].GetInt32();
                        height = p.GetProperty("image")[1].GetInt32();
                        var st = p.GetProperty("imageScaleOffset");
                        scaleOffset = new Vector4(st[0].GetSingle(), st[1].GetSingle(), st[2].GetSingle(), st[3].GetSingle());
                    }
                    var mesh = BakedUVs(renderer.RealtimeGIMesh(), withUVs);
                    var albedo = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
                    var emission = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    try
                    {
                        foreach (var (target, emissive) in new[] { (albedo, false), (emission, true) })
                            GIMaterialImages.Draw(target, width, height, emissive, linear, settings.albedoBoost, realtimeUVs: false,
                                () => GIMaterialImages.DrawRenderer(renderer, mesh, scaleOffset, emissive, s_Emits, "the bake of baked lightmaps", s_Warn, EmptySlotMaterial()));
                        var albedoBytes = Native.ReadPixels(albedo.EnsureCreated(), 0, 0, width, height, false);
                        var emissionBytes = Native.ReadPixels(emission.EnsureCreated(), 0, 0, width, height, true);
                        if (albedoBytes == null || emissionBytes == null)
                        {
                            Approximated("headless:" + path, $"{path}: Unbound's bake (Lightmapping.Bake) needs a GPU for its materials' meta passes; it makes no indirect light without one.");
                            return null;
                        }
                        var shared = renderer.sharedMaterials;
                        var doubleSided = new bool[shared.Length];
                        for (int k = 0; k < shared.Length; k++) doubleSided[k] = shared[k] != null && shared[k].doubleSidedGI;
                        materials.Add(new
                        {
                            albedo = new { width, height, data = Convert.ToBase64String(albedoBytes) },
                            emissive = new { width, height, data = Convert.ToBase64String(emissionBytes) },
                            doubleSided,
                        });
                    }
                    finally
                    {
                        RenderTexture.ReleaseTemporary(albedo);
                        RenderTexture.ReleaseTemporary(emission);
                    }
                }
            }
            finally
            {
                foreach (var copy in withUVs.Values)
                    if (copy != null) Object.DestroyImmediate(copy);
            }
            var sky = SkyOf(settings);
            var skyBytes = new byte[sky.Length * sizeof(float)];
            Buffer.BlockCopy(sky, 0, skyBytes, 0, skyBytes.Length);
            return new
            {
                samples = settings.indirectSampleCount,
                maxBounces = settings.maxBounces,
                minBounces = Math.Min(settings.minBounces, settings.maxBounces),
                blueNoise,
                materials,
                environment = new
                {
                    samples = settings.environmentSampleCount,
                    importanceSampling = settings.environmentImportanceSampling,
                    side = SkyFaceSize,
                    faces = Convert.ToBase64String(skyBytes),
                },
            };
        }

        /// <summary>The mesh a renderer's material images are drawn with: its own, or for a mesh
        /// without lightmap UVs a copy that has its UVs as them (the bake lays such a mesh out by
        /// its UVs).</summary>
        static Mesh BakedUVs(Mesh mesh, Dictionary<Mesh, Mesh> copies)
        {
            if (mesh == null) return null;
            using (Mesh.EngineReads())
            {
                if (mesh.uv2.Length == mesh.vertexCount) return mesh;
                if (!copies.TryGetValue(mesh, out var copy))
                {
                    copy = Object.Instantiate(mesh);
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    copy.uv2 = mesh.uv;
                    copies[mesh] = copy;
                }
                return copy;
            }
        }

        static string LayoutPath(string guid) => ProjectInfo.UnboundPath("BakedGI", guid, "atlas.json");

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

        /// <summary>The scene's light probes the bake lights: each enabled Light Probe Group of an
        /// active GameObject, with its file ID and its points in world space, as three numbers
        /// each.</summary>
        internal static List<object> Probes(LoadedFile file)
        {
            var groups = new List<object>();
            foreach (var pair in file.Objects)
            {
                if (!LightProbeGroup.TakenByBake(pair.Value, out var group)) continue;
                var world = group.WorldProbePositions();
                var points = new float[world.Length * 3];
                for (int i = 0; i < world.Length; i++)
                {
                    points[3 * i] = world[i].x;
                    points[3 * i + 1] = world[i].y;
                    points[3 * i + 2] = world[i].z;
                }
                groups.Add(new { fileId = pair.Key, points });
            }
            return groups;
        }

        /// <summary>The scene's lights that take part in a bake: enabled mixed and baked lights of
        /// active GameObjects, made from its file, each with its colour as the bake takes it
        /// (times its intensity, and times its Indirect Multiplier, linear). Said once per scene
        /// and left out or simplified: area lights, lights whose intensity multiplies their linear
        /// colour (GraphicsSettings.lightsUseLinearIntensity; not measured), cookies (ignored),
        /// soft baked shadows (a Baked Shadow Angle or Radius: baked hard).</summary>
        /// <summary>Why a bake's lightmaps (and its lighting data) are not written, or null: a
        /// renderer or light of a prefab instance (how the lighting data names it is not
        /// measured: its IDs combine the instance's and the prefab's), part of their light left
        /// out or approximated (said), no indirect light made, a light whose baking output is
        /// not measured (a mixed light without shadows in the Subtractive Lighting Mode).</summary>
        internal static string ImagesRefused(bool prefabs, bool approximated, bool indirectMade, bool lightsMeasured) =>
            prefabs ? "a baked renderer or light of a prefab instance: how the lighting data names it is not measured"
            : approximated || !indirectMade ? "it did not make all of the lightmaps' light (said)"
            : !lightsMeasured ? "a mixed light without shadows in the Subtractive Lighting Mode: what the lighting data keeps of it is not measured"
            : null;

        /// <summary>Whether the bake under way leaves out or approximates part of its light (a
        /// light it does not bake as the Editor does, the indirect light it cannot make): its
        /// lightmaps are then not written into the project.</summary>
        static bool s_Approximated;

        /// <summary>Says once that the bake leaves out or approximates part of its light.</summary>
        static void Approximated(string key, string message)
        {
            s_Approximated = true;
            Warn(key, message);
        }

        internal static List<object> Lights(LoadedFile file, string path)
        {
            var lights = new List<object>();
            foreach (var pair in file.Objects)
            {
                if (!(pair.Value is Light light) || light == null || !light.enabled || !light.gameObject.activeInHierarchy) continue;
                if (light.lightmapBakeType == LightmapBakeType.Realtime) continue;
                int kind = light.type == LightType.Directional ? 0 : light.type == LightType.Point ? 1 : light.type == LightType.Spot ? 2 : -1;
                if (kind < 0)
                {
                    Approximated("light-type:" + path + pair.Key, $"{path}: '{light.name}' is a {light.type} light, which Unbound's bake (Lightmapping.Bake) does not bake yet.");
                    continue;
                }
                if (UnityEngine.Rendering.GraphicsSettings.lightsUseLinearIntensity)
                {
                    Approximated("light-linear:" + path, $"{path}: lights whose intensity multiplies their linear colour (GraphicsSettings.lightsUseLinearIntensity) are not baked by Unbound's bake (Lightmapping.Bake) yet.");
                    continue;
                }
                if (light.cookie != null)
                    Approximated("light-cookie:" + path + pair.Key, $"{path}: '{light.name}' has a cookie, which Unbound's bake (Lightmapping.Bake) does not apply yet.");
                if ((kind == 0 ? light.shadowAngle : light.shadowRadius) > 0f && light.shadows != LightShadows.None)
                    Approximated("light-soft:" + path + pair.Key, $"{path}: '{light.name}' has soft baked shadows (its Baked Shadow {(kind == 0 ? "Angle" : "Radius")}), which Unbound's bake (Lightmapping.Bake) bakes hard yet.");
                var t = light.transform;
                Color direct = light.ScaledColor(light.intensity, linear: true), indirect = light.ScaledColor(light.intensity * light.bounceIntensity, linear: true);
                Vector3 position = t.position;
                Quaternion rotation = t.rotation;
                lights.Add(new
                {
                    name = HierarchyPath(t),
                    fileId = pair.Key,
                    kind,
                    mode = light.lightmapBakeType == LightmapBakeType.Mixed ? 1 : 2,
                    color = new[] { direct.r, direct.g, direct.b },
                    indirectColor = new[] { indirect.r, indirect.g, indirect.b },
                    position = new[] { position.x, position.y, position.z },
                    rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w },
                    range = light.range,
                    spotAngle = light.spotAngle,
                    castsShadows = light.shadows != LightShadows.None,
                });
            }
            return lights;
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
        internal static BakeResult Lay(LoadedFile file, LightingSettings settings, bool images = false)
        {
            settings ??= UnityEditor.Lightmapping.lightingSettingsDefaults;
            s_Approximated = false;
            string path = file.Path;
            string guid = string.IsNullOrEmpty(file.Guid) && Native.IsReady ? UnityEditor.AssetDatabase.AssetPathToGUID(path) : file.Guid;
            // The layout of an earlier bake is not this one's, whatever this one makes.
            if (!string.IsNullOrEmpty(guid)) Forget(path, guid);
            if (!settings.bakedGI) return null;
            foreach (var obj in file.Objects.Values)
                if (obj is Terrain terrain && terrain != null && terrain.enabled && terrain.gameObject.activeInHierarchy
                    && (terrain.gameObject.m_StaticEditorFlags & ContributeGI) != 0)
                {
                    Warn("terrain:" + path, $"{path}: '{terrain.name}' is a lightmapped Terrain, which Unbound's layout of the baked lightmaps (Lightmapping.Bake) does not support; it lays out none for the scene.");
                    return null;
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
                        return null;
                    }
                    if (!(ParametersOf(renderer.m_LightmapParameters, settings, out var why) is Parameters parameters))
                    {
                        Warn("parameters:" + path, $"{path}: '{renderer.name}' has {why}; Unbound does not lay out its baked lightmaps (Lightmapping.Bake).");
                        return null;
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
                    // A submesh without a material is not drawn: the bake's rays pass it.
                    var slots = renderer.sharedMaterials;
                    var hasMaterial = new bool[slots.Length];
                    for (int k = 0; k < slots.Length; k++) hasMaterial[k] = slots[k] != null;
                    renderers.Add(new
                    {
                        name = renderer.name,
                        fileId,
                        mesh = index,
                        hasMaterial,
                        localToWorld = matrix,
                        scaleInLightmap = renderer.scaleInLightmap,
                        lodLevel = levels.TryGetValue(renderer, out var level) ? (int?)level : null,
                        probeLit = renderer.receiveGI != ReceiveGI.Lightmaps,
                        castsShadows = renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off,
                        receivesShadows = renderer.receiveShadows,
                        stitchSeams = renderer.stitchLightmapSeams,
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
            if (renderers.Count == 0) return null;
            foreach (var (renderer, _) in placed)
                if (levels.ContainsKey(renderer))
                {
                    Approximated("lod:" + path, $"{path}: '{renderer.name}' is of a LOD Group; Unbound's bake (Lightmapping.Bake) does not tell the levels of LOD Groups apart yet (their renderers take and stop light like any other).");
                    break;
                }
            bool fixedSize = settings.LightmapSizeFixed
                || UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.entities.graphics") != null
                || UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.rendering.hybrid") != null;
            var lightmaps = new List<int[]>();
            List<object> entries;
            BakeResult result = null;
            try
            {
                var lights = Lights(file, path);
                var probes = Probes(file);
                string Request(bool layoutOnly, object indirect) => JsonSerializer.Serialize(new
                {
                    maxSize = settings.lightmapMaxSize,
                    padding = settings.lightmapPadding,
                    resolution = settings.lightmapResolution,
                    fixedSize,
                    scene = guid,
                    meshes,
                    renderers,
                    lights,
                    mixedMode = (int)settings.mixedBakeMode,
                    directional = settings.directionalityMode == LightmapsMode.CombinedDirectional,
                    probes,
                    layoutOnly,
                    indirect,
                    lightmapImages = images && !layoutOnly,
                });
                // The request, the bake (a renderer it refuses, named) and the reading of its
                // answer all fail the scene's layout, said.
                var paths = new List<(string, long)>();
                foreach (var (renderer, fileId) in placed) paths.Add((HierarchyPath(renderer.transform), fileId));
                // The indirect light needs the renderers' material images, whose sizes come
                // with the layout: the layout is asked for first. Nothing reads the bake's light
                // yet but the folder its texel buffers are written to, so without one the
                // materials are not drawn.
                object indirect = null;
                if (images || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(BuffersVariable)))
                    indirect = Indirect(path, settings, placed, Native.Call("baked-gi-bake", Request(true, null)));
                var answer = Native.Call("baked-gi-bake", Request(false, indirect));
                entries = Read(answer, paths, lightmaps);
                // A renderer or light of a prefab instance: how the lighting data names it is not
                // measured (its IDs combine the instance's and the prefab's), so none is written.
                bool prefabs = false;
                if (images)
                {
                    foreach (var (renderer, _) in placed) prefabs |= UnityEditor.PrefabUtility.IsPartOfPrefabInstance(renderer);
                    foreach (var obj in file.Objects.Values)
                        if (obj is Light light && light != null && light.lightmapBakeType != LightmapBakeType.Realtime)
                            prefabs |= UnityEditor.PrefabUtility.IsPartOfPrefabInstance(light);
                    var made = BakeResult.Of(answer, paths);
                    string refused = ImagesRefused(prefabs, s_Approximated, indirect != null, made.LightsMeasured);
                    if (refused != null)
                        Warn("images:" + path, $"{path}: {refused}; Unbound's bake (Lightmapping.Bake) writes neither the lightmaps nor the lighting data.");
                    else result = made;
                }
            }
            catch (Exception e)
            {
                Warn("failed:" + path, $"{path}: Unbound's layout of its baked lightmaps (Lightmapping.Bake) failed: {e.Message}");
                return null;
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
            return result;
        }
    }

    /// <summary>What a bake made that its lighting data keeps: each lightmap's image
    /// (Lightmap-N_comp_light.exr), the bake's renderers in its order with their lightmap and
    /// scale and offset, and its lights' baking outputs (by their file IDs).</summary>
    internal sealed class BakeResult
    {
        internal byte[][] Images;
        internal List<(long FileId, int Lightmap, float[] ScaleOffset)> Renderers;
        internal List<(long FileId, int BakeType, int MixedMode, int Channel, int ProbeIndex)> Lights;
        /// <summary>Whether what the lighting data keeps of every light is measured.</summary>
        internal bool LightsMeasured = true;

        internal static BakeResult Of(string answer, IReadOnlyList<(string Path, long FileId)> sent)
        {
            var root = JsonDocument.Parse(answer).RootElement;
            var result = new BakeResult { Renderers = new(), Lights = new() };
            var images = root.GetProperty("images");
            result.Images = new byte[images.GetArrayLength()][];
            for (int i = 0; i < result.Images.Length; i++) result.Images[i] = Convert.FromBase64String(images[i].GetString());
            foreach (var (_, fileId, lightmap, scaleOffset) in BakedAtlas.Placed(root, sent))
                result.Renderers.Add((fileId, lightmap, scaleOffset));
            foreach (var l in root.GetProperty("lights").EnumerateArray())
            {
                result.LightsMeasured &= l.GetProperty("measured").GetBoolean();
                result.Lights.Add((l.GetProperty("fileId").GetInt64(), l.GetProperty("bakeType").GetInt32(), l.GetProperty("mixedMode").GetInt32(),
                    l.GetProperty("channel").GetInt32(), l.GetProperty("probeIndex").GetInt32()));
            }
            return result;
        }
    }
}
