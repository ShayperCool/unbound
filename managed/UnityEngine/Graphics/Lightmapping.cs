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

        [Obsolete("Use lightmapColor property (UnityUpgradable) -> lightmapColor")]
        public Texture2D lightmapLight { get => m_Light; set => m_Light = value; }
        public Texture2D lightmapColor { get => m_Light; set => m_Light = value; }
        public Texture2D lightmapDir { get => m_Dir; set => m_Dir = value; }
        public Texture2D shadowMask { get => m_ShadowMask; set => m_ShadowMask = value; }
        [Obsolete("Property LightmapData.lightmap has been deprecated. Use LightmapData.lightmapColor instead (UnityUpgradable) -> lightmapColor")]
        public Texture2D lightmap { get => m_Light; set => m_Light = value; }
        [Obsolete("Property LightmapData.lightmapFar has been deprecated. Use LightmapData.lightmapColor instead (UnityUpgradable) -> lightmapColor")]
        public Texture2D lightmapFar { get => m_Light; set => m_Light = value; }
        [Obsolete("Property LightmapData.lightmapNear has been deprecated. Use LightmapData.lightmapDir instead (UnityUpgradable) -> lightmapDir")]
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
        Vector3[] m_SourcePositions = Array.Empty<Vector3>();
        bool m_Dering = true;

        public LightProbeGroup() { }

        public Vector3[] probePositions
        {
            get => (Vector3[])m_SourcePositions.Clone();
            set => m_SourcePositions = value == null ? Array.Empty<Vector3>() : (Vector3[])value.Clone();
        }

        public bool dering { get => m_Dering; set => m_Dering = value; }

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
                        terrain.lightmapScaleOffset = Vector(r.GetProperty("st"));
                        terrain.realtimeLightmapIndex = r.GetProperty("indexDynamic").GetInt32();
                        terrain.realtimeLightmapScaleOffset = Vector(r.GetProperty("stDynamic"));
                        continue;
                    }
                    if (!(target is Renderer renderer)) continue;
                    int index = r.GetProperty("index").GetInt32();
                    renderer.lightmapIndex = index >= 0xFFFE ? -1 : index + offset;
                    renderer.lightmapScaleOffset = Vector(r.GetProperty("st"));
                    int dynamic = r.GetProperty("indexDynamic").GetInt32();
                    renderer.realtimeLightmapIndex = dynamic >= 0xFFFE ? -1 : dynamic;
                    renderer.realtimeLightmapScaleOffset = Vector(r.GetProperty("stDynamic"));
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

        /// <summary>A lightmap index that names a lightmap (0xFFFE and 0xFFFF name none).</summary>
        static bool IsLightmapIndex(int index) => index >= 0 && index < 0xFFFE;

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

        static Vector4 Vector(JsonElement v)
        {
            var f = new float[4];
            int i = 0;
            foreach (var x in v.EnumerateArray()) if (i < 4) f[i++] = x.GetSingle();
            return new Vector4(f[0], f[1], f[2], f[3]);
        }

        static readonly int s_Lightmap = Shader.PropertyToID("unity_Lightmap");
        static readonly int s_LightmapInd = Shader.PropertyToID("unity_LightmapInd");
        static readonly int s_ShadowMask = Shader.PropertyToID("unity_ShadowMask");
        static readonly int s_LightmapST = Shader.PropertyToID("unity_LightmapST");
        static readonly int s_DynamicLightmap = Shader.PropertyToID("unity_DynamicLightmap");
        static readonly int s_DynamicLightmapST = Shader.PropertyToID("unity_DynamicLightmapST");
        static readonly int s_DynamicDirectionality = Shader.PropertyToID("unity_DynamicDirectionality");
        static Texture2D s_FlatDirection;

        /// <summary>A directionality that leaves a lightmap's light as it is whatever the shaded
        /// normal: no direction, and a rebalancing term of 1/2, the half Lambert term of no
        /// direction.</summary>
        static Texture2D FlatDirection
        {
            get
            {
                if (s_FlatDirection == null)
                {
                    using (Unbound.Runtime.ObjectRegistry.AsAssets())
                        s_FlatDirection = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Flat Directionality", hideFlags = HideFlags.HideAndDontSave };
                    s_FlatDirection.SetPixelData(new byte[] { 128, 128, 128, 128 }, 0);
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
        internal static Dictionary<int, object> Apply(Renderer renderer, Dictionary<int, object> properties, List<string> keywords, bool lightmaps = true, bool probes = true)
        {
            if (renderer == null) return properties;
            // Precomputed realtime GI's lightmap (unity_DynamicLightmap with the renderer's realtime
            // scale and offset, DYNAMICLIGHTMAP_ON); a renderer with one takes no light probes.
            // With directional lightmaps its directionality too (unity_DynamicDirectionality,
            // DIRLIGHTMAP_COMBINED).
            bool realtime = lightmaps && renderer.m_RealtimeLightmap != null && IsLightmapIndex(renderer.realtimeLightmapIndex);
            bool directional = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
            bool realtimeDirectional = realtime && directional && renderer.m_RealtimeDirectionality != null;
            if (realtime)
            {
                properties = DrawPropertyPool.Copy(properties);
                properties[s_DynamicLightmap] = renderer.m_RealtimeLightmap;
                properties[s_DynamicLightmapST] = renderer.realtimeLightmapScaleOffset;
                keywords.Add("DYNAMICLIGHTMAP_ON");
                // The keyword is the baked lightmap's too: whichever of the two has no direction
                // of its own under it gets one that changes nothing (unmeasured in the Editor).
                if (directional) properties[s_DynamicDirectionality] = renderer.m_RealtimeDirectionality ?? FlatDirection;
                if (realtimeDirectional) keywords.Add("DIRLIGHTMAP_COMBINED");
            }
            var map = lightmaps ? LightmapSettings.Of(renderer.lightmapIndex) : null;
            // Terrain selects the lightmapped variant from its index, even for a missing
            // texture or an index beyond the current array. Native binds black for an empty
            // slot and the default grey texture for an absent slot (terrainlightmaps oracle).
            bool terrainLightmapped = lightmaps && renderer is TerrainRenderer
                && renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 0xFFFE;
            if ((map != null && map.m_Light != null) || terrainLightmapped)
            {
                if (!realtime) properties = DrawPropertyPool.Copy(properties);
                properties[s_Lightmap] = map?.m_Light ?? (map != null ? DefaultTextures.Black : DefaultTextures.Gray);
                properties[s_LightmapST] = renderer.lightmapScaleOffset;
                keywords.Add("LIGHTMAP_ON");
                if ((map?.m_Dir != null || terrainLightmapped) && directional)
                {
                    properties[s_LightmapInd] = map?.m_Dir ?? DefaultTextures.Black;
                    if (!realtimeDirectional) keywords.Add("DIRLIGHTMAP_COMBINED");
                }
                else if (realtimeDirectional) properties[s_LightmapInd] = FlatDirection;
                if (map?.m_ShadowMask != null)
                {
                    properties[s_ShadowMask] = map.m_ShadowMask;
                    // Mixed lighting's shadow masks are bound. A render pipeline mixes them with
                    // its realtime shadows itself (URP sets their keywords: tests/projects/urp
                    // lightmap_shadowmask, lightmap_distance_shadowmask); the built-in one does not yet.
                    if (GraphicsSettings.currentRenderPipeline == null)
                        Unbound.Runtime.Stubs.Ignored("QualitySettings.shadowmaskMode",
                        "baked shadow masks are not mixed with realtime shadows yet (shadowmaskMode and Light.lightShadowCasterMode have no effect)");
                }
                // Its light is in the lightmap: a render pipeline's draw gets no probe (zero SH;
                // measured: tests/projects/urp per-object-constants.jsonl, the lightmapped
                // renderers); the built-in pipeline's keeps the ambient probe whatever its probe
                // usage (measured: tests/projects/reflectionprobes sh.*, with and without light
                // probes in the scene).
                return GraphicsSettings.currentRenderPipeline != null ? WithProbe(default, properties, copy: false, renderer) : properties;
            }
            if (realtime) return GraphicsSettings.currentRenderPipeline != null ? WithProbe(default, properties, copy: false, renderer) : properties;
            if (probes && renderer.lightProbeUsage == LightProbeUsage.CustomProvided) return ProvidedProbes(properties, renderer.m_PropertyBlock);
            var lightProbes = LightmapSettings.lightProbes;
            if (!probes || lightProbes == null || lightProbes.count == 0
                || (renderer.lightProbeUsage != LightProbeUsage.BlendProbes && renderer.lightProbeUsage != LightProbeUsage.UseProxyVolume))
                return properties;
            var anchor = renderer.probeAnchor;
            var position = anchor != null ? anchor.position : renderer.bounds.center;
            int cell = renderer.m_ProbeCell;
            if (!lightProbes.Interpolate(position, ref cell, out var sh)) return properties;
            renderer.m_ProbeCell = cell;
            return WithProbe(sh, properties, owner: renderer);
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
