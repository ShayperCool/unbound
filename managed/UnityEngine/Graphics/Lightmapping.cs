using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A lightmap: its color, directional and shadowmask textures.</summary>
    public sealed partial class LightmapData
    {
        internal Texture2D m_Light;
        internal Texture2D m_Dir;
        internal Texture2D m_ShadowMask;

        public LightmapData() { }

        [global::System.@ObsoleteAttribute("Use lightmapColor property (UnityUpgradable) -> lightmapColor", false)]
        public Texture2D lightmapLight { get => m_Light; set => m_Light = value; }
        public Texture2D lightmapColor { get => m_Light; set => m_Light = value; }
        public Texture2D lightmapDir { get => m_Dir; set => m_Dir = value; }
        public Texture2D shadowMask { get => m_ShadowMask; set => m_ShadowMask = value; }
        [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)] [global::System.@ObsoleteAttribute("Property LightmapData.lightmap has been deprecated. Use LightmapData.lightmapColor instead (UnityUpgradable) -> lightmapColor", true)]
        public Texture2D lightmap { get => m_Light; set => m_Light = value; }
        [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)] [global::System.@ObsoleteAttribute("Property LightmapData.lightmapFar has been deprecated. Use LightmapData.lightmapColor instead (UnityUpgradable) -> lightmapColor", true)]
        public Texture2D lightmapFar { get => m_Light; set => m_Light = value; }
        [global::System.@ObsoleteAttribute("Property LightmapData.lightmapNear has been deprecated. Use LightmapData.lightmapDir instead (UnityUpgradable) -> lightmapDir", true)] [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)]
        public Texture2D lightmapNear { get => m_Dir; set => m_Dir = value; }

        internal LightmapData Copy() => new LightmapData { m_Light = m_Light, m_Dir = m_Dir, m_ShadowMask = m_ShadowMask };
    }

    /// <summary>The loaded scenes' lightmaps and light probes. The getter hands out copies, the
    /// setter takes a copy, as Unity's do.</summary>
    public sealed partial class LightmapSettings : Object
    {
        static LightmapData[] s_Lightmaps = Array.Empty<LightmapData>();
        static LightmapsMode s_Mode = LightmapsMode.CombinedDirectional;
        static LightProbes s_LightProbes;

        public static LightmapData[] lightmaps
        {
            get => Array.ConvertAll(s_Lightmaps, l => l?.Copy());
            set => s_Lightmaps = value == null ? Array.Empty<LightmapData>() : Array.ConvertAll(value, l => l?.Copy());
        }

        public static LightmapsMode lightmapsMode { get => s_Mode; set => s_Mode = value; }

        public static LightProbes lightProbes
        {
            get => s_LightProbes;
            set
            {
                s_LightProbes = value;
                LightProbes.RaiseUpdated();
            }
        }

        /// <summary>A loaded scene's probes become the current ones: no lightProbesUpdated
        /// (measured: none after a scene load, tests/projects/lightprobes).</summary>
        internal static void SetLightProbes(LightProbes probes) => s_LightProbes = probes;

        internal static void Reset()
        {
            s_Lightmaps = Array.Empty<LightmapData>();
            s_Mode = LightmapsMode.CombinedDirectional;
            s_LightProbes = null;
            LightProbes.ResetScenes();
        }

        /// <summary>The lightmap a renderer draws with (no copy), or null.</summary>
        internal static LightmapData Of(int index) =>
            index >= 0 && index < s_Lightmaps.Length ? s_Lightmaps[index] : null;

        internal static int Count => s_Lightmaps.Length;

        /// <summary>unity_Lightmap_HDR, boxed: how the first lightmap's color texture stores its
        /// radiance decides it for every lightmap (<see cref="HdrDecode"/>; matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/lightmapencoding mixed/lightmapDecode).</summary>
        internal static object DecodeValues
        {
            get => HdrDecode.Of(s_Lightmaps.Length > 0 ? s_Lightmaps[0]?.m_Light : null);
        }

        internal static void Append(LightmapData[] maps)
        {
            var all = new LightmapData[s_Lightmaps.Length + maps.Length];
            s_Lightmaps.CopyTo(all, 0);
            maps.CopyTo(all, s_Lightmaps.Length);
            s_Lightmaps = all;
        }
    }

    /// <summary>A group of light probe positions for the bake; at runtime the baked probes
    /// (LightmapSettings.lightProbes) light the scene.</summary>
    [ClassId(220)]
    public sealed partial class LightProbeGroup : Behaviour
    {
        Vector3[] m_SourcePositions = Compat.DefaultConstructorStateMeasured ? NewGroupPositions() : Array.Empty<Vector3>();
        bool m_Dering = true;

        /// <summary>The probes a group added in the Editor starts with: the corners of a cube of
        /// half-size 1, x then y then z from + to - (Unity 6000.0.71f1, tests/projects/defaultcontrols
        /// lpg.added; a loaded group keeps its own, also none; other releases unmeasured, none).</summary>
        static Vector3[] NewGroupPositions() => new[]
        {
            new Vector3(1, 1, 1), new Vector3(1, 1, -1), new Vector3(1, -1, 1), new Vector3(1, -1, -1),
            new Vector3(-1, 1, 1), new Vector3(-1, 1, -1), new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
        };

        public LightProbeGroup() { }

        public Vector3[] probePositions
        {
            get => (Vector3[])m_SourcePositions.Clone();
            set => m_SourcePositions = value == null ? Array.Empty<Vector3>() : (Vector3[])value.Clone();
        }

        public bool dering { get => m_Dering; set => m_Dering = value; }

        /// <summary>Whether a bake takes this group's probes: an enabled group of an active
        /// object, not destroyed (tests/projects/bakedbuffers `ProbesSky`: a disabled group and
        /// one of an inactive object have no probes in the Editor's bake).</summary>
        internal static bool TakenByBake(Object value, out LightProbeGroup group)
        {
            group = value as LightProbeGroup;
            return group != null && group.enabled && group.gameObject.activeInHierarchy;
        }

        /// <summary>The group's probe positions in world space.</summary>
        internal Vector3[] WorldProbePositions()
        {
            var points = new Vector3[m_SourcePositions.Length];
            for (int i = 0; i < points.Length; i++) points[i] = transform.TransformPoint(m_SourcePositions[i]);
            return points;
        }

        /// <summary>Instantiate copies the probes, also none (tests/projects/defaultcontrols
        /// lpg.instantiated*).</summary>
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (LightProbeGroup)source;
            m_Enabled = s.m_Enabled;
            m_SourcePositions = (Vector3[])s.m_SourcePositions.Clone();
            m_Dering = s.m_Dering;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            var positions = new List<Vector3>();
            foreach (var p in body.Get("m_SourcePositions")?.Seq() ?? Array.Empty<YNode>())
                positions.Add(new Vector3(p.Float("x"), p.Float("y"), p.Float("z")));
            m_SourcePositions = positions.ToArray();
            m_Dering = body.Bool("m_Dering", true);
        }
    }

    /// <summary>
    /// A scene's baked lighting (its LightingData asset): the lightmaps join
    /// LightmapSettings.lightmaps, each lightmapped renderer gets its lightmap index and scale and
    /// offset, each baked light its baking output, and the probes become
    /// LightmapSettings.lightProbes. What a renderer draws with is <see cref="Apply"/>'s.
    /// </summary>
    internal static class BakedLighting
    {
        /// <summary>Reads lighting data reply <paramref name="root"/> of the scene with handle
        /// <paramref name="scene"/>; <paramref name="lightingData"/> is the scene's m_LightingDataAsset
        /// reference the reply was read for, the identity of its probes object (none: no reuse).</summary>
        internal static void Read(JsonElement root, LoadedFile file, int scene = 0, ObjectRef lightingData = default)
        {
            int offset = LightmapSettings.Count;
            if (root.TryGetProperty("lightmapsMode", out var mode)) LightmapSettings.lightmapsMode = (LightmapsMode)mode.GetInt32();
            if (root.TryGetProperty("lightmaps", out var lightmaps) && lightmaps.GetArrayLength() > 0)
            {
                var maps = new List<LightmapData>();
                foreach (var m in lightmaps.EnumerateArray())
                    maps.Add(new LightmapData { m_Light = Texture(m, "color"), m_Dir = Texture(m, "dir"), m_ShadowMask = Texture(m, "shadowMask") });
                LightmapSettings.Append(maps.ToArray());
            }
            if (file != null && root.TryGetProperty("renderers", out var renderers))
            {
                foreach (var r in renderers.EnumerateArray())
                {
                    var target = Find(file, r);
                    if (target is Terrain terrain)
                    {
                        int terrainIndex = r.GetProperty("index").GetInt32();
                        terrain.lightmapIndex = terrainIndex >= 0xFFFE ? terrainIndex : terrainIndex + offset;
                        terrain.lightmapScaleOffset = LightingDataReply.Vector(r.GetProperty("st"));
                        terrain.realtimeLightmapIndex = r.GetProperty("indexDynamic").GetInt32();
                        terrain.realtimeLightmapScaleOffset = LightingDataReply.Vector(r.GetProperty("stDynamic"));
                        terrain.m_RealtimeLightmapFromData = IsLightmapIndex(terrain.realtimeLightmapIndex);
                        continue;
                    }
                    if (!(target is Renderer renderer)) continue;
                    // A renderer lit by light probes keeps 0xFFFE (measured: tests/projects/lightingdatanan;
                    // 0xFFFF, no lightmap, is -1 as for a renderer the bake left out, unmeasured).
                    int index = r.GetProperty("index").GetInt32();
                    renderer.lightmapIndex = index < 0xFFFE ? index + offset : index == ProbeLitLightmapIndex ? index : -1;
                    renderer.lightmapScaleOffset = LightingDataReply.Vector(r.GetProperty("st"));
                    int dynamic = r.GetProperty("indexDynamic").GetInt32();
                    renderer.realtimeLightmapIndex = dynamic >= 0xFFFE ? -1 : dynamic;
                    renderer.realtimeLightmapScaleOffset = LightingDataReply.Vector(r.GetProperty("stDynamic"));
                    renderer.m_RealtimeLightmapFromData = IsLightmapIndex(renderer.realtimeLightmapIndex);
                }
            }
            if (file != null && root.TryGetProperty("lights", out var lights))
            {
                foreach (var l in lights.EnumerateArray())
                {
                    if (!(Find(file, l) is Light light)) continue;
                    light.bakingOutput = new LightBakingOutput
                    {
                        probeOcclusionLightIndex = l.GetProperty("probeOcclusionLightIndex").GetInt32(),
                        occlusionMaskChannel = l.GetProperty("occlusionMaskChannel").GetInt32(),
                        lightmapBakeType = (LightmapBakeType)l.GetProperty("lightmapBakeType").GetInt32(),
                        mixedLightingMode = (MixedLightingMode)l.GetProperty("mixedLightingMode").GetInt32(),
                        isBaked = l.GetProperty("isBaked").GetBoolean(),
                    };
                }
            }
            // Baked reflection probes reflect the cubemaps the bake made for them.
            if (file != null && root.TryGetProperty("reflectionProbes", out var reflections))
            {
                foreach (var r in reflections.EnumerateArray())
                {
                    if (!(Find(file, r) is ReflectionProbe probe)) continue;
                    var cubemap = r.GetProperty("cubemap");
                    probe.bakedTexture = cubemap.ValueKind == JsonValueKind.Object
                        ? RenderSettings.BakedReflection(cubemap.GetProperty("guid").GetString(), cubemap.GetProperty("fileId").GetInt64())
                        : null;
                }
            }
            if (root.TryGetProperty("probes", out var probes) && probes.ValueKind == JsonValueKind.Object)
                LightProbes.SceneLoaded(scene, lightingData, probes);
        }

        /// <summary>The lightmap index of a renderer that contributes to global illumination and
        /// receives it from light probes.</summary>
        const int ProbeLitLightmapIndex = 0xFFFE;

        /// <summary>A lightmap index that names a lightmap (0xFFFE and 0xFFFF name none).</summary>
        internal static bool IsLightmapIndex(int index) => index >= 0 && index < 0xFFFE;

        /// <summary>A mixed light baked in the Shadowmask Lighting Mode: the built-in pipeline mixes
        /// its baked shadow mask with its realtime shadows.</summary>
        internal static bool MixesShadowmask(Light light) =>
            light != null && GraphicsSettings.currentRenderPipeline == null
            && light.bakingOutput is { isBaked: true, lightmapBakeType: LightmapBakeType.Mixed, mixedLightingMode: MixedLightingMode.Shadowmask };

        static bool Lightmapped(Renderer renderer) =>
            renderer != null && IsLightmapIndex(renderer.lightmapIndex) && LightmapSettings.Of(renderer.lightmapIndex)?.m_Light != null;

        /// <summary>
        /// The built-in pipeline's shadowmask keywords of a forward pass lit by <paramref name="light"/>,
        /// as Unity's UnityShadowLibrary.cginc lists them: a lightmapped renderer takes
        /// SHADOWS_SHADOWMASK, with LIGHTMAP_SHADOW_MIXING in the Shadowmask mode (not in Distance
        /// Shadowmask); any other renderer takes LIGHTMAP_SHADOW_MIXING in the Shadowmask mode. A
        /// pass without the lightmap (ForwardAdd) gets the renderer's shadow mask and lightmap scale
        /// and offset (tests/projects/shadowmaskmix forward_*).
        /// </summary>
        internal static Dictionary<int, object> ApplyShadowmask(Renderer renderer, Light light, bool lightmaps, Dictionary<int, object> properties, List<string> keywords)
        {
            if (!MixesShadowmask(light)) return properties;
            bool shadowmaskMode = QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask;
            if (Lightmapped(renderer))
            {
                if (!lightmaps)
                {
                    properties = DrawPropertyPool.Copy(properties);
                    properties[s_ShadowMask] = LightmapSettings.Of(renderer.lightmapIndex).m_ShadowMask ?? DefaultTextures.White;
                    properties[s_LightmapST] = renderer.lightmapScaleOffset;
                }
                keywords.Add("SHADOWS_SHADOWMASK");
            }
            else if (lightmaps)
            {
                // A probe-lit renderer's base pass: its light attenuated by the probes' occlusion
                // (the add pass's and another base light's: FrameBuilder.EmitItem).
                float occlusion = ProbeOcclusion(renderer, light);
                if (occlusion != 1f)
                {
                    properties = DrawPropertyPool.Copy(properties);
                    properties[s_LightColor0] = (Vector4)light.FinalColor(QualitySettings.activeColorSpace == ColorSpace.Linear) * occlusion;
                }
            }
            if (shadowmaskMode) keywords.Add("LIGHTMAP_SHADOW_MIXING");
            return properties;
        }

        static readonly int s_LightColor0 = Shader.PropertyToID("_LightColor0");

        /// <summary>
        /// The occlusion a probe-lit renderer's light probes give a light mixed with a shadow mask
        /// (its channel of the probes' occlusion blended at the renderer, 1 otherwise): the built-in
        /// pipeline's forward passes attenuate the light's color by it in both Shadowmask modes
        /// (UnityShadowLibrary.cginc: light probe occlusion is done on the CPU), so a dynamic object
        /// takes static objects' shadows (tests/projects/shadowmaskmix, Shadowed Sphere, also with
        /// the static wall left out of the realtime casters in Distance Shadowmask).
        /// </summary>
        internal static float ProbeOcclusion(Renderer renderer, Light light)
        {
            if (!MixesShadowmask(light)) return 1f;
            int channel = light.bakingOutput.occlusionMaskChannel;
            var occlusion = ProbeOcclusionVector(renderer);
            return channel >= 0 && channel < 4 ? occlusion[channel] : 1f;
        }

        /// <summary>The probes' occlusion blended at a probe-lit renderer (unity_ProbesOcclusion; 1
        /// for a lightmapped renderer or one without blended probes).</summary>
        internal static Vector4 ProbeOcclusionVector(Renderer renderer)
        {
            if (renderer == null || Lightmapped(renderer)) return Vector4.one;
            // A renderer's own occlusion (CustomProvided with one in its block) wins.
            if (renderer.lightProbeUsage == LightProbeUsage.CustomProvided && renderer.m_PropertyBlock != null
                && renderer.m_PropertyBlock.m_Values.TryGetValue(s_ProbesOcclusion, out var provided) && provided is Vector4[] array && array.Length > 0)
                return array[0];
            bool blended = renderer.lightProbeUsage is LightProbeUsage.BlendProbes or LightProbeUsage.CustomProvided
                || (renderer.lightProbeUsage == LightProbeUsage.UseProxyVolume && LightProbeProxyVolume.Of(renderer) == null);
            if (!blended) return Vector4.one;
            if (renderer.m_ProbeOcclusionFrame == Time.frameCount) return renderer.m_ProbeOcclusion;
            var lightProbes = LightmapSettings.lightProbes;
            if (lightProbes == null || lightProbes.count == 0) return Vector4.one;
            var anchor = renderer.probeAnchor;
            int cell = renderer.m_ProbeCell;
            if (!lightProbes.Interpolate(anchor != null ? anchor.position : renderer.bounds.center, ref cell, out _, out var occlusion)) return Vector4.one;
            renderer.m_ProbeCell = cell;
            renderer.m_ProbeOcclusion = occlusion;
            renderer.m_ProbeOcclusionFrame = Time.frameCount;
            return occlusion;
        }

        /// <summary>Whether <paramref name="light"/>'s realtime shadows leave lightmapped casters out:
        /// a light mixed in a Shadowmask bake under NonLightmappedOnly, Default being NonLightmappedOnly
        /// in the Shadowmask mode and Everything in Distance Shadowmask (Light.lightShadowCasterMode;
        /// tests/projects/shadowmaskmix *_default, *_nonlightmappedonly). The built-in casters and the
        /// BatchRendererGroup culling flag share it.</summary>
        internal static bool ShadowmaskLeavesOutLightmapped(Light light)
        {
            var baking = light.bakingOutput;
            if (!baking.isBaked || baking.lightmapBakeType != LightmapBakeType.Mixed || baking.mixedLightingMode != MixedLightingMode.Shadowmask) return false;
            return light.lightShadowCasterMode switch
            {
                LightShadowCasterMode.NonLightmappedOnly => true,
                LightShadowCasterMode.Everything => false,
                _ => QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask,
            };
        }

        /// <summary>Whether a renderer casts <paramref name="light"/>'s realtime shadows in the
        /// built-in pipeline (<see cref="ShadowmaskLeavesOutLightmapped"/>).</summary>
        internal static bool CastsRealtimeShadow(Light light, Renderer renderer) =>
            !MixesShadowmask(light) || !ShadowmaskLeavesOutLightmapped(light) || !Lightmapped(renderer);

        internal static int RealtimeContributors(JsonElement root)
        {
            int count = 0;
            if (root.TryGetProperty("renderers", out var renderers))
                foreach (var renderer in renderers.EnumerateArray())
                    if (IsLightmapIndex(renderer.GetProperty("indexDynamic").GetInt32())) count++;
            return count;
        }

        /// <summary>The scene object an entry is of: by its file id, or (in a prefab instance) by
        /// the id Unity gives a prefab instance's object, its prefab file id XOR the instance's
        /// (assumed, not measured).</summary>
        internal static Object Find(LoadedFile file, JsonElement entry)
        {
            long id = entry.GetProperty("object").GetInt64();
            long prefab = entry.GetProperty("prefab").GetInt64();
            if (prefab != 0) id = (id ^ prefab) & long.MaxValue;
            return file.Objects.TryGetValue(id, out var obj) ? obj : null;
        }

        static Texture2D Texture(JsonElement map, string key)
        {
            if (!map.TryGetProperty(key, out var r) || r.ValueKind != JsonValueKind.Object) return null;
            return AssetDatabaseRuntime.Load(r.GetProperty("guid").GetString(), r.GetProperty("fileId").GetInt64(), typeof(Texture2D)) as Texture2D;
        }

        static readonly int s_Lightmap = Shader.PropertyToID("unity_Lightmap");
        static readonly int s_LightmapInd = Shader.PropertyToID("unity_LightmapInd");
        static readonly int s_ShadowMask = Shader.PropertyToID("unity_ShadowMask");
        static readonly int s_LightmapST = Shader.PropertyToID("unity_LightmapST");
        static readonly int s_LightmapHDR = Shader.PropertyToID("unity_Lightmap_HDR");
        static readonly int s_DynamicLightmap = Shader.PropertyToID("unity_DynamicLightmap");
        static readonly int s_DynamicLightmapST = Shader.PropertyToID("unity_DynamicLightmapST");
        static readonly int s_DynamicLightmapHDR = Shader.PropertyToID("unity_DynamicLightmap_HDR");
        /// <summary>How the shaders decode the realtime lightmap (unity_DynamicLightmap_HDR: a
        /// multiplier and an exponent): its half-float irradiance as it is in a linear project,
        /// raised to 1/2.2 in a gamma-space one (tests/projects/realtimegi `--color-space gamma`,
        /// `room-base` and `room-moved` frames).</summary>
        static readonly object s_LinearRealtimeDecode = new Vector4(1f, 1f, 0f, 0f);
        static readonly object s_GammaRealtimeDecode = new Vector4(1f, 1f / 2.2f, 0f, 0f);
        static readonly int s_DynamicDirectionality = Shader.PropertyToID("unity_DynamicDirectionality");
        static Texture2D s_FlatDirection;
        internal static bool s_WarnedUnmadeRealtimeLightmap;

        /// <summary>A directionality that leaves a lightmap's light as it is whatever the shaded
        /// normal: no direction (1/2 in each channel), and a rebalancing term of 1/2, the half
        /// Lambert term of no direction. Half floats, which hold 1/2 exactly.</summary>
        static Texture2D FlatDirection
        {
            get
            {
                if (s_FlatDirection == null)
                {
                    using (Unbound.Runtime.ObjectRegistry.AsAssets())
                        s_FlatDirection = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true) { name = "Flat Directionality", hideFlags = HideFlags.HideAndDontSave };
                    s_FlatDirection.SetPixelData(new[] { Mathf.FloatToHalf(0.5f), Mathf.FloatToHalf(0.5f), Mathf.FloatToHalf(0.5f), Mathf.FloatToHalf(0.5f) }, 0);
                    s_FlatDirection.Apply(false);
                }
                return s_FlatDirection;
            }
        }
        static readonly int[] s_ShIds =
        {
            Shader.PropertyToID("unity_SHAr"), Shader.PropertyToID("unity_SHAg"), Shader.PropertyToID("unity_SHAb"),
            Shader.PropertyToID("unity_SHBr"), Shader.PropertyToID("unity_SHBg"), Shader.PropertyToID("unity_SHBb"),
            Shader.PropertyToID("unity_SHC"),
        };

        /// <summary>
        /// What a renderer's draws take from the baked lighting: a lightmapped renderer its
        /// lightmap (unity_Lightmap, unity_LightmapInd, unity_ShadowMask, unity_LightmapST) and the
        /// LIGHTMAP_ON (and DIRLIGHTMAP_COMBINED) keywords; one lit by blended probes the probes
        /// interpolated at its bounds' center (or anchor) as ShadeSH9's constants. Null when
        /// neither applies. <paramref name="lightmaps"/>, <paramref name="probes"/>: what the
        /// pipeline asks for (PerObjectData).
        /// </summary>
        internal static Dictionary<int, object> Apply(Renderer renderer, Dictionary<int, object> properties, List<string> keywords, bool lightmaps = true, bool probes = true,
            bool proxyVolumes = true)
        {
            if (renderer == null) return properties;
            // Precomputed realtime GI's lightmap (unity_DynamicLightmap with the renderer's realtime
            // scale and offset, DYNAMICLIGHTMAP_ON); a renderer with one takes no light probes.
            // With directional lightmaps its directionality too (unity_DynamicDirectionality,
            // DIRLIGHTMAP_COMBINED).
            // A realtime lightmap index with no realtime lightmap behind it (a script's, while
            // no realtime GI has a lightmap at that index) draws the same way with a grey texture
            // of half alpha for both maps, terrains and meshes alike (matches the Unity
            // 6000.0.71f1 Editor, tests/projects/terrainlightmaps cases 12-14 and 29-35). An index
            // the scene's realtime GI gave or has a lightmap at, which Unbound did not produce
            // for this renderer, keeps the fallback without it.
            bool indexed = lightmaps && IsLightmapIndex(renderer.realtimeLightmapIndex);
            var made = renderer.realtimeLightmapIndex == renderer.m_RealtimeLightmapMadeFor ? renderer.m_RealtimeLightmap : null;
            var madeDirectionality = made != null ? renderer.m_RealtimeDirectionality : null;
            bool unmade = indexed && made == null;
            bool absent = unmade && !renderer.m_RealtimeLightmapFromData && !RealtimeGI.HasRealtimeLightmap(renderer.realtimeLightmapIndex);
            if (unmade && !absent && !s_WarnedUnmadeRealtimeLightmap)
            {
                s_WarnedUnmadeRealtimeLightmap = true;
                Unbound.Runtime.Stubs.Ignored("Renderer.realtimeLightmapIndex",
                    "points at a realtime lightmap Unbound has not made (realtime GI off in the scene's Lighting Settings or not read, a system not drawn, a terrain's realtime charts, or a script's index at another renderer's atlas): drawn without it");
            }
            bool realtime = indexed && (made != null || absent);
            bool directional = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
            bool realtimeDirectional = realtime && directional && (madeDirectionality != null || absent);
            if (realtime)
            {
                properties = DrawPropertyPool.Copy(properties);
                properties[s_DynamicLightmap] = made ?? DefaultTextures.HalfGray;
                properties[s_DynamicLightmapST] = renderer.realtimeLightmapScaleOffset;
                properties[s_DynamicLightmapHDR] = QualitySettings.activeColorSpace == ColorSpace.Gamma ? s_GammaRealtimeDecode : s_LinearRealtimeDecode;
                keywords.Add("DYNAMICLIGHTMAP_ON");
                // The keyword is the baked lightmap's too: whichever of the two has no direction
                // of its own under it gets one that changes nothing (unmeasured in the Editor).
                if (directional) properties[s_DynamicDirectionality] = madeDirectionality ?? (absent ? DefaultTextures.HalfGray : FlatDirection);
                if (realtimeDirectional) keywords.Add("DIRLIGHTMAP_COMBINED");
            }
            var map = lightmaps ? LightmapSettings.Of(renderer.lightmapIndex) : null;
            // Terrain selects the lightmapped variant from its index, even for a missing
            // texture or an index beyond the current array. Native binds black for an empty
            // slot and an opaque 128 grey for an absent slot, not grayTexture (terrainlightmaps
            // oracle).
            bool terrainLightmapped = lightmaps && renderer is TerrainRenderer
                && renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 0xFFFE;
            if ((map != null && map.m_Light != null) || terrainLightmapped)
            {
                if (!realtime) properties = DrawPropertyPool.Copy(properties);
                properties[s_LightmapST] = renderer.lightmapScaleOffset;
                BindLightmap(map, terrainLightmapped, directional, realtimeDirectional, properties, keywords);
                // Its light is in the lightmap: a render pipeline's draw gets no probe (zero SH;
                // measured: tests/projects/urp per-object-constants.jsonl, the lightmapped
                // renderers); the built-in pipeline's keeps the ambient probe whatever its probe
                // usage (measured: tests/projects/reflectionprobes sh.*, with and without light
                // probes in the scene).
                return GraphicsSettings.currentRenderPipeline != null ? WithProbe(default, properties, copy: false, renderer) : properties;
            }
            if (realtime) return GraphicsSettings.currentRenderPipeline != null ? WithProbe(default, properties, copy: false, renderer) : properties;
            if (probes && renderer.lightProbeUsage == LightProbeUsage.CustomProvided)
            {
                properties = ProvidedProbes(properties, renderer.m_PropertyBlock);
                // Its occlusion, when its block has none, is the probes' as in normal lighting
                // (LightProbeUsage.CustomProvided's documentation).
                if (properties != null && properties.ContainsKey(s_ProbesOcclusion)) return properties;
                var blended = ProbeOcclusionVector(renderer);
                if (GraphicsSettings.currentRenderPipeline != null || blended == Vector4.one) return properties;
                properties = DrawPropertyPool.Copy(properties);
                return WithOcclusion(renderer, blended, properties);
            }
            // Through a built proxy volume: its texture and constants; the renderer's probe
            // constants stay the ambient probe's (the frame's), none with Low quality (measured:
            // tests/projects/lppv *.sh). Without one it is lit as Blend Probes lights it.
            if (probes && proxyVolumes && renderer.lightProbeUsage == LightProbeUsage.UseProxyVolume && LightProbeProxyVolume.Of(renderer) is { } volume)
            {
                properties = volume.LowQuality ? WithProbe(default, properties, owner: renderer) : DrawPropertyPool.Copy(properties);
                volume.Apply(properties);
                return properties;
            }
            var lightProbes = LightmapSettings.lightProbes;
            if (!probes || lightProbes == null || lightProbes.count == 0
                || (renderer.lightProbeUsage != LightProbeUsage.BlendProbes && renderer.lightProbeUsage != LightProbeUsage.UseProxyVolume))
                return properties;
            var anchor = renderer.probeAnchor;
            var position = anchor != null ? anchor.position : renderer.bounds.center;
            int cell = renderer.m_ProbeCell;
            if (!lightProbes.Interpolate(position, ref cell, out var sh, out var occlusion)) return properties;
            renderer.m_ProbeCell = cell;
            renderer.m_ProbeOcclusion = occlusion;
            renderer.m_ProbeOcclusionFrame = Time.frameCount;
            properties = WithProbe(sh, properties, owner: renderer);
            return WithOcclusion(renderer, occlusion, properties);
        }

        /// <summary>A built-in pipeline draw's unity_ProbesOcclusion: the probes' occlusion (a
        /// Deferred pass writes it as the renderer's shadow mask); left alone when it is all 1.</summary>
        static Dictionary<int, object> WithOcclusion(Renderer renderer, Vector4 occlusion, Dictionary<int, object> properties)
        {
            if (GraphicsSettings.currentRenderPipeline != null || occlusion == Vector4.one) return properties;
            properties[s_ProbesOcclusion] = BoxedVectors.Of(renderer.m_OcclusionBox ??= new BoxedVectors(1), 0, occlusion);
            return properties;
        }

        /// <summary>A lightmap's textures and keywords on a draw's own properties: the lightmap
        /// (black for an empty slot, an opaque grey for an absent one where a terrain selects it),
        /// its decode values, LIGHTMAP_ON, its directions with directional lightmaps
        /// (DIRLIGHTMAP_COMBINED, unless the realtime lightmap's directions set it) and its shadow
        /// mask. The scale and offset are the caller's.</summary>
        static void BindLightmap(LightmapData map, bool selected, bool directional, bool realtimeDirectional, Dictionary<int, object> properties, List<string> keywords)
        {
            properties[s_Lightmap] = map?.m_Light ?? (map != null ? DefaultTextures.Black : DefaultTextures.AbsentLightmap);
            properties[s_LightmapHDR] = LightmapSettings.DecodeValues;
            keywords.Add("LIGHTMAP_ON");
            if ((map?.m_Dir != null || selected) && directional)
            {
                properties[s_LightmapInd] = map?.m_Dir ?? DefaultTextures.Black;
                if (!realtimeDirectional) keywords.Add("DIRLIGHTMAP_COMBINED");
            }
            else if (realtimeDirectional) properties[s_LightmapInd] = FlatDirection;
            // Mixed lighting's shadow masks are bound. A render pipeline mixes them with its
            // realtime shadows itself (URP sets their keywords: tests/projects/urp
            // lightmap_shadowmask, lightmap_distance_shadowmask); the built-in pipeline's forward
            // passes take the keywords of their light (ApplyShadowmask).
            if (map?.m_ShadowMask != null) properties[s_ShadowMask] = map.m_ShadowMask;
        }

        /// <summary>A BatchRendererGroup draw's lightmap (its command's index, the legacy lightmaps
        /// keyword: URP's GPU Resident Drawer): the textures and keywords of the scene's lightmap
        /// at <paramref name="index"/>; each instance's scale and offset are its own instance data
        /// (unity_LightmapST). An index without a lightmap draws without one. Matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/residentdrawer lightmaps.</summary>
        internal static Dictionary<int, object> ApplyLightmap(int index, Dictionary<int, object> properties, List<string> keywords)
        {
            var map = LightmapSettings.Of(index);
            if (map == null || map.m_Light == null) return properties;
            properties = DrawPropertyPool.Copy(properties);
            BindLightmap(map, false, LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional, false, properties, keywords);
            return properties;
        }

        static int s_ImmediateCell = -1;

        /// <summary>The light probes interpolated at a point (a Graphics.DrawMesh draw's bounds
        /// centre or anchor), as a renderer that blends probes gets them; outside the probes' hull
        /// none (measured: tests/projects/baked, a DrawMesh sphere just outside the grid gets the
        /// ambient alone).</summary>
        internal static Dictionary<int, object> ApplyProbes(Vector3 position, Dictionary<int, object> properties)
        {
            var lightProbes = LightmapSettings.lightProbes;
            if (lightProbes == null || lightProbes.count == 0) return properties;
            if (!lightProbes.Interpolate(position, ref s_ImmediateCell, out var sh)) return properties;
            if (lightProbes.IsOuterCell(s_ImmediateCell)) return properties;
            return WithProbe(sh, properties);
        }

        static readonly int s_ProbesOcclusion = Shader.PropertyToID("unity_ProbesOcclusion");

        /// <summary>A renderer with LightProbeUsage.CustomProvided: the probe its property block's
        /// arrays (MaterialPropertyBlock.CopySHCoefficientArraysFrom) hold first is its own
        /// (measured: tests/projects/lightprobes, renderers whose block holds one probe). A mesh
        /// renderer's block reaches its draws inside a material copy
        /// (<see cref="Renderer.DrawnMaterial"/>), not as draw properties, so the arrays are read
        /// from <paramref name="block"/> when the draw's properties lack them.</summary>
        static Dictionary<int, object> ProvidedProbes(Dictionary<int, object> properties, MaterialPropertyBlock block)
        {
            Dictionary<int, object> result = null;
            foreach (var id in s_ShIds.Append(s_ProbesOcclusion))
            {
                object value = null;
                if ((properties == null || !properties.TryGetValue(id, out value)) && block != null) block.m_Values.TryGetValue(id, out value);
                if (!(value is Vector4[] array) || array.Length == 0) continue;
                result ??= DrawPropertyPool.Copy(properties);
                result[id] = array[0];
            }
            return result ?? properties;
        }

        /// <summary>The probe's ShadeSH9 constants on a copy of <paramref name="properties"/> (on
        /// them, without <paramref name="copy"/>: a dictionary this draw already owns).</summary>
        static Dictionary<int, object> WithProbe(SphericalHarmonicsL2 sh, Dictionary<int, object> properties, bool copy = true, Renderer owner = null)
        {
            sh.ShaderConstants(out var ar, out var ag, out var ab, out var br, out var bg, out var bb, out var c);
            if (copy || properties == null)
                properties = DrawPropertyPool.Copy(properties);
            // A renderer keeps its constants boxed while they stay the same.
            var boxes = owner != null ? owner.m_ProbeBoxes ??= new BoxedVectors(s_ShIds.Length) : null;
            properties[s_ShIds[0]] = BoxedVectors.Of(boxes, 0, ar);
            properties[s_ShIds[1]] = BoxedVectors.Of(boxes, 1, ag);
            properties[s_ShIds[2]] = BoxedVectors.Of(boxes, 2, ab);
            properties[s_ShIds[3]] = BoxedVectors.Of(boxes, 3, br);
            properties[s_ShIds[4]] = BoxedVectors.Of(boxes, 4, bg);
            properties[s_ShIds[5]] = BoxedVectors.Of(boxes, 5, bb);
            properties[s_ShIds[6]] = BoxedVectors.Of(boxes, 6, c);
            return properties;
        }
    }
}
