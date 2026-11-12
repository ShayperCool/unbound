using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Unbound's own precompute of realtime GI (docs/REALTIME_GI.md, A scene's bake) where the
    /// Unity 6000.0.71f1 Editor would bake: a scene the Editor opens whose LightmapSettings ask
    /// for a bake on load (Lightmapping.bakeOnSceneLoad IfMissingLightingData) and that has no
    /// lighting data (tests/projects/realtimegi bake cases: the Editor bakes when it opens such a
    /// scene; a scene loaded at runtime is not baked). The bake is the realtime part only: the
    /// Editor's bake also makes baked lightmaps, the light probes' baked lighting, reflection
    /// probes and the environment lighting, which Unbound does not (each said once).
    /// </summary>
    internal static partial class RealtimeGI
    {
        /// <summary>StaticEditorFlags.ContributeGI.</summary>
        const uint ContributeGI = 1;

        /// <summary>Says once what the Editor's bake of a scene it opens without lighting data
        /// would make and Unbound does not: baked lightmaps and the light probes' baked lighting
        /// (baked global illumination on; a scene without Lighting Settings has the defaults',
        /// which have it on), the environment lighting from a sky (the ambient probe and the
        /// default reflection), baked reflection probes. Unbound makes the realtime GI alone
        /// (<see cref="Bake"/>).</summary>
        internal static void NoteBakeParts(LoadedFile file, LightingSettings settings)
        {
            string path = file?.Path ?? "a scene";
            if (settings == null || settings.bakedGI)
                Warn("bake-baked:" + path, $"{path}: its bake on load (Lightmapping.bakeOnSceneLoad) would make baked lightmaps and the light probes' baked lighting, which Unbound does not make.");
            if (RenderSettings.skybox != null || RenderSettings.ambientMode == Rendering.AmbientMode.Skybox)
                Warn("bake-environment:" + path, $"{path}: its bake on load (Lightmapping.bakeOnSceneLoad) would make the environment lighting from the sky (the ambient probe and the default reflection), which Unbound does not make.");
            if (file != null)
                foreach (var obj in file.Objects.Values)
                    if (obj is ReflectionProbe probe && probe.mode == Rendering.ReflectionProbeMode.Baked)
                    {
                        Warn("bake-reflection:" + path, $"{path}: its bake on load (Lightmapping.bakeOnSceneLoad) would bake its reflection probes, which Unbound does not make.");
                        break;
                    }
        }

        /// <summary>The renderers of the scene's LOD Groups below their most detailed level: realtime
        /// GI lights only the most detailed one (Unity 6000.0 Manual, "Lightmapping LODs": "the
        /// system lights the most detailed model out of the LOD Group as if it has the Contribute
        /// Global Illumination setting enabled", lower LODs take their indirect light from light
        /// probes).</summary>
        static HashSet<Renderer> LowerLods(LoadedFile file)
        {
            var lower = new HashSet<Renderer>();
            foreach (var obj in file.Objects.Values)
            {
                if (!(obj is LODGroup group)) continue;
                var lods = group.GetLODs();
                for (int level = 1; level < lods.Length; level++)
                    foreach (var r in lods[level].renderers)
                        if (r != null) lower.Add(r);
                // A renderer at the most detailed level as well stays in.
                if (lods.Length > 0)
                    foreach (var r in lods[0].renderers)
                        if (r != null) lower.Remove(r);
            }
            return lower;
        }

        /// <summary>The Lightmap Parameters the bake takes: the realtime fields of the built-in
        /// Default-Medium asset (tests/projects/realtimegi's scenes use it, and its
        /// RoomSystemsSystemTag1 copy of it); of these the bake takes the Cluster Resolution, the
        /// Irradiance Budget and Quality, the Modelling Tolerance and the System Tag as they are
        /// and refuses other values of the rest.</summary>
        internal struct BakeParameters
        {
            internal float Resolution, ClusterResolution, BackFaceTolerance, ModellingTolerance, Pushoff;
            internal int IrradianceBudget, IrradianceQuality, SystemTag;
            internal bool IsTransparent, EdgeStitching;

            internal static BakeParameters DefaultMedium => new BakeParameters
            {
                Resolution = 1f, ClusterResolution = 0.5f, IrradianceBudget = 128, IrradianceQuality = 8192,
                BackFaceTolerance = 0.9f, IsTransparent = false, ModellingTolerance = 0.01f, SystemTag = -1,
                EdgeStitching = true, Pushoff = 0.0001f,
            };

            /// <summary>Why the bake cannot take these parameters, or null.</summary>
            internal string Refusal()
            {
                var d = DefaultMedium;
                if (Resolution != d.Resolution) return $"a Resolution of {Resolution} (only {d.Resolution} is measured)";
                if (BackFaceTolerance != d.BackFaceTolerance) return $"a Backface Tolerance of {BackFaceTolerance} (only {d.BackFaceTolerance} is measured)";
                if (IsTransparent) return "Is Transparent (not measured)";
                if (!EdgeStitching) return "Edge Stitching off (not measured)";
                if (Pushoff != d.Pushoff) return $"a Pushoff of {Pushoff} (only {d.Pushoff} is measured)";
                if (!(ClusterResolution > 0f) || !(IrradianceBudget > 0) || !(IrradianceQuality > 0))
                    return $"a Cluster Resolution of {ClusterResolution}, an Irradiance Budget of {IrradianceBudget} and Quality of {IrradianceQuality}";
                return null;
            }

            /// <summary>What makes renderers share a system (all the realtime fields).</summary>
            internal ulong Group()
            {
                ulong h = 1469598103934665603UL;
                void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
                Mix(BitConverter.SingleToInt32Bits(Resolution));
                Mix(BitConverter.SingleToInt32Bits(ClusterResolution));
                Mix(BitConverter.SingleToInt32Bits(BackFaceTolerance));
                Mix(BitConverter.SingleToInt32Bits(ModellingTolerance));
                Mix(BitConverter.SingleToInt32Bits(Pushoff));
                Mix(IrradianceBudget);
                Mix(IrradianceQuality);
                Mix(SystemTag);
                Mix(IsTransparent ? 1 : 0);
                Mix(EdgeStitching ? 1 : 0);
                return h;
            }
        }

        static readonly Dictionary<(string, long), BakeParameters?> s_Parameters = new Dictionary<(string, long), BakeParameters?>();

        /// <summary>The Lightmap Parameters a reference names (none: the scene's, else
        /// Default-Medium), or null when they cannot be read.</summary>
        static BakeParameters? ParametersOf(ObjectRef reference, LightingSettings settings)
        {
            if (reference.IsNull && settings != null) reference = settings.DefaultLightmapParameters;
            if (!OwnParameters(reference)) return BakeParameters.DefaultMedium;
            // The other built-in assets (Default-HighResolution and the lower ones) are not measured.
            if (reference.Guid == BuiltinExtraGuid) return null;
            var key = (reference.Guid, reference.FileId);
            if (s_Parameters.TryGetValue(key, out var known)) return known;
            BakeParameters? read = null;
            try
            {
                foreach (var doc in Native.LoadDocuments(reference.Guid))
                {
                    if (doc.ClassId != 1113 || doc.FileId != reference.FileId) continue;
                    var b = doc.Body;
                    read = new BakeParameters
                    {
                        Resolution = b.Float("resolution", 1f),
                        ClusterResolution = b.Float("clusterResolution", 0.5f),
                        IrradianceBudget = b.Int("irradianceBudget", 128),
                        IrradianceQuality = b.Int("irradianceQuality", 8192),
                        BackFaceTolerance = b.Float("backFaceTolerance", 0.9f),
                        IsTransparent = b.Bool("isTransparent"),
                        ModellingTolerance = b.Float("modellingTolerance", 0.01f),
                        SystemTag = b.Int("systemTag", -1),
                        EdgeStitching = b.Int("edgeStitching", 1) != 0,
                        Pushoff = b.Float("pushoff", 0.0001f),
                    };
                }
            }
            catch (Exception) { read = null; }
            s_Parameters[key] = read;
            return read;
        }

        /// <summary>The scene's realtime GI made by Unbound's own precompute from its renderers
        /// (what <c>realtime-gi-load</c> answers for the Editor's data, with the renderers it
        /// left out), or null when there is nothing to bake or the bake failed (said once).
        /// Its light probes, those of its LightProbeGroups, become the scene's.</summary>
        static JsonElement? Bake(int scene, LoadedFile file, LightingSettings settings)
        {
            float resolution = settings.indirectResolution;
            var renderers = new List<object>();
            var refused = new List<string>();
            var probes = new List<float>();
            var probePositions = new List<Vector3>();
            var lowerLods = LowerLods(file);
            foreach (var pair in file.Objects)
            {
                if (pair.Value is Terrain terrain && terrain.enabled && terrain.gameObject.activeInHierarchy
                    && (terrain.gameObject.m_StaticEditorFlags & ContributeGI) != 0)
                    refused.Add($"'{terrain.name}': a Terrain (not made by Unbound's precompute)");
                if (pair.Value is LightProbeGroup group && group.enabled && group.gameObject.activeInHierarchy)
                    foreach (var local in group.probePositions)
                    {
                        var p = group.transform.TransformPoint(local);
                        probePositions.Add(p);
                        probes.Add(p.x); probes.Add(p.y); probes.Add(p.z);
                    }
                if (!(pair.Value is MeshRenderer renderer) || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if ((renderer.gameObject.m_StaticEditorFlags & ContributeGI) == 0 || renderer.receiveGI != ReceiveGI.Lightmaps
                    || lowerLods.Contains(renderer)) continue;
                if (BakeRequestOf(pair.Key, renderer, resolution, settings, out var why) is object request) renderers.Add(request);
                else refused.Add($"'{renderer.name}': {why}");
            }
            foreach (var why in refused)
                Warn("bake:" + file.Path + why, $"{file.Path}: a renderer is left out of Unbound's realtime GI bake: {why}");
            if (renderers.Count == 0)
            {
                if (probePositions.Count > 0)
                    Warn("bake-probes:" + file.Path, $"{file.Path}: its bake on load would light its light probes from realtime GI; it has no renderer Unbound's precompute takes, so they are not lit.");
                return null;
            }
            JsonElement root;
            try
            {
                var json = Native.Call("realtime-gi-bake", JsonSerializer.Serialize(new { renderers, probes }));
                root = JsonDocument.Parse(json).RootElement;
            }
            catch (Exception e)
            {
                Warn("bake-failed:" + file.Path, $"{file.Path}: Unbound's realtime GI bake of it failed, so it has no realtime GI: {e.Message}");
                return null;
            }
            if (root.TryGetProperty("refused", out var left))
                foreach (var r in left.EnumerateArray())
                {
                    var name = BakedLighting.Find(file, r)?.name ?? "?";
                    Warn("bake-refused:" + file.Path + name, $"{file.Path}: '{name}' is left out of Unbound's realtime GI bake: {r.GetProperty("error").GetString()}");
                }
            if (probePositions.Count > 0)
            {
                var set = new ProbeSet
                {
                    Positions = probePositions.ToArray(),
                    Probes = new Rendering.SphericalHarmonicsL2[probePositions.Count],
                    Occlusion = new Vector4[probePositions.Count],
                    Baked = false,
                };
                for (int i = 0; i < set.Occlusion.Length; i++) set.Occlusion[i] = Vector4.one;
                ProbeTetrahedralization.Build(set);
                LightProbes.SceneLoaded(scene, LightProbes.FromSet(set));
            }
            return root;
        }

        /// <summary>A renderer of the bake as the native side takes it (its mesh, its matrix and
        /// the settings that make its realtime lightmap UVs and clusters), or null with why it
        /// cannot be baked.</summary>
        static object BakeRequestOf(long id, MeshRenderer renderer, float resolution, LightingSettings settings, out string why)
        {
            why = null;
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) { why = "it has no mesh"; return null; }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) { why = $"a submesh of {mesh.GetTopology(sub)}"; return null; }
            float scale = renderer.scaleInLightmap * resolution;
            if (!(scale > 0f)) { why = "it has no realtime lightmap resolution"; return null; }
            if (!(ParametersOf(renderer.m_LightmapParameters, settings) is BakeParameters parameters))
            {
                why = renderer.m_LightmapParameters.Guid == BuiltinExtraGuid
                    ? "its built-in Lightmap Parameters are not Default-Medium (not measured)"
                    : "its Lightmap Parameters cannot be read";
                return null;
            }
            if (parameters.Refusal() is string refusal) { why = "its Lightmap Parameters have " + refusal; return null; }
            bool automatic = MakesAutomaticCharts(renderer);
            if (automatic && AutomaticChartsRefusal(renderer, mesh, s_NoRenderers) is string charts) { why = charts; return null; }
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var source = mesh.uv3;
            if (source.Length != vertices.Length) source = mesh.uv2;
            if (source.Length != vertices.Length) source = mesh.uv;
            if (source.Length != vertices.Length) { why = "it has no lightmap UVs"; return null; }
            var positions = new float[3 * vertices.Length];
            var normalValues = new float[normals.Length == vertices.Length ? 3 * vertices.Length : 0];
            var uvs = new float[2 * vertices.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                positions[3 * v] = vertices[v].x; positions[3 * v + 1] = vertices[v].y; positions[3 * v + 2] = vertices[v].z;
                uvs[2 * v] = source[v].x; uvs[2 * v + 1] = source[v].y;
                if (normalValues.Length > 0)
                {
                    normalValues[3 * v] = normals[v].x; normalValues[3 * v + 1] = normals[v].y; normalValues[3 * v + 2] = normals[v].z;
                }
            }
            var m = renderer.transform.localToWorldMatrix;
            var matrix = new float[16];
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++) matrix[4 * c + r] = m[r, c];
            return new
            {
                @object = id,
                prefab = 0L,
                positions,
                normals = normalValues,
                uvs,
                triangles = mesh.triangles,
                localToWorld = matrix,
                pixelSize = 1f / scale,
                minChartTexels = renderer.m_MinimumChartSize,
                automatic = automatic
                    ? new { matrix = WorldScale(renderer.transform), maxDistance = renderer.m_AutoUVMaxDistance, maxAngle = renderer.m_AutoUVMaxAngle, ignoreNormals = renderer.m_IgnoreNormalsForChartDetection }
                    : null,
                clusterSize = 1f / (scale * parameters.ClusterResolution),
                modellingTolerance = parameters.ModellingTolerance,
                group = parameters.Group(),
                rays = parameters.IrradianceQuality,
                budget = parameters.IrradianceBudget,
            };
        }
    }
}
