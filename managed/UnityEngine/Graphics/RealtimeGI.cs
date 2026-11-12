using GI = UnityEngine.Experimental.GlobalIllumination;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>A light as precomputed realtime GI takes it (`realtime_gi::GiLight`).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GiLight
    {
        internal int Kind;
        internal float PositionX, PositionY, PositionZ;
        internal float DirectionX, DirectionY, DirectionZ;
        /// <summary>Linear: color times intensity and bounce intensity.</summary>
        internal float R, G, B;
        internal float Range;
        internal float SpotAngle;
        /// <summary>The angle of a spot light's inner cone, in degrees.</summary>
        internal float InnerSpotAngle;
        /// <summary>Experimental.GlobalIllumination.FalloffType of a point or spot light.</summary>
        internal int Falloff;
    }

    /// <summary>One frame's precomputed realtime GI inputs (`realtime_gi::GiFrame`).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GiFrame
    {
        internal float IndirectScale;
        internal int Synchronous;
        internal float BudgetMs;
        internal float UpdateThreshold;
        /// <summary>LightmapsMode.CombinedDirectional: the systems solve their directionality.</summary>
        internal int Directional;
    }
}

namespace UnityEngine
{
    /// <summary>
    /// Precomputed realtime GI (docs/REALTIME_GI.md): a scene whose lighting data holds it gets
    /// its realtime lightmaps updated at runtime from its realtime lights, emissive materials and
    /// environment, as in the Unity 6000.0.71f1 Editor (tests/projects/realtimegi). The data and
    /// the update live on the native side; this side renders the materials' meta passes into
    /// each system's albedo and emission textures (at load, after
    /// <see cref="RendererExtensions.UpdateGIMaterials"/>), passes the frame's inputs in
    /// PostLateUpdate.EnlightenRuntimeUpdate and uploads the atlases renderers draw with
    /// (unity_DynamicLightmap and, for directional lightmaps, unity_DynamicDirectionality,
    /// <see cref="BakedLighting.Apply"/>).
    /// </summary>
    internal static partial class RealtimeGI
    {
        const int UpdateChanged = 1, UpdateConverged = 2, UpdateProbes = 4;

        /// <summary>Time per frame the systems may take outside synchronous mode. Unity updates
        /// them on worker threads (unmeasured share); Unbound updates at least one system per frame
        /// on the main thread within this budget.</summary>
        const float FrameBudgetMs = 2f;

        /// <summary>The meta pass's largest albedo (unity_MaxOutputValue) when the Editor renders
        /// albedo for realtime GI.</summary>
        const float MaxAlbedo = 0.97f;

        sealed class GISystem
        {
            internal int Atlas, Width, Height;
            internal Renderer[] Renderers;
            internal Vector4[] ScaleOffsets;
            internal bool MaterialsDirty = true;
            /// <summary>The realtime lightmap UV meshes made for its renderers.</summary>
            internal readonly List<Mesh> Streams = new List<Mesh>();
            /// <summary>Its realtime lightmap UVs could not be made: not drawn, not updated.</summary>
            internal bool Failed;
        }

        sealed class GIScene
        {
            internal int Scene;
            internal ulong Handle;
            internal GISystem[] Systems;
            internal Texture2D[] Atlases;
            /// <summary>Per atlas, its directionality; made when the lightmaps are directional.</summary>
            internal Texture2D[] Directionality;
            /// <summary>Per atlas, whether its renderers draw with its directionality: from the
            /// first one read since the lightmaps became directional until they stop being so.</summary>
            internal bool[] DirectionalityBound;
            /// <summary>The lightmaps mode of the scene's last update.</summary>
            internal bool Directional;
            /// <summary>What to say once when the scene is first drawn with directional
            /// lightmaps (its directionality cannot be read, or has unmeasured parts); else null.</summary>
            internal string DirectionalityNote;
            internal float AlbedoBoost = 1f;
            internal bool Converged, Failed;
            /// <summary>The scene's own light probes its probe sets light, and the probes'
            /// coefficients as read back.</summary>
            internal ProbeSet Probes;
            internal float[] ProbeCoefficients = Array.Empty<float>();
            /// <summary>The environment version the native scene has.</summary>
            internal int EnvironmentVersion = -1;
            /// <summary>The scene's environment lighting is baked (Lighting Settings): the
            /// ambient source gives its realtime GI no light.</summary>
            internal bool BakedEnvironment;
        }

        static readonly List<GIScene> s_Scenes = new List<GIScene>();
        static readonly Dictionary<Renderer, (GIScene Scene, int System, int Index)> s_Renderers =
            new Dictionary<Renderer, (GIScene, int, int)>();
        static GiLight[] s_Lights = new GiLight[8];
        static readonly List<Renderer> s_Released = new List<Renderer>();
        static ushort[] s_Texels = Array.Empty<ushort>();
        static byte[] s_Directions = Array.Empty<byte>();
        internal static readonly HashSet<string> s_Warned = new HashSet<string>(StringComparer.Ordinal);
        static bool s_Registered;
        /// <summary>Mixed lights are lit as realtime ones when the first scene bakes no global
        /// illumination.</summary>
        static bool s_MixedAreRealtime;

        /// <summary>DynamicGI.isConverged: every loaded system's update has settled.</summary>
        internal static bool IsConverged
        {
            get
            {
                foreach (var scene in s_Scenes)
                    if (!scene.Converged && !scene.Failed) return false;
                return true;
            }
        }

        /// <summary>DynamicGI.synchronousMode.</summary>
        internal static bool Synchronous;

        static void Warn(string key, string message)
        {
            if (s_Warned.Add(key)) Debug.LogWarning("Unbound: " + message);
        }

        /// <summary>The scene loaded with handle <paramref name="scene"/>: its lighting data's
        /// precomputed realtime GI, if any (a first scene replaces the others').</summary>
        internal static void SceneLoaded(int scene, ObjectRef lightingData, LoadedFile file, LightingSettings settings, bool first)
        {
            if (first)
            {
                Reset();
                s_MixedAreRealtime = settings == null || !settings.bakedGI;
            }
            if (lightingData.IsNull || lightingData.Guid == null || !Native.IsReady || file == null) return;
            if (settings != null && !settings.realtimeGI) return;
            JsonElement root;
            try
            {
                var json = Native.Call("realtime-gi-load", JsonSerializer.Serialize(new
                {
                    guid = lightingData.Guid,
                    fileId = lightingData.FileId,
                    linear = QualitySettings.activeColorSpace == ColorSpace.Linear,
                }));
                root = JsonDocument.Parse(json).RootElement;
            }
            catch (Exception e)
            {
                Warn("load:" + file.Path, $"{file.Path}: its precomputed realtime GI could not be read, so it is not drawn: {e.Message}");
                return;
            }
            if (root.ValueKind != JsonValueKind.Object) return;
            var loaded = new GIScene { Scene = scene, Handle = root.GetProperty("handle").GetUInt64(), AlbedoBoost = settings != null ? settings.albedoBoost : 1f };
            var sizes = new List<Texture2D>();
            foreach (var size in root.GetProperty("atlases").EnumerateArray())
            {
                int n = size.GetInt32();
                sizes.Add(new Texture2D(n, n, TextureFormat.RGBAHalf, false, true)
                {
                    name = "Realtime Lightmap",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                });
            }
            loaded.Atlases = sizes.ToArray();
            loaded.Directionality = new Texture2D[sizes.Count];
            loaded.DirectionalityBound = new bool[sizes.Count];
            var systems = new List<GISystem>();
            foreach (var s in root.GetProperty("systems").EnumerateArray())
            {
                var system = new GISystem
                {
                    Atlas = s.GetProperty("atlas").GetInt32(),
                    Width = s.GetProperty("width").GetInt32(),
                    Height = s.GetProperty("height").GetInt32(),
                };
                var renderers = new List<Renderer>();
                var offsets = new List<Vector4>();
                foreach (var r in s.GetProperty("renderers").EnumerateArray())
                {
                    var renderer = BakedLighting.Find(file, r) as Renderer;
                    var st = r.GetProperty("st");
                    renderers.Add(renderer);
                    offsets.Add(new Vector4(st[0].GetSingle(), st[1].GetSingle(), st[2].GetSingle(), st[3].GetSingle()));
                    if (renderer != null) s_Renderers[renderer] = (loaded, systems.Count, renderers.Count - 1);
                }
                system.Renderers = renderers.ToArray();
                system.ScaleOffsets = offsets.ToArray();
                systems.Add(system);
            }
            for (int index = 0; index < systems.Count; index++)
            {
                var system = systems[index];
                // A system is drawn when every renderer of it has its realtime lightmap UVs.
                var failure = RealtimeUVs(loaded, index, system, settings);
                if (failure != null)
                {
                    Warn("uvs:" + failure, $"{file.Path}: a system of its precomputed realtime GI is not drawn: {failure}");
                    system.Failed = true;
                    foreach (var renderer in system.Renderers)
                        if (renderer is MeshRenderer mesh) mesh.enlightenVertexStream = null;
                    DestroyStreams(system);
                }
                else
                {
                    foreach (var renderer in system.Renderers)
                        if (renderer != null) renderer.m_RealtimeLightmap = loaded.Atlases[system.Atlas];
                }
            }
            loaded.Systems = systems.ToArray();
            s_Scenes.Add(loaded);
            // A scene whose environment lighting is baked: its realtime GI gets no light from
            // the ambient source (matches the Unity 6000.0.71f1 Editor, tests/projects/realtimegi
            // roombakedenvironment-*: an ambient color change shows nothing, the environment
            // alone lights nothing), a script's cube still does.
            loaded.BakedEnvironment = settings != null && !settings.realtimeEnvironmentLighting;
            var directionality = root.GetProperty("directionality");
            if (directionality.GetProperty("error").ValueKind == JsonValueKind.String)
                loaded.DirectionalityNote = $"{file.Path}: the directionality of its precomputed realtime GI cannot be read, so its realtime lightmaps are drawn as non-directional ones: {directionality.GetProperty("error").GetString()}";
            else if (directionality.GetProperty("copies").GetBoolean())
                loaded.DirectionalityNote = $"{file.Path}: its precomputed realtime GI copies lightmap texels, whose directionality is not measured against the Editor (docs/REALTIME_GI.md).";
            BindProbes(loaded, root.GetProperty("probes"), file);
            if (!s_Registered)
            {
                s_Registered = true;
                PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.EnlightenRuntimeUpdate), Update);
            }
        }

        internal static void SceneUnloaded(int scene)
        {
            for (int i = s_Scenes.Count - 1; i >= 0; i--)
                if (s_Scenes[i].Scene == scene) Release(i);
        }

        internal static void Reset()
        {
            for (int i = s_Scenes.Count - 1; i >= 0; i--) Release(i);
            // A scene loaded alone takes nothing of a cube a script gave before it: its
            // environment is made from its ambient source, whatever that was before the load.
            EnvironmentChanged();
        }

        static void Release(int index)
        {
            var scene = s_Scenes[index];
            s_Scenes.RemoveAt(index);
            Unbind(scene);
            // Renderers of an unloaded scene are destroyed already: their entries go by scene.
            s_Released.Clear();
            foreach (var pair in s_Renderers)
                if (pair.Value.Scene == scene) s_Released.Add(pair.Key);
            foreach (var renderer in s_Released) s_Renderers.Remove(renderer);
            foreach (var atlas in scene.Atlases) Object.DestroyImmediate(atlas);
            foreach (var directionality in scene.Directionality)
                if (directionality != null) Object.DestroyImmediate(directionality);
            if (Native.IsReady) Native.Call("realtime-gi-release", JsonSerializer.Serialize(new { handle = scene.Handle }));
        }

        /// <summary>Renderer.UpdateGIMaterials: the renderer's system renders its materials again
        /// before its next update.</summary>
        internal static void MaterialsChanged(Renderer renderer)
        {
            if (renderer != null && s_Renderers.TryGetValue(renderer, out var at))
                at.Scene.Systems[at.System].MaterialsDirty = true;
        }

        /// <summary>DynamicGI.SetEmissive: the renderer emits <paramref name="color"/> (in the
        /// project's color space) for realtime GI instead of its materials' emission, until the
        /// scene unloads.</summary>
        internal static unsafe void SetEmissive(Renderer renderer, Color color)
        {
            if (renderer == null || !s_Renderers.TryGetValue(renderer, out var at)) return;
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            float* rgb = stackalloc float[3] { linear.r, linear.g, linear.b };
            NativeBuffer error;
            if (Native.Api.GiSetEmissive(at.Scene.Handle, at.System, at.Index, rgb, &error) != 0) Failed(at.Scene, error);
            at.Scene.Converged = false;
        }

        static unsafe void Failed(GIScene scene, NativeBuffer error)
        {
            string message;
            try { message = error.Length > 0 ? System.Text.Encoding.UTF8.GetString(error.Data, (int)error.Length) : "unknown error"; }
            finally { Native.Free(error); }
            Warn(message, $"precomputed realtime GI is not drawn: {message}");
            Unbind(scene);
        }

        /// <summary>A scene whose realtime GI cannot go on: its renderers draw as without it
        /// (light probes, no realtime lightmap).</summary>
        static void Unbind(GIScene scene)
        {
            scene.Failed = true;
            // Its light probes light nothing again (the ambient probe).
            if (scene.Probes != null)
            {
                var set = scene.Probes;
                scene.Probes = null;
                ReleaseProbes(set, LitProbeSets());
            }
            foreach (var system in scene.Systems)
            {
                foreach (var renderer in system.Renderers)
                    if (renderer != null)
                    {
                        renderer.m_RealtimeLightmap = null;
                        renderer.m_RealtimeDirectionality = null;
                        if (renderer is MeshRenderer mesh) mesh.enlightenVertexStream = null;
                    }
                DestroyStreams(system);
            }
        }

        static void DestroyStreams(GISystem system)
        {
            foreach (var stream in system.Streams) Object.DestroyImmediate(stream);
            system.Streams.Clear();
        }

        static readonly List<Light> s_FrameLights = new List<Light>();

        static unsafe void Update()
        {
            if (s_Scenes.Count == 0 || !Native.IsReady) return;
            int count = CollectLights();
            var frame = new GiFrame
            {
                IndirectScale = DynamicGI.indirectScale,
                Synchronous = Synchronous ? 1 : 0,
                BudgetMs = FrameBudgetMs,
                UpdateThreshold = DynamicGI.updateThreshold,
                Directional = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional ? 1 : 0,
            };
            UpdateEnvironment();
            foreach (var scene in s_Scenes)
            {
                if (scene.Failed) continue;
                for (int i = 0; i < scene.Systems.Length; i++)
                {
                    if (!scene.Systems[i].MaterialsDirty || scene.Systems[i].Failed) continue;
                    scene.Systems[i].MaterialsDirty = false;
                    RenderMaterials(scene, i);
                    if (scene.Failed) break;
                }
                if (scene.Failed) continue;
                // A scene entering or leaving the directional mode draws without directionality
                // until its atlases have theirs again (an earlier period's is not the light's now).
                if (scene.Directional != (frame.Directional != 0))
                {
                    scene.Directional = frame.Directional != 0;
                    for (int a = 0; a < scene.Atlases.Length; a++) BindDirectionality(scene, a, null);
                }
                if (frame.Directional != 0 && scene.DirectionalityNote != null)
                {
                    Warn("directionality:" + scene.Handle, scene.DirectionalityNote);
                    scene.DirectionalityNote = null;
                }
                NativeBuffer error;
                int result;
                if (scene.EnvironmentVersion != s_EnvironmentVersion)
                {
                    fixed (float* faces = EnvironmentOf(scene.BakedEnvironment))
                        result = Native.Api.GiSetEnvironment(scene.Handle, faces, s_Faces.Length, &error);
                    if (result != 0)
                    {
                        Failed(scene, error);
                        continue;
                    }
                    scene.EnvironmentVersion = s_EnvironmentVersion;
                }
                fixed (GiLight* lights = s_Lights)
                    result = Native.Api.GiUpdate(scene.Handle, &frame, lights, count, &error);
                if (result < 0)
                {
                    Failed(scene, error);
                    continue;
                }
                scene.Converged = (result & UpdateConverged) != 0;
                if ((result & UpdateChanged) != 0) Upload(scene, frame.Directional != 0);
                if ((result & UpdateProbes) != 0) UploadProbes(scene);
            }
        }

        /// <summary>Collects the frame's lights for realtime GI and returns how many. A render
        /// pipeline's lights are what its delegate makes of them
        /// (Experimental.GlobalIllumination.Lightmapping.SetDelegate): the Universal Render
        /// Pipeline's have an inverse-squared falloff, a linear intensity and their own inner
        /// spot angle (matches the Unity 6000.0.71f1 Editor, tests/projects/realtimegi
        /// `--pipeline urp`). Without one: the built-in pipeline's.</summary>
        internal static int CollectLights()
        {
            s_FrameLights.Clear();
            foreach (var light in Light.s_Enabled)
                if (Contributes(light)) s_FrameLights.Add(light);
            return GI.Lightmapping.HasCustomDelegate ? PipelineLights() : BuiltinLights();
        }

        /// <summary>One of the lights <see cref="CollectLights"/> collected.</summary>
        internal static GiLight CollectedLight(int index) => s_Lights[index];

        /// <summary>The frame's lights as the built-in pipeline gives them to realtime GI: the
        /// legacy falloff, the color scaled by the intensity and bounce intensity before its
        /// conversion to linear, LightmapperUtils' inner spot angle.</summary>
        static int BuiltinLights()
        {
            int count = 0;
            foreach (var light in s_FrameLights)
            {
                if (GraphicsSettings.lightsUseLinearIntensity)
                {
                    // How the built-in pipeline's realtime GI takes lights of linear intensity
                    // is not measured (a render pipeline's are its delegate's).
                    Warn("linear-intensity", "precomputed realtime GI with linear light intensity (GraphicsSettings.lightsUseLinearIntensity) in the built-in render pipeline is not supported yet: lights add no realtime indirect light.");
                    continue;
                }
                var t = light.transform;
                var color = light.ScaledColor(light.intensity * light.bounceIntensity, linear: true);
                Add(ref count, (int)light.type, t.position, t.forward, color.r, color.g, color.b, light.range, light.spotAngle,
                    GI.LightmapperUtils.ExtractInnerCone(light) * Mathf.Rad2Deg, GI.FalloffType.Legacy);
            }
            return count;
        }

        static Light[] s_Requests = Array.Empty<Light>();
        static Unity.Collections.NativeArray<GI.LightDataGI> s_Converted;

        /// <summary>The frame's lights as the registered delegate converts them: its indirect
        /// color (the color times intensity and bounce intensity), cone angles and falloff. A
        /// light it gives no indirect light, or none at all (InitNoBake), adds nothing.</summary>
        static int PipelineLights()
        {
            int requested = s_FrameLights.Count;
            if (s_Requests.Length != requested)
            {
                s_Requests = new Light[requested];
                if (s_Converted.IsCreated) s_Converted.Dispose();
                s_Converted = new Unity.Collections.NativeArray<GI.LightDataGI>(Math.Max(requested, 1), Unity.Collections.Allocator.Persistent);
            }
            for (int i = 0; i < requested; i++) s_Requests[i] = s_FrameLights[i];
            try
            {
                GI.Lightmapping.GetDelegate()(s_Requests, s_Converted);
            }
            catch (Exception e)
            {
                // A light the conversion rejects (a negative intensity, say) must not stop the
                // update: the frame has no lights, the environment still lights the scene.
                if (!s_Warned.Contains("pipeline-lights"))
                    Warn("pipeline-lights", $"precomputed realtime GI: the render pipeline's conversion of its lights failed, so lights add no realtime indirect light: {e.Message}");
                return 0;
            }
            int count = 0;
            for (int i = 0; i < requested; i++)
            {
                var data = s_Converted[i];
                float intensity = data.indirectColor.intensity;
                if (data.mode == GI.LightMode.Unknown || intensity == 0f) continue;
                int kind;
                switch (data.type)
                {
                    case GI.LightType.Directional: kind = (int)LightType.Directional; break;
                    case GI.LightType.Point: kind = (int)LightType.Point; break;
                    case GI.LightType.Spot: kind = (int)LightType.Spot; break;
                    default:
                        Warn("pipeline-light-type", "precomputed realtime GI: lights the render pipeline gives as other than directional, point or spot lights are not supported: they add no realtime indirect light.");
                        continue;
                }
                Add(ref count, kind, data.position, data.orientation * Vector3.forward,
                    data.indirectColor.red * intensity, data.indirectColor.green * intensity, data.indirectColor.blue * intensity,
                    data.range, data.coneAngle * Mathf.Rad2Deg, data.innerConeAngle * Mathf.Rad2Deg, data.falloff);
            }
            return count;
        }

        static void Add(ref int count, int kind, Vector3 position, Vector3 direction, float r, float g, float b, float range, float spotAngle, float innerSpotAngle,
            GI.FalloffType falloff)
        {
            if (count == s_Lights.Length) Array.Resize(ref s_Lights, count * 2);
            s_Lights[count++] = new GiLight
            {
                Kind = kind,
                PositionX = position.x, PositionY = position.y, PositionZ = position.z,
                DirectionX = direction.x, DirectionY = direction.y, DirectionZ = direction.z,
                R = r, G = g, B = b,
                Range = range,
                SpotAngle = spotAngle,
                InnerSpotAngle = innerSpotAngle,
                Falloff = (int)falloff,
            };
        }

        /// <summary>Whether a light adds to realtime GI: an enabled realtime light with bounce
        /// intensity (a mixed one when the scene bakes no global illumination: then it is lit as a
        /// realtime one). Area lights are baked only.</summary>
        static bool Contributes(Light light)
        {
            if (light == null || !light.isActiveAndEnabled || light.bounceIntensity <= 0f) return false;
            if (light.type != LightType.Point && light.type != LightType.Spot && light.type != LightType.Directional) return false;
            return light.lightmapBakeType switch
            {
                LightmapBakeType.Realtime => true,
                LightmapBakeType.Mixed => s_MixedAreRealtime,
                _ => false,
            };
        }

        /// <summary>The scene's light probes take the realtime lighting its probe sets give them,
        /// added to their baked lighting when they have any (matches the Unity 6000.0.71f1
        /// Editor's LightProbes.GetInterpolatedProbe, tests/projects/realtimegi: Room, and
        /// RoomBakedEnvironment for probes with both). Probes merged with other scenes'
        /// (additive loads) are unmeasured: they keep what they have, with a warning.</summary>
        static void BindProbes(GIScene scene, JsonElement reply, LoadedFile file)
        {
            int count = reply.GetProperty("count").GetInt32();
            if (reply.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                Warn("probes:" + file.Path, $"{file.Path}: light probes are not lit by its precomputed realtime GI: {error.GetString()}");
            if (count == 0) return;
            var set = ProbeTarget(scene.Scene, count, out var reason);
            if (set == null)
            {
                if (reason != null) Warn("probes-target:" + file.Path, $"{file.Path}: {reason}");
                return;
            }
            scene.Probes = set;
            scene.ProbeCoefficients = new float[27 * set.Positions.Length];
        }

        /// <summary>The probe set realtime GI lights for scene <paramref name="scene"/>: its own
        /// LightProbes' (not the current ones, which are another scene's on an additive load), or
        /// null with the reason (null too when the scene has no probes).</summary>
        internal static ProbeSet ProbeTarget(int scene, int count, out string reason)
        {
            reason = null;
            if (LightProbes.OwnOf(scene) is not LightProbes probes) return null;
            var set = probes.m_Self;
            if (count > set.Positions.Length)
                reason = $"its precomputed realtime GI lights {count} light probes where the scene has {set.Positions.Length}: they are not lit.";
            return reason == null ? set : null;
        }

        /// <summary>Whether a loaded scene's realtime GI takes the ambient source's environment
        /// (none loaded: yes, whoever asks).</summary>
        static bool TakesAmbientSource()
        {
            foreach (var scene in s_Scenes)
                if (!scene.BakedEnvironment) return true;
            return s_Scenes.Count == 0;
        }

        /// <summary>Gives the set <paramref name="read"/> probes' realtime coefficients (27
        /// each): its lighting, or what is added to its baked lighting, also to baked
        /// coefficients a script gave since (tests/projects/realtimegi
        /// roombakedenvironment-bakedset).</summary>
        internal static void ApplyProbes(ProbeSet set, float[] coefficients, int read)
        {
            if (set.Baked) set.BakedAlone ??= (SphericalHarmonicsL2[])set.Probes.Clone();
            var baked = set.BakedAlone;
            for (int i = 0; i < read; i++)
                for (int k = 0; k < 27; k++)
                    set.Probes[i][k / 9, k % 9] = coefficients[27 * i + k] + (baked != null ? baked[i][k / 9, k % 9] : 0f);
            set.Realtime = true;
        }

        /// <summary>Realtime GI stops lighting the set: it has its baked lighting alone again
        /// (without any, callers take the ambient probe), unless another loaded scene's realtime
        /// GI lights the same set (one Lighting Data loaded twice).</summary>
        internal static void ReleaseProbes(ProbeSet set, IEnumerable<ProbeSet> stillLit)
        {
            foreach (var other in stillLit)
                if (other == set) return;
            set.Realtime = false;
            if (set.BakedAlone == null) return;
            set.Probes = set.BakedAlone;
            set.BakedAlone = null;
        }

        static IEnumerable<ProbeSet> LitProbeSets()
        {
            foreach (var other in s_Scenes)
                if (!other.Failed && other.Probes != null) yield return other.Probes;
        }

        /// <summary>The light probes' coefficients after an update.</summary>
        static unsafe void UploadProbes(GIScene scene)
        {
            if (scene.Probes is not ProbeSet set) return;
            int read;
            fixed (float* coefficients = scene.ProbeCoefficients)
                read = Native.Api.GiReadProbes(scene.Handle, coefficients, scene.ProbeCoefficients.Length);
            if (read <= 0) return;
            ApplyProbes(set, scene.ProbeCoefficients, read);
            if (LightmapSettings.lightProbes is LightProbes current && current.m_Set != set)
                Warn("probes-not-current", "light probes of a scene loaded beside others are lit by its precomputed realtime GI but are not the current light probes, which keep their own lighting (unmeasured).");
        }

        static unsafe void Upload(GIScene scene, bool directional)
        {
            for (int a = 0; a < scene.Atlases.Length; a++)
            {
                var atlas = scene.Atlases[a];
                int values = atlas.width * atlas.height * 4;
                if (s_Texels.Length < values) s_Texels = new ushort[values];
                int written;
                fixed (ushort* texels = s_Texels)
                    written = Native.Api.GiReadAtlas(scene.Handle, a, texels, values);
                if (written < 0) continue;
                atlas.SetPixelData(s_Texels, 0);
                atlas.Apply(false);
                if (directional) UploadDirectionality(scene, a, values);
            }
        }

        /// <summary>An atlas's directionality (unity_DynamicDirectionality), once every system of
        /// the atlas has solved its own since the lightmaps became directional (nothing is read
        /// before): the renderers of the atlas's systems draw with it from then on, while the
        /// lightmaps mode stays directional.</summary>
        static unsafe void UploadDirectionality(GIScene scene, int index, int bytes)
        {
            if (s_Directions.Length < bytes) s_Directions = new byte[bytes];
            int written;
            fixed (byte* texels = s_Directions)
                written = Native.Api.GiReadDirectionality(scene.Handle, index, texels, bytes);
            if (written <= 0) return;
            var texture = scene.Directionality[index];
            if (texture == null)
            {
                var atlas = scene.Atlases[index];
                texture = scene.Directionality[index] = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false, true)
                {
                    name = "Realtime Directionality",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }
            texture.SetPixelData(s_Directions, 0);
            texture.Apply(false);
            BindDirectionality(scene, index, texture);
        }

        /// <summary>Gives the renderers of an atlas's systems its directionality
        /// (<paramref name="texture"/>), or none (null).</summary>
        static void BindDirectionality(GIScene scene, int index, Texture2D texture)
        {
            if (scene.DirectionalityBound[index] == (texture != null)) return;
            scene.DirectionalityBound[index] = texture != null;
            foreach (var system in scene.Systems)
            {
                if (system.Atlas != index || system.Failed) continue;
                foreach (var renderer in system.Renderers)
                    if (renderer != null) renderer.m_RealtimeDirectionality = texture;
            }
        }

        /// <summary>The built-in Lightmap Parameters asset scenes use by default ("Default-Medium",
        /// unity_builtin_extra).</summary>
        const long DefaultParametersFileId = 15204;
        const string BuiltinExtraGuid = "0000000000000000f000000000000000";

        static bool OwnParameters(ObjectRef parameters) =>
            !parameters.IsNull && !(parameters.FileId == DefaultParametersFileId && parameters.Guid == BuiltinExtraGuid);

        /// <summary>Each renderer of the system gets its realtime lightmap UVs as its
        /// <see cref="MeshRenderer.enlightenVertexStream"/> (made natively, checked against the
        /// system's precomputed layout: Lightmap Parameters that change the charts, such as their
        /// resolution, fail that check). A renderer with Preserve UVs keeps its mesh's lightmap
        /// UV charts; one without (Optimize Realtime UVs in the Inspector) gets the charts the
        /// precompute makes within its Max Distance and Max Angle. Returns why they could not be
        /// made, or null.</summary>
        static unsafe string RealtimeUVs(GIScene scene, int index, GISystem system, LightingSettings settings)
        {
            float resolution = settings != null ? settings.indirectResolution : 2f;
            for (int r = 0; r < system.Renderers.Length; r++)
            {
                if (!(system.Renderers[r] is MeshRenderer renderer)) return $"its renderer {r} is missing or not a MeshRenderer (unsupported)";
                bool automatic = MakesAutomaticCharts(renderer);
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) return $"'{renderer.name}' has no mesh";
                if (automatic && AutomaticChartsRefusal(renderer, mesh) is string refusal) return refusal;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) return $"'{renderer.name}' has a submesh of {mesh.GetTopology(sub)}";
                float scale = renderer.scaleInLightmap * resolution;
                if (!(scale > 0f)) return $"'{renderer.name}' has no realtime lightmap resolution";
                var vertices = mesh.vertices;
                // The input of realtime lightmap UVs is Mesh.uv3 when the mesh has it, else the
                // baked lightmap UVs (Mesh.uv2), else Mesh.uv (Unity 6000.0 Manual, "Generate
                // lightmap UVs"; tests/projects/realtimegi RoomCharts, Channels).
                var source = mesh.uv3;
                if (source.Length != vertices.Length) source = mesh.uv2;
                if (source.Length != vertices.Length) source = mesh.uv;
                if (source.Length != vertices.Length) return $"'{renderer.name}' has no lightmap UVs";
                var matrix = renderer.transform.localToWorldMatrix;
                var positions = new float[3 * vertices.Length];
                var uvs = new float[2 * vertices.Length];
                float[] shading = null;
                float[] instance = null;
                if (automatic)
                {
                    // The precompute's charts are made of the mesh under the instance's
                    // rotation and scale without its world rotation (WorldScale), which the
                    // native side applies as the precompute does.
                    instance = WorldScale(renderer.transform);
                    var normals = mesh.normals;
                    shading = new float[3 * vertices.Length];
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        positions[3 * v] = vertices[v].x; positions[3 * v + 1] = vertices[v].y; positions[3 * v + 2] = vertices[v].z;
                        shading[3 * v] = normals[v].x; shading[3 * v + 1] = normals[v].y; shading[3 * v + 2] = normals[v].z;
                        uvs[2 * v] = source[v].x; uvs[2 * v + 1] = source[v].y;
                    }
                }
                else
                {
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        var p = matrix.MultiplyPoint3x4(vertices[v]);
                        positions[3 * v] = p.x; positions[3 * v + 1] = p.y; positions[3 * v + 2] = p.z;
                        uvs[2 * v] = source[v].x; uvs[2 * v + 1] = source[v].y;
                    }
                }
                var triangles = mesh.triangles;
                var output = new float[2 * vertices.Length];
                NativeBuffer error;
                int status;
                fixed (float* pp = positions)
                fixed (float* np = shading)
                fixed (float* mp = instance)
                fixed (float* up = uvs)
                fixed (int* tp = triangles)
                fixed (float* op = output)
                    status = automatic
                        ? Native.Api.GiAutomaticUvs(scene.Handle, index, r, pp, np, up, vertices.Length, (uint*)tp, triangles.Length,
                            1f / scale, renderer.m_MinimumChartSize, renderer.m_AutoUVMaxDistance, renderer.m_AutoUVMaxAngle,
                            renderer.m_IgnoreNormalsForChartDetection ? 1 : 0, mp, op, &error)
                        : Native.Api.GiRealtimeUvs(scene.Handle, index, r, pp, up, vertices.Length, (uint*)tp, triangles.Length,
                            1f / scale, renderer.m_MinimumChartSize, op, &error);
                if (status != 0)
                {
                    string message;
                    try { message = error.Length > 0 ? System.Text.Encoding.UTF8.GetString(error.Data, (int)error.Length) : "unknown error"; }
                    finally { Native.Free(error); }
                    // Lightmap Parameters of its own may change its charts (their resolution, say).
                    if (OwnParameters(renderer.m_LightmapParameters))
                        message += " (the renderer has Lightmap Parameters of its own)";
                    return $"'{renderer.name}': {message}";
                }
                var realtime = new List<Vector2>(vertices.Length);
                for (int v = 0; v < vertices.Length; v++) realtime.Add(new Vector2(output[2 * v], output[2 * v + 1]));
                Mesh stream;
                using (Unbound.Runtime.ObjectRegistry.AsAssets())
                    stream = new Mesh { name = mesh.name + " (realtime lightmap UVs)", hideFlags = HideFlags.HideAndDontSave };
                stream.vertices = vertices;
                stream.SetUVs(2, realtime);
                renderer.enlightenVertexStream = stream;
                system.Streams.Add(stream);
            }
            return null;
        }

        /// <summary>Whether the precompute makes the renderer's realtime charts (Optimize Realtime
        /// UVs) rather than keeping its mesh's: not with Preserve UVs, nor with a Max Distance or
        /// Max Angle of zero, with which it keeps them too (tests/projects/realtimegi RoomTurned,
        /// StepNoDistance; a scratch bake of the same step with Max Angle 0 kept them as
        /// well).</summary>
        internal static bool MakesAutomaticCharts(MeshRenderer renderer) =>
            !renderer.m_PreserveUVs && renderer.m_AutoUVMaxDistance > 0f && renderer.m_AutoUVMaxAngle > 0f;

        /// <summary>The rotation of a quaternion, its columns scaled (a 3 x 3 matrix, column by
        /// column), in the precompute's order of operations.</summary>
        static float[] RotationScaled(Quaternion q, Vector3 s)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            return new[]
            {
                s.x * ((z * -2f * z + y * -2f * y) + 1f),
                s.x * ((z * 2f * w + y * 2f * x) + 0f),
                s.x * ((z * 2f * x + y * -2f * w) + 0f),
                s.y * ((x * 2f * y + z * -2f * w) + 0f),
                s.y * ((x * -2f * x + z * -2f * z) + 1f),
                s.y * ((x * 2f * w + z * 2f * y) + 0f),
                s.z * ((y * 2f * w + x * 2f * z) + 0f),
                s.z * ((y * 2f * z + x * -2f * w) + 0f),
                s.z * ((y * -2f * y + x * -2f * x) + 1f),
            };
        }

        /// <summary><paramref name="a"/> times <paramref name="b"/>, each entry summed as
        /// (second + third) + first term.</summary>
        static float[] Product(float[] a, float[] b)
        {
            var o = new float[9];
            for (int c = 0; c < 3; c++)
                for (int r = 0; r < 3; r++)
                    o[3 * c + r] = (b[3 * c + 1] * a[3 + r] + b[3 * c + 2] * a[6 + r]) + b[3 * c] * a[r];
            return o;
        }

        /// <summary>The geometry matrix the Unity 6000.0.71f1 Editor gives its precompute for an
        /// instance with Optimize Realtime UVs: the transform's rotation and scale with its world
        /// rotation taken out (the inverse of the world rotation times the levels' rotations
        /// and scales), a 3 x 3 matrix column by column. Single precision, in the order that
        /// gives the Editor's values to the last bit (tests/projects/realtimegi RoomTurned); a
        /// level whose scale is negative along an axis turns the rotations below it as a mirror
        /// does.</summary>
        internal static float[] WorldScale(Transform transform)
        {
            var rotation = transform.localRotation;
            float x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w;
            var scaled = RotationScaled(rotation, transform.localScale);
            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                var p = parent.localRotation;
                var s = parent.localScale;
                float sx = float.IsNegative(s.x) ? -1f : 1f, sy = float.IsNegative(s.y) ? -1f : 1f, sz = float.IsNegative(s.z) ? -1f : 1f;
                float xa = sy * sz < 0f ? -x : x, ya = sz * sx < 0f ? -y : y, za = sy * sx < 0f ? -z : z;
                float nx = -(((ya * p.z - za * p.y) - xa * p.w) - w * p.x);
                float ny = -(((za * p.x - xa * p.z) - ya * p.w) - w * p.y);
                float nz = -(((xa * p.y - za * p.w) - w * p.z) - ya * p.x);
                float nw = ((w * p.w - xa * p.x) - za * p.z) - ya * p.y;
                x = nx; y = ny; z = nz; w = nw;
                scaled = Product(RotationScaled(p, s), scaled);
            }
            return Product(RotationScaled(new Quaternion(-x, -y, -z, w), Vector3.one), scaled);
        }

        /// <summary>Why the charts of a renderer with Optimize Realtime UVs are not made, or
        /// null: what is not measured (a zero or mirrored scale, no normals). (The precompute
        /// keys the geometry it makes the charts of by the mesh and the instance matrix, so
        /// renderers share charts when both are the same: tests/projects/realtimegi RoomTurned,
        /// SharedTurned and SharedFlat.)</summary>
        internal static string AutomaticChartsRefusal(MeshRenderer renderer, Mesh mesh)
        {
            var size = renderer.transform.lossyScale;
            if (!(size.x > 0f && size.y > 0f && size.z > 0f))
                return $"'{renderer.name}' has Optimize Realtime UVs and a scale of {size} (a zero or mirrored scale is not measured)";
            if (mesh.normals.Length != mesh.vertexCount)
                return $"'{renderer.name}' has Optimize Realtime UVs and no normals";
            return null;
        }

        static readonly Dictionary<int, object> s_MetaProperties = new Dictionary<int, object>();
        static readonly int s_LightmapST = Shader.PropertyToID("unity_LightmapST");
        static readonly int s_DynamicLightmapST = Shader.PropertyToID("unity_DynamicLightmapST");

        /// <summary>A system's albedo and emission textures, its renderers' meta passes drawn at
        /// their realtime lightmap positions within the system (as the Editor renders them: filled,
        /// then again as wireframe so that texels the triangles only touch are covered too), read
        /// back and handed to the system.</summary>
        static unsafe void RenderMaterials(GIScene scene, int index)
        {
            var system = scene.Systems[index];
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var albedo = RenderTexture.GetTemporary(system.Width, system.Height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var emission = RenderTexture.GetTemporary(system.Width, system.Height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            try
            {
                DrawMeta(scene, system, albedo, emissive: false, linear);
                DrawMeta(scene, system, emission, emissive: true, linear);
                var albedoBytes = Native.ReadPixels(albedo.EnsureCreated(), 0, 0, system.Width, system.Height, false);
                var emissionBytes = Native.ReadPixels(emission.EnsureCreated(), 0, 0, system.Width, system.Height, true);
                if (albedoBytes == null || emissionBytes == null)
                {
                    Warn("headless", "precomputed realtime GI needs a GPU for its materials' meta passes: it is not drawn.");
                    Unbind(scene);
                    return;
                }
                NativeBuffer error;
                fixed (byte* p = albedoBytes)
                    if (Native.Api.GiSetMaterial(scene.Handle, index, 0, p, albedoBytes.Length, &error) != 0) { Failed(scene, error); return; }
                fixed (byte* p = emissionBytes)
                    if (Native.Api.GiSetMaterial(scene.Handle, index, 1, p, emissionBytes.Length, &error) != 0) { Failed(scene, error); return; }
                scene.Converged = false;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(albedo);
                RenderTexture.ReleaseTemporary(emission);
            }
        }

        static void DrawMeta(GIScene scene, GISystem system, RenderTexture target, bool emissive, bool linear)
        {
            var saved = GfxStream.Current;
            bool wireframe = GL.wireframe;
            GfxStream.SetTarget(target.EnsureCreated());
            GfxStream.Viewport(new Rect(0, 0, system.Width, system.Height));
            GfxStream.Clear(GfxStream.ClearColor | GfxStream.ClearDepth, emissive ? new Color(0f, 0f, 0f, 1f) : new Color(0f, 0f, 0f, 0f));
            GfxStream.Camera(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            GfxStream.GlobalVector("unity_MetaVertexControl", new Vector4(0f, 1f, 0f, 0f));
            GfxStream.GlobalVector("unity_MetaFragmentControl", emissive ? new Vector4(0f, 1f, 0f, 0f) : new Vector4(1f, 0f, 0f, 0f));
            GfxStream.GlobalFloat("unity_OneOverOutputBoost", 1f / scene.AlbedoBoost);
            GfxStream.GlobalFloat("unity_MaxOutputValue", MaxAlbedo);
            GfxStream.GlobalFloat("unity_UseLinearSpace", linear ? 1f : 0f);
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    GL.wireframe = pass == 1;
                    for (int r = 0; r < system.Renderers.Length; r++)
                        DrawRenderer(system.Renderers[r], system.ScaleOffsets[r], emissive);
                }
            }
            finally
            {
                GL.wireframe = wireframe;
                GfxStream.Restore(saved);
            }
        }

        static void DrawRenderer(Renderer renderer, Vector4 scaleOffset, bool emissive)
        {
            if (renderer == null) return;
            var mesh = (renderer as MeshRenderer)?.RealtimeGIMesh();
            if (mesh == null)
            {
                Warn("mesh:" + renderer.name, $"'{renderer.name}' ({renderer.GetType().Name}) has no mesh Unbound can render for precomputed realtime GI: it adds no albedo or emission.");
                return;
            }
            var materials = renderer.sharedMaterials;
            s_MetaProperties.Clear();
            s_MetaProperties[s_LightmapST] = scaleOffset;
            s_MetaProperties[s_DynamicLightmapST] = scaleOffset;
            int submeshes = Math.Min(materials.Length, mesh.subMeshCount);
            for (int i = 0; i < submeshes; i++)
            {
                var material = materials[i];
                if (material == null) continue;
                // Emission counts for realtime GI when the material says so.
                if (emissive && (material.globalIlluminationFlags & (MaterialGlobalIlluminationFlags.RealtimeEmissive | MaterialGlobalIlluminationFlags.EmissiveIsBlack))
                    != MaterialGlobalIlluminationFlags.RealtimeEmissive)
                    continue;
                int pass = MetaPass(material);
                if (pass < 0)
                {
                    Warn("meta:" + material.name, $"GI: setting up material '{material.name}' failed: its shader has no Meta pass. '{renderer.name}' does not affect precomputed realtime GI.");
                    continue;
                }
                GfxStream.Draw(mesh.Sync(), i, material.Sync(), pass, renderer.localToWorldMatrix, Color.white, properties: s_MetaProperties);
            }
        }

        static int MetaPass(Material material)
        {
            var info = ShaderPassInfo.Of(material.shader);
            if (info == null) return -1;
            for (int i = 0; i < info.Passes.Count; i++)
                if (info.Passes[i].Tags.TryGetValue("LightMode", out var mode) && string.Equals(mode, "Meta", StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }
}

namespace UnityEngine
{
    public static partial class RendererExtensions
    {
        /// <summary>Schedules the renderer's system to render its materials' albedo and emission
        /// again (an albedo change waits for it: measured, tests/projects/realtimegi
        /// room-albedo-nocall and room-albedo-call).</summary>
        public static void UpdateGIMaterials(this Renderer renderer) => RealtimeGI.MaterialsChanged(renderer);
    }
}
