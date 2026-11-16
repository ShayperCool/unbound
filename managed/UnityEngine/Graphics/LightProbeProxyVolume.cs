using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A grid of light probes interpolated over a box and sampled per pixel by the renderers that
    /// use it (LightProbeUsage.UseProxyVolume), as the Unity 6000.0.71f1 Editor builds it
    /// (tests/projects/lppv, OpenGL Core, NVIDIA):
    /// <list type="bullet">
    /// <item>The box: Automatic Local and Automatic World enclose the renderers of the object and
    /// its children whose Light Probes use the volume (as the Manual's Light Probe Proxy Volume
    /// page says), in the object's local space or as a world-aligned box; with no such renderer a
    /// unit box (around the object's origin, or at its position). Custom is the custom size around
    /// the custom origin, in the object's local space.</item>
    /// <item>The resolution: Custom's, or per axis the box's size in the world times the density
    /// (at most 1 probe a unit), rounded, at least 1, to the nearest power of two (a tie upwards),
    /// at most 32.</item>
    /// <item>The probes: at the cells' centers; with Cell Corner the box grows by one cell, its
    /// size times resolution / (resolution - 1), so the probes sit on the box's corners.</item>
    /// <item>The texture (unity_ProbeVolumeSH): 4 * X by Y by Z texels, X wide blocks of the
    /// probes' red, green and blue L0 and L1 terms in unity_SHAr's layout, then their occlusion,
    /// 32-bit or (Half Float) 16-bit floats; the probes are those
    /// LightProbes.CalculateInterpolatedLightAndOcclusionProbes gives.</item>
    /// <item>A renderer drawn through it keeps the ambient probe's constants (Normal; Low: none).</item>
    /// <item>Custom bounds on a rotated or scaled object: the probes fill the box around the
    /// origin in the object's space (boundsGlobal says so too), while the shaders' world-to-object
    /// matrix offsets by the origin in the world's (tests/projects/lppv rotatedcustom); both as
    /// measured.</item>
    /// </list>
    /// A volume is built in the frame's PostLateUpdate.UpdateLightProbeProxyVolumes, so one made
    /// this frame lights nothing before it; Automatic rebuilds when what it is built from changes,
    /// Every Frame every frame, Via Scripting on <see cref="Update"/>.
    /// </summary>
    [ClassId(259)]
    public sealed partial class LightProbeProxyVolume : Behaviour
    {
        BoundingBoxMode m_BoundingBoxMode = BoundingBoxMode.AutomaticLocal;
        ResolutionMode m_ResolutionMode = ResolutionMode.Automatic;
        ProbePositionMode m_ProbePositionMode = ProbePositionMode.CellCorner;
        RefreshMode m_RefreshMode = RefreshMode.Automatic;
        QualityMode m_QualityMode = QualityMode.Normal;
        DataFormat m_DataFormat = DataFormat.Float;
        float m_ProbeDensity = 0.25f;
        int m_ResolutionX = 2, m_ResolutionY = 2, m_ResolutionZ = 2;
        Vector3 m_BoundingBoxSize = Vector3.one, m_BoundingBoxOrigin;

        public LightProbeProxyVolume() => Volumes.Add(this);

        public enum ResolutionMode { Automatic = 0, Custom = 1 }
        public enum BoundingBoxMode { AutomaticLocal = 0, AutomaticWorld = 1, Custom = 2 }
        public enum ProbePositionMode { CellCorner = 0, CellCenter = 1 }
        public enum RefreshMode { Automatic = 0, EveryFrame = 1, ViaScripting = 2 }
        public enum QualityMode { Low = 0, Normal = 1 }
        public enum DataFormat { HalfFloat = 0, Float = 1 }

        /// <summary>The Editor's OpenGL Core device supports proxy volumes (measured).</summary>
        [Unbound.EditorFaithful("the Editor's OpenGL Core device supports proxy volumes (measured: tests/projects/builtinfx lppv)")]
        public static bool isFeatureSupported => true;

        public BoundingBoxMode boundingBoxMode { get => m_BoundingBoxMode; set => m_BoundingBoxMode = value; }
        public ResolutionMode resolutionMode { get => m_ResolutionMode; set => m_ResolutionMode = value; }
        public ProbePositionMode probePositionMode { get => m_ProbePositionMode; set => m_ProbePositionMode = value; }
        public RefreshMode refreshMode { get => m_RefreshMode; set => m_RefreshMode = value; }
        public QualityMode qualityMode { get => m_QualityMode; set => m_QualityMode = value; }
        public DataFormat dataFormat { get => m_DataFormat; set => m_DataFormat = value; }
        public float probeDensity { get => m_ProbeDensity; set => m_ProbeDensity = Mathf.Clamp(value, 0.0001f, 1024f); }
        public Vector3 sizeCustom { get => m_BoundingBoxSize; set => m_BoundingBoxSize = value; }
        public Vector3 originCustom { get => m_BoundingBoxOrigin; set => m_BoundingBoxOrigin = value; }

        /// <summary>The custom resolution, whatever the mode (the Editor answers it in Automatic
        /// mode too, 2 by default; measured: tests/projects/lppv *.settings).</summary>
        public int gridResolutionX { get => m_ResolutionX; set => m_ResolutionX = Mathf.Clamp(value, 1, 32); }
        public int gridResolutionY { get => m_ResolutionY; set => m_ResolutionY = Mathf.Clamp(value, 1, 32); }
        public int gridResolutionZ { get => m_ResolutionZ; set => m_ResolutionZ = Mathf.Clamp(value, 1, 32); }

        /// <summary>The world-aligned box around the volume's box (before Cell Corner grows it).</summary>
        public Bounds boundsGlobal
        {
            get
            {
                var box = Box();
                if (box.Local) return Transformed(box.Center, box.Size, box.WorldToObject.inverse);
                return new Bounds(box.Center, box.Size);
            }
        }

        /// <summary>Builds the volume now (Via Scripting's refresh; the other modes' too).</summary>
        public void Update() => Build();

        // ---------------------------------------------------------------- building

        /// <summary>Every volume made, for the frame's refresh (destroyed ones leave it then).</summary>
        static readonly List<LightProbeProxyVolume> Volumes = Register();
        static readonly List<Renderer> s_Renderers = new List<Renderer>();

        static List<LightProbeProxyVolume> Register()
        {
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateLightProbeProxyVolumes), RefreshAll);
            return new List<LightProbeProxyVolume>();
        }

        Texture3D m_Texture;
        // Kept between builds (Every Frame builds every frame): the probes' positions, their
        // interpolated probes and occlusion, the texels.
        Vector3[] m_Positions = Array.Empty<Vector3>();
        SphericalHarmonicsL2[] m_Probes = Array.Empty<SphericalHarmonicsL2>();
        Vector4[] m_Occlusion = Array.Empty<Vector4>();
        float[] m_Texels = Array.Empty<float>();
        ushort[] m_Halves = Array.Empty<ushort>();
        Vector4 m_Params, m_SizeInv, m_Min;
        Matrix4x4 m_WorldToObject;
        Hash128 m_BuiltFrom;
        // The scene's probes it was built from (replaced as a whole when they change).
        object m_BuiltProbes;
        bool m_Built;

        /// <summary>What the shaders read of a built volume (<see cref="Apply"/>).</summary>
        internal bool Built => m_Built && m_Texture != null;

        /// <summary>Whether volumes are built and drawn through: the graphics tier compiles
        /// programs with them (TierSettings.enableLPPV; Android's automatic tiers do not). A render
        /// pipeline's draws use them only when they ask (PerObjectData.LightProbeProxyVolume; URP's
        /// do not: their renderers keep Blend Probes' probes, tests/projects/urp
        /// per-object-constants.jsonl, the Proxy Sphere).</summary>
        internal static bool Active => GraphicsSettings.HasShaderDefine(BuiltinShaderDefine.UNITY_LIGHT_PROBE_PROXY_VOLUME);

        internal override void OnEngineDestroy()
        {
            Volumes.Remove(this);
            if (m_Texture != null) DestroyImmediate(m_Texture);
            m_Texture = null;
            m_Built = false;
        }

        static void RefreshAll()
        {
            if (!Active) return;
            for (int i = Volumes.Count - 1; i >= 0; i--)
            {
                var volume = Volumes[i];
                if (volume == null)
                {
                    Volumes.RemoveAt(i);
                    continue;
                }
                if (!volume.isActiveAndEnabled) continue;
                switch (volume.m_RefreshMode)
                {
                    case RefreshMode.EveryFrame: volume.Build(); break;
                    case RefreshMode.Automatic:
                        if (!volume.m_Built || !ReferenceEquals(CurrentProbes(), volume.m_BuiltProbes) || volume.Inputs() != volume.m_BuiltFrom) volume.Build();
                        break;
                    // Via Scripting builds on Update() alone (a first build without one is unmeasured).
                    case RefreshMode.ViaScripting: if (!volume.m_Built) volume.Build(); break;
                }
            }
        }

        readonly struct VolumeBox
        {
            internal readonly Vector3 Center, Size;
            /// <summary>Whether the box is in the object's space (unity_ProbeVolumeParams.y).</summary>
            internal readonly bool Local;
            internal readonly Matrix4x4 WorldToObject;
            internal VolumeBox(Vector3 center, Vector3 size, bool local, Matrix4x4 worldToObject) =>
                (Center, Size, Local, WorldToObject) = (center, size, local, worldToObject);
        }

        /// <summary>The box the probes fill: in the object's space (Automatic Local; Custom, offset
        /// by its origin) or the world's (Automatic World).</summary>
        VolumeBox Box()
        {
            var worldToLocal = transform.worldToLocalMatrix;
            if (m_BoundingBoxMode == BoundingBoxMode.Custom)
                return new VolumeBox(Vector3.zero, m_BoundingBoxSize, true, Matrix4x4.Translate(-m_BoundingBoxOrigin) * worldToLocal);
            bool local = m_BoundingBoxMode == BoundingBoxMode.AutomaticLocal;
            s_Renderers.Clear();
            GetComponentsInChildren(false, s_Renderers);
            bool any = false;
            Vector3 min = default, max = default;
            foreach (var renderer in s_Renderers)
            {
                if (!renderer.enabled || renderer.lightProbeUsage != LightProbeUsage.UseProxyVolume) continue;
                var bounds = local ? Transformed(renderer.localBounds.center, renderer.localBounds.size, worldToLocal * renderer.localToWorldMatrix) : renderer.bounds;
                min = any ? Vector3.Min(min, bounds.min) : bounds.min;
                max = any ? Vector3.Max(max, bounds.max) : bounds.max;
                any = true;
            }
            s_Renderers.Clear();
            if (!any) return new VolumeBox(local ? Vector3.zero : transform.position, Vector3.one, local, worldToLocal);
            return new VolumeBox((min + max) * 0.5f, max - min, local, worldToLocal);
        }

        static Bounds Transformed(Vector3 center, Vector3 size, Matrix4x4 matrix)
        {
            var c = matrix.MultiplyPoint3x4(center);
            var e = size * 0.5f;
            var extents = new Vector3(
                Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
                Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
                Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
            return new Bounds(c, extents * 2f);
        }

        /// <summary>The cells per axis (see the class summary).</summary>
        Vector3Int Resolution(in VolumeBox box)
        {
            if (m_ResolutionMode == ResolutionMode.Custom) return new Vector3Int(m_ResolutionX, m_ResolutionY, m_ResolutionZ);
            var size = box.Size;
            if (box.Local)
            {
                var scale = transform.lossyScale;
                size = new Vector3(size.x * Mathf.Abs(scale.x), size.y * Mathf.Abs(scale.y), size.z * Mathf.Abs(scale.z));
            }
            float density = Mathf.Min(m_ProbeDensity, 1f);
            return new Vector3Int(Cells(size.x * density), Cells(size.y * density), Cells(size.z * density));
        }

        /// <summary>The probes' count along an axis: the nearest power of two (a tie upwards) to
        /// the rounded count, 1 to 32.</summary>
        internal static int Cells(float probes)
        {
            int n = Math.Max(1, (int)Math.Floor(probes + 0.5f));
            int lower = 1;
            while (lower * 2 <= n) lower *= 2;
            int cells = lower == n || n - lower < lower * 2 - n ? lower : lower * 2;
            return Math.Min(cells, 32);
        }

        static object CurrentProbes() => LightmapSettings.lightProbes?.m_Set;

        /// <summary>What the volume is built from, besides the scene's probes: its box, settings and
        /// transform.</summary>
        Hash128 Inputs()
        {
            var box = Box();
            var hash = new Hash128();
            hash.Append(box.Center.x); hash.Append(box.Center.y); hash.Append(box.Center.z);
            hash.Append(box.Size.x); hash.Append(box.Size.y); hash.Append(box.Size.z);
            for (int i = 0; i < 16; i++) hash.Append(box.WorldToObject[i]);
            var scale = transform.lossyScale;
            hash.Append(scale.x); hash.Append(scale.y); hash.Append(scale.z);
            hash.Append((int)m_ResolutionMode | (int)m_ProbePositionMode << 2 | (int)m_QualityMode << 4 | (int)m_DataFormat << 6 | (int)m_BoundingBoxMode << 8);
            hash.Append(m_ProbeDensity);
            hash.Append(m_ResolutionX); hash.Append(m_ResolutionY); hash.Append(m_ResolutionZ);
            return hash;
        }

        void Build()
        {
            var box = Box();
            var cells = Resolution(box);
            var size = box.Size;
            if (m_ProbePositionMode == ProbePositionMode.CellCorner)
                size = new Vector3(Grown(size.x, cells.x), Grown(size.y, cells.y), Grown(size.z, cells.z));
            var min = box.Center - size * 0.5f;
            var objectToWorld = box.WorldToObject.inverse;
            int count = cells.x * cells.y * cells.z;
            if (m_Positions.Length != count)
            {
                m_Positions = new Vector3[count];
                m_Probes = new SphericalHarmonicsL2[count];
                m_Occlusion = new Vector4[count];
            }
            var positions = m_Positions;
            for (int z = 0, i = 0; z < cells.z; z++)
                for (int y = 0; y < cells.y; y++)
                    for (int x = 0; x < cells.x; x++, i++)
                    {
                        var p = min + Vector3.Scale(new Vector3((x + 0.5f) / cells.x, (y + 0.5f) / cells.y, (z + 0.5f) / cells.z), size);
                        positions[i] = box.Local ? objectToWorld.MultiplyPoint3x4(p) : p;
                    }
            var probes = m_Probes;
            var occlusion = m_Occlusion;
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(positions, probes, occlusion);
            int width = 4 * cells.x;
            if (m_Texels.Length != 4 * width * cells.y * cells.z) m_Texels = new float[4 * width * cells.y * cells.z];
            var texels = m_Texels;
            for (int z = 0, i = 0; z < cells.z; z++)
                for (int y = 0; y < cells.y; y++)
                    for (int x = 0; x < cells.x; x++, i++)
                    {
                        int row = (z * cells.y + y) * width;
                        var sh = probes[i];
                        for (int c = 0; c < 3; c++)
                            Put(texels, row + c * cells.x + x, new Vector4(sh[c, 3], sh[c, 1], sh[c, 2], sh[c, 0] - sh[c, 6]));
                        Put(texels, row + 3 * cells.x + x, occlusion[i]);
                    }
            var format = m_DataFormat == DataFormat.Float ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf;
            if (m_Texture == null || m_Texture.width != width || m_Texture.height != cells.y || m_Texture.depth != cells.z || m_Texture.format != format)
            {
                if (m_Texture != null) DestroyImmediate(m_Texture);
                m_Texture = new Texture3D(width, cells.y, cells.z, format, false)
                {
                    name = "Light Probe Proxy Volume", hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
            }
            if (format == TextureFormat.RGBAFloat) m_Texture.SetPixelData(texels, 0);
            else
            {
                if (m_Halves.Length != texels.Length) m_Halves = new ushort[texels.Length];
                for (int i = 0; i < texels.Length; i++) m_Halves[i] = Mathf.FloatToHalf(texels[i]);
                m_Texture.SetPixelData(m_Halves, 0);
            }
            m_Texture.Apply(false);
            m_Params = new Vector4(1f, box.Local ? 1f : 0f, 1f / width, 0f);
            // Custom: the shaders get the origin in the world's space (see the class summary).
            m_WorldToObject = m_BoundingBoxMode == BoundingBoxMode.Custom
                ? transform.worldToLocalMatrix * Matrix4x4.Translate(-m_BoundingBoxOrigin)
                : box.WorldToObject;
            m_SizeInv = new Vector4(1f / size.x, 1f / size.y, 1f / size.z, 0f);
            m_Min = new Vector4(min.x, min.y, min.z, 0f);
            m_BuiltFrom = Inputs();
            m_BuiltProbes = CurrentProbes();
            m_Built = true;
        }

        static float Grown(float size, int cells) => cells > 1 ? size * cells / (cells - 1) : size;

        static void Put(float[] texels, int texel, Vector4 value)
        {
            texels[4 * texel] = value.x;
            texels[4 * texel + 1] = value.y;
            texels[4 * texel + 2] = value.z;
            texels[4 * texel + 3] = value.w;
        }

        // ---------------------------------------------------------------- drawing

        static readonly int s_ParamsId = Shader.PropertyToID("unity_ProbeVolumeParams");
        static readonly int s_WorldToObjectId = Shader.PropertyToID("unity_ProbeVolumeWorldToObject");
        static readonly int s_SizeInvId = Shader.PropertyToID("unity_ProbeVolumeSizeInv");
        static readonly int s_MinId = Shader.PropertyToID("unity_ProbeVolumeMin");
        static readonly int s_TextureId = Shader.PropertyToID("unity_ProbeVolumeSH");

        /// <summary>The volume a renderer that uses one samples: its override's, else its own.</summary>
        internal static LightProbeProxyVolume Of(Renderer renderer)
        {
            if (!Active) return null;
            var owner = renderer.lightProbeProxyVolumeOverride;
            var volume = owner != null ? owner.GetComponent<LightProbeProxyVolume>() : renderer.GetComponent<LightProbeProxyVolume>();
            return volume != null && volume.isActiveAndEnabled && volume.Built ? volume : null;
        }

        /// <summary>The volume's constants and texture on <paramref name="properties"/> (a copy the
        /// draw owns); with Low quality the renderer's probe constants are zero.</summary>
        internal void Apply(Dictionary<int, object> properties)
        {
            properties[s_ParamsId] = m_Params;
            properties[s_WorldToObjectId] = m_WorldToObject;
            properties[s_SizeInvId] = m_SizeInv;
            properties[s_MinId] = m_Min;
            properties[s_TextureId] = m_Texture;
        }

        internal bool LowQuality => m_QualityMode == QualityMode.Low;

        internal override void CopyFrom(Component source, System.Func<Object, Object> remap)
        {
            var o = (LightProbeProxyVolume)source;
            (m_BoundingBoxMode, m_ResolutionMode, m_ProbePositionMode, m_RefreshMode, m_QualityMode, m_DataFormat) =
                (o.m_BoundingBoxMode, o.m_ResolutionMode, o.m_ProbePositionMode, o.m_RefreshMode, o.m_QualityMode, o.m_DataFormat);
            (m_ProbeDensity, m_ResolutionX, m_ResolutionY, m_ResolutionZ, m_BoundingBoxSize, m_BoundingBoxOrigin) =
                (o.m_ProbeDensity, o.m_ResolutionX, o.m_ResolutionY, o.m_ResolutionZ, o.m_BoundingBoxSize, o.m_BoundingBoxOrigin);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_BoundingBoxMode = (BoundingBoxMode)body.Int("m_BoundingBoxMode");
            m_ResolutionMode = (ResolutionMode)body.Int("m_ResolutionMode");
            m_ProbePositionMode = (ProbePositionMode)body.Int("m_ProbePositionMode");
            m_RefreshMode = (RefreshMode)body.Int("m_RefreshMode");
            m_QualityMode = (QualityMode)body.Int("m_QualityMode", (int)QualityMode.Normal);
            m_DataFormat = (DataFormat)body.Int("m_DataFormat", (int)DataFormat.Float);
            m_ProbeDensity = body.Float("m_ResolutionProbesPerUnit", 0.25f);
            m_ResolutionX = body.Int("m_ResolutionX", 2);
            m_ResolutionY = body.Int("m_ResolutionY", 2);
            m_ResolutionZ = body.Int("m_ResolutionZ", 2);
            m_BoundingBoxSize = body.Get("m_BoundingBoxSize") != null ? body.Vector3("m_BoundingBoxSize") : Vector3.one;
            m_BoundingBoxOrigin = body.Vector3("m_BoundingBoxOrigin");
        }
    }
}
