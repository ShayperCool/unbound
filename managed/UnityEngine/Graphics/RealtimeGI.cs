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
        internal float InnerSpotAngle;
    }

    /// <summary>One frame's precomputed realtime GI inputs (`realtime_gi::GiFrame`).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GiFrame
    {
        internal float IndirectScale;
        internal float EnvironmentR, EnvironmentG, EnvironmentB;
        internal int Synchronous;
        internal float BudgetMs;
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
    /// (unity_DynamicLightmap, <see cref="BakedLighting.Apply"/>).
    /// </summary>
    internal static class RealtimeGI
    {
        const int UpdateChanged = 1, UpdateConverged = 2;

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
            internal float AlbedoBoost = 1f;
            internal bool Converged, Failed;
        }

        static readonly List<GIScene> s_Scenes = new List<GIScene>();
        static readonly Dictionary<Renderer, (GIScene Scene, int System, int Index)> s_Renderers =
            new Dictionary<Renderer, (GIScene, int, int)>();
        static GiLight[] s_Lights = new GiLight[8];
        static readonly List<Renderer> s_Released = new List<Renderer>();
        static ushort[] s_Texels = Array.Empty<ushort>();
        static readonly HashSet<string> s_Warned = new HashSet<string>(StringComparer.Ordinal);
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
                // A system is drawn when every renderer of it has its realtime lightmap UVs.
                var failure = RealtimeUVs(loaded, systems.Count, system, settings);
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
                systems.Add(system);
            }
            loaded.Systems = systems.ToArray();
            s_Scenes.Add(loaded);
            if (LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional)
                Warn("directional:" + file.Path, $"{file.Path}: directional realtime lightmaps are not supported yet: its precomputed realtime GI is drawn without directionality.");
            if (LightmapSettings.lightProbes is LightProbes probes && probes.count > 0)
                Warn("probes:" + file.Path, $"{file.Path}: light probes lit by precomputed realtime GI are not supported yet: probe-lit objects keep the ambient light.");
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
            foreach (var system in scene.Systems)
            {
                foreach (var renderer in system.Renderers)
                    if (renderer != null)
                    {
                        renderer.m_RealtimeLightmap = null;
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
            int count = 0;
            bool linearIntensity = GraphicsSettings.lightsUseLinearIntensity;
            foreach (var light in Light.s_Enabled)
            {
                if (!Contributes(light)) continue;
                if (linearIntensity)
                {
                    // Render pipelines scale the linear color by the intensity; how precomputed
                    // realtime GI takes such a light is not measured.
                    Warn("linear-intensity", "precomputed realtime GI with linear light intensity (GraphicsSettings.lightsUseLinearIntensity, render pipelines) is not supported yet: lights add no realtime indirect light.");
                    continue;
                }
                if (count == s_Lights.Length) Array.Resize(ref s_Lights, count * 2);
                var t = light.transform;
                var position = t.position;
                var direction = t.forward;
                var color = light.ScaledColor(light.intensity * light.bounceIntensity, linear: true);
                s_Lights[count++] = new GiLight
                {
                    Kind = (int)light.type,
                    PositionX = position.x, PositionY = position.y, PositionZ = position.z,
                    DirectionX = direction.x, DirectionY = direction.y, DirectionZ = direction.z,
                    R = color.r, G = color.g, B = color.b,
                    Range = light.range,
                    SpotAngle = light.spotAngle,
                    InnerSpotAngle = light.innerSpotAngle,
                };
            }
            var frame = new GiFrame
            {
                IndirectScale = DynamicGI.indirectScale,
                Synchronous = Synchronous ? 1 : 0,
                BudgetMs = FrameBudgetMs,
            };
            var environment = Environment();
            frame.EnvironmentR = environment.r;
            frame.EnvironmentG = environment.g;
            frame.EnvironmentB = environment.b;
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
                NativeBuffer error;
                int result;
                fixed (GiLight* lights = s_Lights)
                    result = Native.Api.GiUpdate(scene.Handle, &frame, lights, count, &error);
                if (result < 0)
                {
                    Failed(scene, error);
                    continue;
                }
                scene.Converged = (result & UpdateConverged) != 0;
                if ((result & UpdateChanged) != 0) Upload(scene);
            }
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

        /// <summary>The environment's linear radiance: the flat ambient color (other ambient modes
        /// are not supported yet: they light nothing, with a warning).</summary>
        static AmbientMode s_WarnedAmbient = (AmbientMode)(-1);

        static Color Environment()
        {
            var mode = RenderSettings.ambientMode;
            if (mode == AmbientMode.Flat) return RenderSettings.ambientLight.linear;
            if (mode != s_WarnedAmbient)
            {
                s_WarnedAmbient = mode;
                Warn("environment:" + mode,
                    $"precomputed realtime GI takes only a flat ambient color as its environment yet; with ambient mode {mode} the environment adds no realtime indirect light.");
            }
            return Color.black;
        }

        static unsafe void Upload(GIScene scene)
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
            }
        }

        /// <summary>The built-in Lightmap Parameters asset scenes use by default ("Default-Medium",
        /// unity_builtin_extra): the only parameters the realtime lightmap UVs are measured with.</summary>
        const long DefaultParametersFileId = 15204;
        const string BuiltinExtraGuid = "0000000000000000f000000000000000";

        static bool DefaultParameters(ObjectRef parameters) =>
            parameters.IsNull || (parameters.FileId == DefaultParametersFileId && parameters.Guid == BuiltinExtraGuid);

        /// <summary>Each renderer of the system gets its realtime lightmap UVs as its
        /// <see cref="MeshRenderer.enlightenVertexStream"/> (made natively, checked against the
        /// system's precomputed layout). Returns why they could not be made, or null.</summary>
        static unsafe string RealtimeUVs(GIScene scene, int index, GISystem system, LightingSettings settings)
        {
            float resolution = settings != null ? settings.indirectResolution : 2f;
            if (settings != null && !DefaultParameters(settings.LightmapParametersRef))
                return "the scene's Lightmap Parameters are not the default ones (unmeasured for realtime lightmap UVs)";
            for (int r = 0; r < system.Renderers.Length; r++)
            {
                if (!(system.Renderers[r] is MeshRenderer renderer)) return $"its renderer {r} is missing or not a MeshRenderer (unsupported)";
                if (!renderer.m_PreserveUVs)
                    return $"'{renderer.name}' has Preserve UVs off: its realtime lightmap UVs need the precompute's automatic charts, which Unbound does not make yet";
                if (!DefaultParameters(renderer.m_LightmapParameters))
                    return $"'{renderer.name}' has its own Lightmap Parameters (unmeasured for realtime lightmap UVs)";
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) return $"'{renderer.name}' has no mesh";
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) return $"'{renderer.name}' has a submesh of {mesh.GetTopology(sub)}";
                float scale = renderer.scaleInLightmap * resolution;
                if (!(scale > 0f)) return $"'{renderer.name}' has no realtime lightmap resolution";
                var vertices = mesh.vertices;
                var source = mesh.uv2;
                if (source.Length != vertices.Length) source = mesh.uv;
                if (source.Length != vertices.Length) return $"'{renderer.name}' has no lightmap UVs";
                var matrix = renderer.transform.localToWorldMatrix;
                var positions = new float[3 * vertices.Length];
                var uvs = new float[2 * vertices.Length];
                for (int v = 0; v < vertices.Length; v++)
                {
                    var p = matrix.MultiplyPoint3x4(vertices[v]);
                    positions[3 * v] = p.x; positions[3 * v + 1] = p.y; positions[3 * v + 2] = p.z;
                    uvs[2 * v] = source[v].x; uvs[2 * v + 1] = source[v].y;
                }
                var triangles = mesh.triangles;
                var output = new float[2 * vertices.Length];
                NativeBuffer error;
                int status;
                fixed (float* pp = positions)
                fixed (float* up = uvs)
                fixed (int* tp = triangles)
                fixed (float* op = output)
                    status = Native.Api.GiRealtimeUvs(scene.Handle, index, r, pp, up, vertices.Length, (uint*)tp, triangles.Length,
                        1f / scale, renderer.m_MinimumChartSize, op, &error);
                if (status != 0)
                {
                    string message;
                    try { message = error.Length > 0 ? System.Text.Encoding.UTF8.GetString(error.Data, (int)error.Length) : "unknown error"; }
                    finally { Native.Free(error); }
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
