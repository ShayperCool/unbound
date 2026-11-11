using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A reflection probe: the cubemap the renderers inside its box reflect instead of the default
    /// reflection. Custom probes reflect their texture, baked ones the cubemap the scene's
    /// lighting data gives them (an unbaked one is left out), realtime ones the cube they render
    /// (black until then; ReflectionProbe.Realtime.cs). Which probes a renderer uses was
    /// measured on the 6000.0 Editor, see <see cref="Closest"/>.
    /// </summary>
    [ClassId(215)]
    public sealed partial class ReflectionProbe : Behaviour
    {
        internal static readonly List<ReflectionProbe> s_Enabled = new List<ReflectionProbe>();

        ReflectionProbeType m_Type;
        ReflectionProbeMode m_Mode = ReflectionProbeMode.Baked;
        ReflectionProbeRefreshMode m_RefreshMode;
        ReflectionProbeTimeSlicingMode m_TimeSlicingMode;
        int m_Resolution = 128;
        Vector3 m_BoxSize = new Vector3(10f, 10f, 10f);
        Vector3 m_BoxOffset;
        float m_NearClip = 0.3f;
        float m_FarClip = 1000f;
        float m_ShadowDistance = 100f;
        ReflectionProbeClearFlags m_ClearFlags = ReflectionProbeClearFlags.Skybox;
        Color m_BackgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f, 0f);
        int m_CullingMask = -1;
        float m_Intensity = 1f;
        float m_BlendDistance = 1f;
        bool m_Hdr = true;
        bool m_BoxProjection;
        bool m_RenderDynamicObjects;
        int m_Importance = 1;
        Texture m_CustomBakedTexture;
        Texture m_BakedTexture;

        public ReflectionProbe() { }

        public ReflectionProbeType type { get => m_Type; set => m_Type = value; }
        public ReflectionProbeMode mode { get => m_Mode; set => m_Mode = value; }
        public ReflectionProbeRefreshMode refreshMode { get => m_RefreshMode; set => m_RefreshMode = value; }
        public ReflectionProbeTimeSlicingMode timeSlicingMode { get => m_TimeSlicingMode; set => m_TimeSlicingMode = value; }
        public int resolution { get => m_Resolution; set => m_Resolution = value; }
        public Vector3 size { get => m_BoxSize; set => m_BoxSize = value; }
        public Vector3 center { get => m_BoxOffset; set => m_BoxOffset = value; }
        public float nearClipPlane { get => m_NearClip; set => m_NearClip = value; }
        public float farClipPlane { get => m_FarClip; set => m_FarClip = value; }
        public float shadowDistance { get => m_ShadowDistance; set => m_ShadowDistance = value; }
        public ReflectionProbeClearFlags clearFlags { get => m_ClearFlags; set => m_ClearFlags = value; }
        public Color backgroundColor { get => m_BackgroundColor; set => m_BackgroundColor = value; }
        public int cullingMask { get => m_CullingMask; set => m_CullingMask = value; }
        public float intensity { get => m_Intensity; set => m_Intensity = value; }
        public float blendDistance { get => m_BlendDistance; set => m_BlendDistance = value; }
        public bool hdr { get => m_Hdr; set => m_Hdr = value; }
        public bool boxProjection { get => m_BoxProjection; set => m_BoxProjection = value; }
        public bool renderDynamicObjects { get => m_RenderDynamicObjects; set => m_RenderDynamicObjects = value; }
        public int importance { get => m_Importance; set => m_Importance = value; }
        /// <summary>A custom probe's cubemap: renderers reflect it from the next frame on (in the
        /// frame it is set they reflect black; measured: tests/projects/builtinfx
        /// probe_blend_distance_now).</summary>
        public Texture customBakedTexture
        {
            get => m_CustomBakedTexture;
            set
            {
                m_CustomBakedTexture = value;
                m_CustomFrame = Time.frameCount;
            }
        }
        int m_CustomFrame = -1;
        public Texture bakedTexture { get => m_BakedTexture; set => m_BakedTexture = value; }
        /// <summary>The cubemap the probe gives: the custom texture of a custom probe, the baked one
        /// of a baked probe, what a realtime probe last rendered into (its own cube or the one
        /// RenderProbe was given).</summary>
        public Texture texture => m_Mode == ReflectionProbeMode.Custom ? m_CustomBakedTexture
            : m_Mode == ReflectionProbeMode.Realtime ? (Texture)m_RenderTarget ?? realtimeTexture : m_BakedTexture;

        /// <summary>The box in world space: the probe's position plus <see cref="center"/>, the
        /// probe's rotation and scale ignored (measured).</summary>
        public Bounds bounds => new Bounds(transform.position + m_BoxOffset, m_BoxSize);

        /// <summary>What places a probe for the renderers: its position, box, importance, blend
        /// distance and box projection.</summary>
        internal struct Placement
        {
            internal Vector3 Position, Offset, Size;
            internal int Importance;
            internal float BlendDistance;
            internal bool BoxProjection;

            internal Bounds Bounds => new Bounds(Position + Offset, Size);
        }

        Placement m_Drawn;
        bool m_HasDrawn;

        Placement Live => new Placement
        {
            Position = transform.position,
            Offset = m_BoxOffset,
            Size = m_BoxSize,
            Importance = m_Importance,
            BlendDistance = m_BlendDistance,
            BoxProjection = m_BoxProjection,
        };

        /// <summary>The placement rendering uses: the one the frame began with, so a change made
        /// during a frame (in Update, in LateUpdate, by moving the probe) reaches the frame's
        /// renders only from the next frame on, for cameras the player loop renders and for
        /// Camera.Render alike (measured: tests/projects/reflectionprobes loop_*_same keep the old
        /// state, loop_*_next and forward_lag_next take the new one), while
        /// Renderer.GetClosestReflectionProbes answers with the change at once (lag.*). Deferred
        /// shading's probe volumes and a render pipeline's culled probes use it as well
        /// (unmeasured: one rule for every render). A probe enabled during the frame: its live
        /// placement (unmeasured).</summary>
        internal Placement Drawn => m_HasDrawn ? m_Drawn : Live;

        /// <summary>Takes every enabled probe's placement for the frame's forward draws.</summary>
        internal static void BeginFrame()
        {
            foreach (var probe in s_Enabled)
            {
                if (!probe) continue;
                probe.m_Drawn = probe.Live;
                probe.m_HasDrawn = true;
            }
        }

        /// <summary>How shaders decode the texture, scaled by the intensity (measured: 0.9 for an
        /// RGBA32 or half float cubemap at intensity 0.9).</summary>
        public Vector4 textureHDRDecodeValues
        {
            get
            {
                var decode = DecodeValues(texture);
                decode.x *= m_Intensity;
                return decode;
            }
        }

        /// <summary>What objects outside every probe reflect: the scene's default reflection.</summary>
        public static Texture defaultTexture => RenderSettings.DefaultReflectionTexture;

        /// <summary>How shaders decode it: an RGBM reflection's range (5, raised to 2.2 in linear
        /// color space), else as stored.</summary>
        public static Vector4 defaultTextureHDRDecodeValues => DecodeValues(defaultTexture);

        static Vector4 DecodeValues(Texture texture)
        {
            if (texture is Cubemap cube && cube.m_Rgbm)
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? new Vector4(Mathf.Pow(5f, 2.2f), 2.2f, 0f, 1f) : new Vector4(5f, 1f, 0f, 1f);
            return new Vector4(1f, 1f, 0f, 0f);
        }

        /// <summary>
        /// The probes a renderer reflects and their weights, as the Editor gives them (measured:
        /// tests/projects/reflectionprobes weights.*): every probe whose box touches the renderer's
        /// bounds, by importance (highest first), then by how much of the bounds they hold, then
        /// the box whose centre is nearer the bounds' centre, then the earlier enabled. One probe:
        /// weight 1. A probe more important than the next: the share of the bounds inside it, the
        /// next the rest. Equally important probes share by what they hold. Only the first two
        /// blend; any further probe is listed with weight 0. None with usage Off.
        /// </summary>
        internal static void Closest(Renderer renderer, List<ReflectionProbeBlendInfo> into) =>
            Closest(renderer.bounds, renderer.reflectionProbeUsage, into, drawn: false);

        /// <summary>The same with the probes' live placement, or with <paramref name="drawn"/> the
        /// one forward draws use (<see cref="Drawn"/>).</summary>
        internal static void Closest(Bounds bounds, ReflectionProbeUsage usage, List<ReflectionProbeBlendInfo> into, bool drawn)
        {
            into.Clear();
            if (usage == ReflectionProbeUsage.Off || s_Enabled.Count == 0) return;
            var candidates = s_Candidates;
            candidates.Clear();
            var center = bounds.center;
            int order = 0;
            foreach (var probe in s_Enabled)
            {
                order++;
                // A realtime probe counts before it has rendered (it reflects black).
                if (!probe || (probe.texture == null && probe.m_Mode != ReflectionProbeMode.Realtime)) continue;
                var placement = drawn ? probe.Drawn : probe.Live;
                var box = placement.Bounds;
                if (!Touches(box, bounds)) continue;
                candidates.Add((probe, placement.Importance, InsideShare(box, bounds), (box.center - center).sqrMagnitude, order));
            }
            if (candidates.Count == 0) return;
            candidates.Sort(s_ByImportance);
            if (candidates.Count == 1)
            {
                into.Add(new ReflectionProbeBlendInfo { probe = candidates[0].Probe, weight = 1f });
                return;
            }
            var first = candidates[0];
            var second = candidates[1];
            float weight;
            if (first.Importance != second.Importance) weight = first.Inside;
            else weight = first.Inside + second.Inside > 0f ? first.Inside / (first.Inside + second.Inside) : 1f;
            into.Add(new ReflectionProbeBlendInfo { probe = first.Probe, weight = weight });
            into.Add(new ReflectionProbeBlendInfo { probe = second.Probe, weight = 1f - weight });
            for (int i = 2; i < candidates.Count; i++) into.Add(new ReflectionProbeBlendInfo { probe = candidates[i].Probe, weight = 0f });
        }

        static readonly List<(ReflectionProbe Probe, int Importance, float Inside, float Distance, int Order)> s_Candidates =
            new List<(ReflectionProbe, int, float, float, int)>();
        static readonly Comparison<(ReflectionProbe Probe, int Importance, float Inside, float Distance, int Order)> s_ByImportance = (a, b) =>
        {
            int c = b.Importance.CompareTo(a.Importance);
            if (c != 0) return c;
            c = b.Inside.CompareTo(a.Inside);
            if (c != 0) return c;
            c = a.Distance.CompareTo(b.Distance);
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        };

        static bool Touches(Bounds a, Bounds b) =>
            a.min.x <= b.max.x && a.max.x >= b.min.x && a.min.y <= b.max.y && a.max.y >= b.min.y && a.min.z <= b.max.z && a.max.z >= b.min.z;

        /// <summary>The share of <paramref name="bounds"/> inside <paramref name="box"/> (a flat
        /// side counts as fully inside along that axis when it is).</summary>
        static float InsideShare(Bounds box, Bounds bounds)
        {
            float share = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float lo = bounds.min[axis], hi = bounds.max[axis];
                float overlap = Mathf.Min(hi, box.max[axis]) - Mathf.Max(lo, box.min[axis]);
                float extent = hi - lo;
                if (extent <= 0f) share *= overlap >= 0f ? 1f : 0f;
                else share *= Mathf.Clamp01(overlap / extent);
            }
            return share;
        }

        static readonly int s_Cube0 = Shader.PropertyToID("unity_SpecCube0");
        static readonly int s_Cube1 = Shader.PropertyToID("unity_SpecCube1");
        static readonly int s_Hdr0 = Shader.PropertyToID("unity_SpecCube0_HDR");
        static readonly int s_Hdr1 = Shader.PropertyToID("unity_SpecCube1_HDR");
        static readonly int s_Min0 = Shader.PropertyToID("unity_SpecCube0_BoxMin");
        static readonly int s_Max0 = Shader.PropertyToID("unity_SpecCube0_BoxMax");
        static readonly int s_Pos0 = Shader.PropertyToID("unity_SpecCube0_ProbePosition");
        static readonly int s_Min1 = Shader.PropertyToID("unity_SpecCube1_BoxMin");
        static readonly int s_Max1 = Shader.PropertyToID("unity_SpecCube1_BoxMax");
        static readonly int s_Pos1 = Shader.PropertyToID("unity_SpecCube1_ProbePosition");
        static readonly List<ReflectionProbeBlendInfo> s_Blend = new List<ReflectionProbeBlendInfo>();

        /// <summary>A renderer's reflection: its probes' cubemaps, decode values, boxes and the
        /// first's weight (unity_SpecCube0_BoxMin.w, what the built-in shaders blend by). Nothing
        /// when it reflects the default reflection (the frame's globals). <paramref name="srp"/>:
        /// laid out as a render pipeline's draws get it (<see cref="ApplySrp"/>).</summary>
        internal static Dictionary<int, object> Apply(Renderer renderer, Dictionary<int, object> properties, bool srp = false) =>
            renderer == null ? properties
                : Apply(renderer.bounds, renderer.reflectionProbeUsage, properties, srp, renderer);

        /// <summary>The vectors of a slot: decode values, box min and max, position.</summary>
        const int SlotVectors = 4;

        /// <summary>The same for bounds and a probe usage (a Graphics.DrawMesh draw's).
        /// <paramref name="owner"/>: the renderer, which keeps the vectors (both slots) boxed while
        /// they stay the same.</summary>
        internal static Dictionary<int, object> Apply(Bounds bounds, ReflectionProbeUsage usage, Dictionary<int, object> properties, bool srp = false,
            Renderer owner = null)
        {
            if (s_Enabled.Count == 0) return properties;
            // A render pipeline blends by its own setting: Simple samples two probes as well.
            Closest(bounds, srp && usage == ReflectionProbeUsage.Simple ? ReflectionProbeUsage.BlendProbes : usage, s_Blend, drawn: true);
            if (s_Blend.Count == 0) return properties;
            properties = DrawPropertyPool.Copy(properties);
            var boxes = owner != null ? owner.m_ReflectionBoxes ??= new BoxedVectors(2 * SlotVectors) : null;
            if (srp) return ApplySrp(properties, boxes);
            var first = s_Blend[0].probe;
            var second = s_Blend.Count > 1 ? s_Blend[1].probe : null;
            bool blend = usage != ReflectionProbeUsage.Simple && second != null;
            Set(properties, boxes, 0, first, s_Cube0, s_Hdr0, s_Min0, s_Max0, s_Pos0, blend ? s_Blend[0].weight : 1f, renderer: bounds);
            if (blend) Set(properties, boxes, SlotVectors, second, s_Cube1, s_Hdr1, s_Min1, s_Max1, s_Pos1, 0f, renderer: bounds);
            else Set(properties, boxes, SlotVectors, first, s_Cube1, s_Hdr1, s_Min1, s_Max1, s_Pos1, 0f, renderer: bounds);
            return properties;
        }

        /// <summary>
        /// A render pipeline draw's probes (measured: tests/projects/urp per-object-constants.jsonl,
        /// URP's forward path): each box's w of BoxMax is the probe's blend distance (URP blends
        /// per pixel by it); the first's BoxMin.w its weight as above; the second's BoxMin.w the
        /// sign of the first's importance over it (what URP's shaders read to let the more
        /// important probe dominate). Without a second probe the second slot is the default
        /// reflection over an unbounded box (BoxMin.w 1, BoxMax.w 0).
        /// </summary>
        static Dictionary<int, object> ApplySrp(Dictionary<int, object> properties, BoxedVectors boxes)
        {
            var first = s_Blend[0];
            var firstPlacement = first.probe.Drawn;
            Set(properties, boxes, 0, first.probe, s_Cube0, s_Hdr0, s_Min0, s_Max0, s_Pos0, first.weight, firstPlacement.BlendDistance);
            if (s_Blend.Count > 1)
            {
                var second = s_Blend[1].probe;
                var secondPlacement = second.Drawn;
                float sign = Math.Sign(firstPlacement.Importance - secondPlacement.Importance);
                Set(properties, boxes, SlotVectors, second, s_Cube1, s_Hdr1, s_Min1, s_Max1, s_Pos1, sign, secondPlacement.BlendDistance);
            }
            else SetUnbounded(properties, boxes, SlotVectors, s_Cube1, s_Hdr1, s_Min1, s_Max1, s_Pos1, 1f);
            return properties;
        }

        /// <summary>A slot holding the default reflection over an unbounded box (what a render
        /// pipeline's draws get without a probe; <paramref name="weight"/> is BoxMin.w).</summary>
        static void SetUnbounded(Dictionary<int, object> properties, BoxedVectors boxes, int slot, int cube, int hdr, int min, int max, int position, float weight)
        {
            properties[cube] = defaultTexture;
            properties[hdr] = BoxedVectors.Of(boxes, slot, DefaultDecode);
            properties[min] = BoxedVectors.Of(boxes, slot + 1, new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, weight));
            properties[max] = BoxedVectors.Of(boxes, slot + 2, new Vector4(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, 0f));
            properties[position] = BoxedVectors.Of(boxes, slot + 3, Vector4.zero);
        }

        /// <summary>The default reflection's decode values with the lighting settings' reflection
        /// intensity (the frame's unity_SpecCube0_HDR).</summary>
        internal static Vector4 DefaultDecode
        {
            get
            {
                var decode = defaultTextureHDRDecodeValues;
                decode.x *= RenderSettings.reflectionIntensity;
                return decode;
            }
        }

        /// <summary>A probe's slot. With <paramref name="renderer"/> (the built-in pipeline's
        /// draws), a box-projected probe's box also holds the renderer's bounds (measured:
        /// tests/projects/reflectionprobes forward_box, a floor larger than the probes).</summary>
        static void Set(Dictionary<int, object> properties, BoxedVectors boxes, int slot, ReflectionProbe probe, int cube, int hdr, int min, int max, int position,
            float weight, float maxW = 0f, Bounds? renderer = null)
        {
            var placement = probe.Drawn;
            var box = placement.Bounds;
            if (renderer is Bounds drawn && placement.BoxProjection) box.Encapsulate(drawn);
            bool pending = probe.m_Mode == ReflectionProbeMode.Custom && probe.m_CustomFrame >= Time.frameCount;
            properties[cube] = pending ? Black : probe.texture ?? Black;
            properties[hdr] = BoxedVectors.Of(boxes, slot, probe.textureHDRDecodeValues);
            properties[min] = BoxedVectors.Of(boxes, slot + 1, new Vector4(box.min.x, box.min.y, box.min.z, weight));
            properties[max] = BoxedVectors.Of(boxes, slot + 2, new Vector4(box.max.x, box.max.y, box.max.z, maxW));
            var p = placement.Position;
            properties[position] = BoxedVectors.Of(boxes, slot + 3, new Vector4(p.x, p.y, p.z, placement.BoxProjection ? 1f : 0f));
        }

        internal override void OnEngineEnable()
        {
            if (!s_Enabled.Contains(this)) s_Enabled.Add(this);
            if (m_Mode == ReflectionProbeMode.Realtime && m_RefreshMode == ReflectionProbeRefreshMode.OnAwake && !m_Awoken)
            {
                m_Awoken = true;
                m_Pending = true;
            }
        }

        internal override void OnEngineDisable()
        {
            s_Enabled.Remove(this);
            m_HasDrawn = false;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Type = (ReflectionProbeType)body.Int("m_Type");
            m_Mode = (ReflectionProbeMode)body.Int("m_Mode");
            m_RefreshMode = (ReflectionProbeRefreshMode)body.Int("m_RefreshMode");
            m_TimeSlicingMode = (ReflectionProbeTimeSlicingMode)body.Int("m_TimeSlicingMode");
            m_Resolution = body.Int("m_Resolution", 128);
            m_BoxSize = body.Vector3("m_BoxSize", m_BoxSize);
            m_BoxOffset = body.Vector3("m_BoxOffset");
            m_NearClip = body.Float("m_NearClip", 0.3f);
            m_FarClip = body.Float("m_FarClip", 1000f);
            m_ShadowDistance = body.Float("m_ShadowDistance", 100f);
            m_ClearFlags = (ReflectionProbeClearFlags)body.Int("m_ClearFlags", 1);
            m_BackgroundColor = body.Color("m_BackGroundColor", m_BackgroundColor);
            m_CullingMask = (int)(body.Get("m_CullingMask")?.Long("m_Bits", -1) ?? -1);
            m_Intensity = body.Float("m_IntensityMultiplier", 1f);
            m_BlendDistance = body.Float("m_BlendDistance", 1f);
            m_Hdr = body.Bool("m_HDR", true);
            m_BoxProjection = body.Bool("m_BoxProjection");
            m_RenderDynamicObjects = body.Bool("m_RenderDynamicObjects");
            m_Importance = body.Int("m_Importance", 1);
            var custom = body.Ref("m_CustomBakedTexture");
            m_CustomBakedTexture = custom.IsNull ? null : resolver.Resolve(custom, typeof(Texture)) as Texture;
            // Baked probes get their cubemaps from the scene's lighting data.
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var o = (ReflectionProbe)source;
            m_Type = o.m_Type;
            m_Mode = o.m_Mode;
            m_RefreshMode = o.m_RefreshMode;
            m_TimeSlicingMode = o.m_TimeSlicingMode;
            m_Resolution = o.m_Resolution;
            m_BoxSize = o.m_BoxSize;
            m_BoxOffset = o.m_BoxOffset;
            m_NearClip = o.m_NearClip;
            m_FarClip = o.m_FarClip;
            m_ShadowDistance = o.m_ShadowDistance;
            m_ClearFlags = o.m_ClearFlags;
            m_BackgroundColor = o.m_BackgroundColor;
            m_CullingMask = o.m_CullingMask;
            m_Intensity = o.m_Intensity;
            m_BlendDistance = o.m_BlendDistance;
            m_Hdr = o.m_Hdr;
            m_BoxProjection = o.m_BoxProjection;
            m_RenderDynamicObjects = o.m_RenderDynamicObjects;
            m_Importance = o.m_Importance;
            m_CustomBakedTexture = remap(o.m_CustomBakedTexture) as Texture;
            m_BakedTexture = remap(o.m_BakedTexture) as Texture;
        }
    }
}
