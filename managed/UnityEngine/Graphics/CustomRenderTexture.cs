using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A render texture a material fills, initialized and updated by the engine in
    /// PostLateUpdate.UpdateCustomRenderTextures (tests/projects/customrt):
    /// <list type="bullet">
    /// <item>Initialization (OnLoad: the frame it is first seen; Realtime: every frame;
    /// <see cref="Initialize"/>: the next update point) fills the texture, both buffers when it
    /// is double buffered: with <see cref="initializationMaterial"/>'s first pass over the whole
    /// texture (Material source), else with <see cref="initializationTexture"/> times the color
    /// as a material color (Unity's Hidden/BlitCopy with <c>_Color</c>), else with the color as
    /// it is. Cubemaps initialize every face whatever <see cref="cubemapFaceMask"/>.</item>
    /// <item>Updates (OnLoad: once, the frame it is first seen; Realtime: every frame once
    /// <see cref="updatePeriod"/> seconds of game time have passed since the last; plus every
    /// <see cref="Update(int)"/> count) run after the frame's initialization. Each draws the
    /// update zones (the whole texture without zones) with Unity's CustomRenderTexture vertex
    /// program, sixteen zones per draw, each zone with its pass (-1: <see cref="shaderPass"/>).</item>
    /// <item>A double-buffered texture draws into its second buffer reading itself
    /// (<c>_SelfTexture2D</c>/<c>_SelfTextureCube</c>); the buffers swap before each update but
    /// the first after an initialization, and before each zone that asks for it. A single
    /// buffer binds no self texture.</item>
    /// <item>Textures update after the ones their materials read.</item>
    /// </list>
    /// </summary>
    [ClassId(86)]
    public sealed partial class CustomRenderTexture : RenderTexture, ISerializedAsset, Instantiation.ICloneable
    {
        Material m_Material;
        Material m_InitMaterial;
        Texture m_InitTexture;
        Color m_InitColor = Color.white;
        CustomRenderTextureInitializationSource m_InitSource = CustomRenderTextureInitializationSource.TextureAndColor;
        CustomRenderTextureUpdateMode m_UpdateMode = CustomRenderTextureUpdateMode.OnLoad;
        CustomRenderTextureUpdateMode m_InitMode = CustomRenderTextureUpdateMode.OnDemand;
        CustomRenderTextureUpdateZoneSpace m_ZoneSpace = CustomRenderTextureUpdateZoneSpace.Normalized;
        readonly List<CustomRenderTextureUpdateZone> m_Zones = new List<CustomRenderTextureUpdateZone>();
        float m_UpdatePeriod;
        int m_ShaderPass;
        uint m_CubemapFaceMask = 0xFFFFFFFF;
        bool m_DoubleBuffered;
        bool m_WrapZones;

        RenderTexture m_Other;
        // Scheduling state (CustomRenderTextureManager).
        internal bool m_Loaded;
        internal bool m_InitPending;
        internal int m_UpdatesPending;
        internal float m_SinceUpdate;
        internal bool m_UpdatedSinceInit;

        /// <summary>Depth 24 (D24S8) whatever the constructor (measured).</summary>
        const int DepthBits = 24;

        public CustomRenderTexture(int width, int height, RenderTextureFormat format, RenderTextureReadWrite readWrite)
            : base(width, height, DepthBits, format, readWrite) => CustomRenderTextureManager.Register(this);

        public CustomRenderTexture(int width, int height, RenderTextureFormat format)
            : this(width, height, format, RenderTextureReadWrite.Default) { }

        public CustomRenderTexture(int width, int height)
            : this(width, height, RenderTextureFormat.Default, RenderTextureReadWrite.Default) { }

        public CustomRenderTexture(int width, int height, DefaultFormat defaultFormat)
            : base(width, height, DepthBits, defaultFormat) => CustomRenderTextureManager.Register(this);

        public CustomRenderTexture(int width, int height, GraphicsFormat format)
            : base(width, height, DepthBits, format) => CustomRenderTextureManager.Register(this);

        /// <summary>A `.asset` custom render texture (registered when it is read).</summary>
        CustomRenderTexture() => CustomRenderTextureManager.Register(this);

        public Material material { get => m_Material; set => m_Material = value; }
        public Material initializationMaterial { get => m_InitMaterial; set => m_InitMaterial = value; }
        public Texture initializationTexture { get => m_InitTexture; set => m_InitTexture = value; }
        public CustomRenderTextureInitializationSource initializationSource { get => m_InitSource; set => m_InitSource = value; }
        public Color initializationColor { get => m_InitColor; set => m_InitColor = value; }
        public CustomRenderTextureUpdateMode updateMode { get => m_UpdateMode; set => m_UpdateMode = value; }
        public CustomRenderTextureUpdateMode initializationMode { get => m_InitMode; set => m_InitMode = value; }
        public CustomRenderTextureUpdateZoneSpace updateZoneSpace { get => m_ZoneSpace; set => m_ZoneSpace = value; }
        public int shaderPass { get => m_ShaderPass; set => m_ShaderPass = value; }
        public uint cubemapFaceMask { get => m_CubemapFaceMask; set => m_CubemapFaceMask = value; }
        public bool doubleBuffered { get => m_DoubleBuffered; set => m_DoubleBuffered = value; }
        public bool wrapUpdateZones { get => m_WrapZones; set => m_WrapZones = value; }
        public float updatePeriod { get => m_UpdatePeriod; set => m_UpdatePeriod = value; }

        /// <summary>Asks for <paramref name="count"/> updates at the next update point.</summary>
        public void Update(int count)
        {
            CustomRenderTextureManager.InvokeTriggerUpdate(this, count);
            m_UpdatesPending += count;
        }

        public void Update() => Update(1);

        /// <summary>Asks for an initialization at the next update point.</summary>
        public void Initialize()
        {
            CustomRenderTextureManager.InvokeTriggerInitialize(this);
            m_InitPending = true;
        }

        public void ClearUpdateZones() => m_Zones.Clear();

        public void SetUpdateZones(CustomRenderTextureUpdateZone[] updateZones)
        {
            if (updateZones == null) throw new ArgumentNullException(nameof(updateZones));
            m_Zones.Clear();
            m_Zones.AddRange(updateZones);
        }

        public void GetUpdateZones(List<CustomRenderTextureUpdateZone> updateZones)
        {
            if (updateZones == null) throw new ArgumentNullException(nameof(updateZones));
            updateZones.Clear();
            updateZones.AddRange(m_Zones);
        }

        internal void GetUpdateZonesInternal(object updateZones) => GetUpdateZones((List<CustomRenderTextureUpdateZone>)updateZones);

        /// <summary>The second buffer once a double-buffered texture has made it (with the texture's
        /// description and the default sampler; it stays after double buffering is turned off).</summary>
        public RenderTexture GetDoubleBufferRenderTexture() => m_Other;

        /// <summary>Makes the second buffer match the texture's description.</summary>
        public void EnsureDoubleBufferConsistency()
        {
            if (!m_DoubleBuffered) return;
            if (m_Other != null && SameDescription(m_Other.descriptor, descriptor)) return;
            if (m_Other != null)
            {
                m_Other.ReleaseQuietly();
                DestroyImmediate(m_Other);
            }
            using (ObjectRegistry.AsAssets())
                m_Other = new RenderTexture(descriptor) { name = name, hideFlags = HideFlags.HideAndDontSave };
            m_Other.Create();
        }

        static bool SameDescription(in RenderTextureDescriptor a, in RenderTextureDescriptor b) =>
            a.width == b.width && a.height == b.height && a.graphicsFormat == b.graphicsFormat && a.depthStencilFormat == b.depthStencilFormat
            && a.dimension == b.dimension && a.volumeDepth == b.volumeDepth && a.msaaSamples == b.msaaSamples && a.useMipMap == b.useMipMap
            && a.mipCount == b.mipCount;

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRenderTexture(body);
            m_Material = resolver.Resolve(body.Ref("m_Material"), typeof(Material)) as Material;
            m_InitMaterial = resolver.Resolve(body.Ref("m_InitMaterial"), typeof(Material)) as Material;
            m_InitTexture = resolver.Resolve(body.Ref("m_InitTexture"), typeof(Texture)) as Texture;
            m_InitSource = (CustomRenderTextureInitializationSource)body.Int("m_InitSource");
            m_InitColor = body.Color("m_InitColor", Color.white);
            m_UpdateMode = (CustomRenderTextureUpdateMode)body.Int("m_UpdateMode");
            m_InitMode = (CustomRenderTextureUpdateMode)body.Int("m_InitializationMode");
            m_ZoneSpace = (CustomRenderTextureUpdateZoneSpace)body.Int("m_UpdateZoneSpace");
            m_Zones.Clear();
            foreach (var zone in body.Get("m_UpdateZones")?.Seq() ?? Array.Empty<YNode>())
                m_Zones.Add(new CustomRenderTextureUpdateZone
                {
                    updateZoneCenter = zone.Vector3("updateZoneCenter"),
                    updateZoneSize = zone.Vector3("updateZoneSize"),
                    rotation = zone.Float("rotation"),
                    passIndex = zone.Int("passIndex"),
                    needSwap = zone.Bool("needSwap"),
                });
            m_UpdatePeriod = body.Float("m_UpdatePeriod");
            m_ShaderPass = body.Int("m_ShaderPass");
            m_CubemapFaceMask = unchecked((uint)body.Long("m_CubemapFaceMask", 0xFFFFFFFF));
            m_DoubleBuffered = body.Bool("m_DoubleBuffered");
            m_WrapZones = body.Bool("m_WrapUpdateZones");
        }

        Object Instantiation.ICloneable.CloneObject()
        {
            var copy = new CustomRenderTexture();
            copy.descriptor = descriptor;
            copy.SetSamplerQuietly(filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel, mipMapBias);
            copy.m_Material = m_Material;
            copy.m_InitMaterial = m_InitMaterial;
            copy.m_InitTexture = m_InitTexture;
            copy.m_InitColor = m_InitColor;
            copy.m_InitSource = m_InitSource;
            copy.m_UpdateMode = m_UpdateMode;
            copy.m_InitMode = m_InitMode;
            copy.m_ZoneSpace = m_ZoneSpace;
            copy.m_Zones.AddRange(m_Zones);
            copy.m_UpdatePeriod = m_UpdatePeriod;
            copy.m_ShaderPass = m_ShaderPass;
            copy.m_CubemapFaceMask = m_CubemapFaceMask;
            copy.m_DoubleBuffered = m_DoubleBuffered;
            copy.m_WrapZones = m_WrapZones;
            return copy;
        }

        internal override void OnEngineDestroy()
        {
            CustomRenderTextureManager.Unregister(this);
            if (m_Other != null)
            {
                m_Other.ReleaseQuietly();
                DestroyImmediate(m_Other);
                m_Other = null;
            }
            base.OnEngineDestroy();
        }

        /// <summary>Unloaded by Resources.UnloadAsset: no longer updated, its GPU data released.</summary>
        internal void UnloadAsset()
        {
            CustomRenderTextureManager.Unregister(this);
            m_Other?.ReleaseQuietly();
            ReleaseQuietly();
        }

        // ------------------------------------------------------------------ drawing

        static readonly int s_SelfTexture2D = Shader.PropertyToID("_SelfTexture2D");
        static readonly int s_SelfTextureCube = Shader.PropertyToID("_SelfTextureCube");
        static readonly int s_Info = Shader.PropertyToID("_CustomRenderTextureInfo");
        static readonly int s_Parameters = Shader.PropertyToID("CustomRenderTextureParameters");
        static readonly int s_Centers = Shader.PropertyToID("CustomRenderTextureCenters");
        static readonly int s_SizesAndRotations = Shader.PropertyToID("CustomRenderTextureSizesAndRotations");
        static readonly int s_PrimitiveIds = Shader.PropertyToID("CustomRenderTexturePrimitiveIDs");
        static readonly int s_Color = Shader.PropertyToID("_Color");
        /// <summary>Zones per draw (kCustomTextureBatchSize in UnityCustomRenderTexture.cginc).</summary>
        const int BatchSize = 16;
        const int VerticesPerZone = 6;

        static Material s_InitBlit;
        bool m_ReportedVolume, m_ReportedInitTexture, m_ReportedPass;

        int Faces => dimension == TextureDimension.Cube ? 6 : 1;

        /// <summary>3D targets are not drawn into by the renderer yet: the texture is left as it is,
        /// with an error.</summary>
        bool Unsupported()
        {
            if (dimension != TextureDimension.Tex3D) return false;
            if (!m_ReportedVolume)
            {
                m_ReportedVolume = true;
                Debug.LogError($"CustomRenderTexture '{name}' is a 3D texture: Unbound does not render into 3D render textures yet, so it is neither initialized nor updated.");
            }
            return true;
        }

        /// <summary>Creates the texture when it is first seen (the second buffer comes with the
        /// first initialization or update: measured, a double-buffered texture never initialized
        /// nor updated has none).</summary>
        internal void Load() => EnsureCreated();

        /// <summary>Fills the texture (both buffers when double buffered).</summary>
        internal void RunInitialization()
        {
            m_UpdatedSinceInit = false;
            if (Unsupported()) return;
            EnsureCreated();
            EnsureDoubleBufferConsistency();
            ulong target = EnsureCreated();
            var saved = GfxStream.Current;
            for (int face = 0; face < Faces; face++)
                InitializeFace(target, face);
            if (m_DoubleBuffered && m_Other != null)
            {
                CopyLevels(target, m_Other.EnsureCreated());
            }
            Restore(saved);
        }

        void InitializeFace(ulong target, int face)
        {
            var properties = Variables(face, 0f);
            if (m_InitSource == CustomRenderTextureInitializationSource.Material && m_InitMaterial != null)
            {
                DrawQuad(target, face, m_InitMaterial, 0, properties);
                return;
            }
            if (m_InitTexture == null)
            {
                // The color as it is (not converted to the color space).
                GfxStream.SetTarget(target, 0, 0, face);
                GfxStream.Clear(1, m_InitColor);
                return;
            }
            if (m_InitTexture.dimension != TextureDimension.Tex2D)
            {
                if (!m_ReportedInitTexture)
                {
                    m_ReportedInitTexture = true;
                    Debug.LogError($"CustomRenderTexture '{name}': initialization from a {m_InitTexture.dimension} texture is not supported; it is initialized with its color.");
                }
                GfxStream.SetTarget(target, 0, 0, face);
                GfxStream.Clear(1, m_InitColor);
                return;
            }
            if (!s_InitBlit)
                using (ObjectRegistry.AsAssets())
                    s_InitBlit = new Material(Shader.Find("Hidden/BlitCopy")) { hideFlags = HideFlags.HideAndDontSave };
            s_InitBlit.SetTexture("_MainTex", m_InitTexture);
            s_InitBlit.SetColor(s_Color, m_InitColor);
            DrawQuad(target, face, s_InitBlit, 0, properties);
        }

        static void DrawQuad(ulong target, int face, Material material, int pass, Dictionary<int, object> properties)
        {
            ulong mesh = Immediate.BlitQuad.Sync();
            ulong handle = material.Sync();
            GfxStream.SetTarget(target, 0, 0, face);
            GfxStream.Camera(Matrix4x4.identity, Immediate.Ortho);
            GfxStream.Draw(mesh, 0, handle, pass, Matrix4x4.identity, Color.white, properties: properties);
        }

        static void Restore(in GfxStream.State saved)
        {
            GfxStream.Camera(saved.View, saved.Projection, saved.Flags);
            var active = ActiveTarget;
            GfxStream.SetTarget(active != null ? active.EnsureCreated() : 0);
        }

        /// <summary>The variables Unity's include reads, for a face: _CustomRenderTextureInfo
        /// (width, height, depth 1, face) and CustomRenderTextureParameters (pixel space, the
        /// face as the third texcoord, not 3D) (measured for 2D textures and cubemaps).</summary>
        Dictionary<int, object> Variables(int face, float space)
        {
            return new Dictionary<int, object>
            {
                [s_Info] = new Vector4(width, height, 1, face),
                [s_Parameters] = new Vector4(space, face, 0, 0),
            };
        }

        /// <summary>Runs <paramref name="count"/> updates.</summary>
        internal void RunUpdates(int count)
        {
            if (count <= 0 || m_Material == null || Unsupported()) return;
            EnsureCreated();
            EnsureDoubleBufferConsistency();
            var saved = GfxStream.Current;
            var zones = ExpandedZones(out float space);
            for (int i = 0; i < count; i++)
            {
                if (m_UpdatedSinceInit) Swap();
                m_UpdatedSinceInit = true;
                DrawZones(zones, space);
            }
            Restore(saved);
        }

        struct DrawZone
        {
            internal Vector4 Center, SizeAndRotation;
            internal int Id, Pass;
            internal bool Swap;
        }

        /// <summary>The zones to draw: the texture's (the whole texture without any), each with
        /// its copies across the edges when the zones wrap.</summary>
        List<DrawZone> ExpandedZones(out float space)
        {
            var result = new List<DrawZone>();
            bool pixels = m_ZoneSpace == CustomRenderTextureUpdateZoneSpace.Pixel;
            space = pixels ? 1 : 0;
            if (m_Zones.Count == 0)
            {
                space = 0;
                result.Add(new DrawZone { Center = new Vector4(0.5f, 0.5f, 0.5f, 0), SizeAndRotation = new Vector4(1, 1, 1, 0), Id = 0, Pass = m_ShaderPass });
                return result;
            }
            float w = pixels ? width : 1, h = pixels ? height : 1;
            for (int i = 0; i < m_Zones.Count; i++)
            {
                var z = m_Zones[i];
                var zone = new DrawZone
                {
                    Center = new Vector4(z.updateZoneCenter.x, z.updateZoneCenter.y, z.updateZoneCenter.z, 0),
                    SizeAndRotation = new Vector4(z.updateZoneSize.x, z.updateZoneSize.y, z.updateZoneSize.z, z.rotation),
                    Id = i,
                    Pass = z.passIndex == -1 ? m_ShaderPass : z.passIndex,
                    Swap = z.needSwap,
                };
                result.Add(zone);
                if (!m_WrapZones) continue;
                // The zone's extent with its rotation, in the zone space.
                float angle = z.rotation * Mathf.Deg2Rad;
                float c = Mathf.Abs(Mathf.Cos(angle)), s = Mathf.Abs(Mathf.Sin(angle));
                float halfX = 0.5f * (z.updateZoneSize.x * c + z.updateZoneSize.y * s);
                float halfY = 0.5f * (z.updateZoneSize.x * s + z.updateZoneSize.y * c);
                var xs = Offsets(z.updateZoneCenter.x, halfX, w);
                var ys = Offsets(z.updateZoneCenter.y, halfY, h);
                foreach (var dx in xs)
                    foreach (var dy in ys)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var copy = zone;
                        copy.Center.x += dx;
                        copy.Center.y += dy;
                        copy.Swap = false;
                        result.Add(copy);
                    }
            }
            return result;
        }

        static List<float> Offsets(float center, float half, float extent)
        {
            var list = new List<float> { 0 };
            if (center - half < 0) list.Add(extent);
            if (center + half > extent) list.Add(-extent);
            return list;
        }

        void DrawZones(List<DrawZone> zones, float space)
        {
            int start = 0;
            while (start < zones.Count)
            {
                if (zones[start].Swap) Swap();
                int end = start + 1;
                while (end < zones.Count && end - start < BatchSize && zones[end].Pass == zones[start].Pass && !zones[end].Swap) end++;
                DrawBatch(zones, start, end, space);
                start = end;
            }
        }

        void DrawBatch(List<DrawZone> zones, int start, int end, float space)
        {
            int pass = zones[start].Pass;
            if (pass < 0 || (m_Material.shader != null && pass >= m_Material.passCount))
            {
                if (!m_ReportedPass)
                {
                    m_ReportedPass = true;
                    Debug.LogError($"CustomRenderTexture '{name}': material '{m_Material.name}' has no pass {pass}.");
                }
                return;
            }
            int n = end - start;
            var centers = new Vector4[BatchSize];
            var sizes = new Vector4[BatchSize];
            var ids = new float[BatchSize];
            for (int i = 0; i < n; i++)
            {
                centers[i] = zones[start + i].Center;
                sizes[i] = zones[start + i].SizeAndRotation;
                // The include reads the ids with asuint.
                ids[i] = BitConverter.Int32BitsToSingle(zones[start + i].Id);
            }
            var mesh = ProceduralMeshes.Get(MeshTopology.Triangles, n * VerticesPerZone).Sync();
            ulong material = m_Material.Sync();
            bool selfBound = m_DoubleBuffered && m_Other != null;
            ulong target = selfBound ? m_Other.EnsureCreated() : EnsureCreated();
            for (int face = 0; face < Faces; face++)
            {
                if (dimension == TextureDimension.Cube && (m_CubemapFaceMask & (1u << face)) == 0) continue;
                var properties = Variables(face, space);
                properties[s_Centers] = centers;
                properties[s_SizesAndRotations] = sizes;
                properties[s_PrimitiveIds] = ids;
                if (selfBound) properties[dimension == TextureDimension.Cube ? s_SelfTextureCube : s_SelfTexture2D] = (Texture)this;
                GfxStream.SetTarget(target, 0, 0, face);
                GfxStream.Camera(Matrix4x4.identity, Matrix4x4.identity);
                GfxStream.Draw(mesh, 0, material, pass, Matrix4x4.identity, Color.white, properties: properties);
            }
        }

        static RenderTexture s_SwapScratch;

        /// <summary>Exchanges the contents of the two buffers (each keeps its object and handle).</summary>
        internal void Swap()
        {
            if (!m_DoubleBuffered || m_Other == null) return;
            var d = descriptor;
            if (s_SwapScratch == null || !SameDescription(s_SwapScratch.descriptor, d))
            {
                if (s_SwapScratch != null)
                {
                    s_SwapScratch.ReleaseQuietly();
                    DestroyImmediate(s_SwapScratch);
                }
                using (ObjectRegistry.AsAssets())
                    s_SwapScratch = new RenderTexture(d) { name = "CustomRenderTexture swap", hideFlags = HideFlags.HideAndDontSave };
            }
            ulong a = EnsureCreated(), b = m_Other.EnsureCreated(), t = s_SwapScratch.EnsureCreated();
            CopyLevels(a, t);
            CopyLevels(b, a);
            CopyLevels(t, b);
        }

        /// <summary>Copies every face and every mip of this texture's description between two
        /// textures of that description. Updates draw into mip 0 only (the renderer regenerates an
        /// auto-generated chain after the pass), so the whole chain is copied: a copy of mip 0 alone
        /// would leave the other buffer's lower mips with older content.</summary>
        void CopyLevels(ulong from, ulong to)
        {
            var whole = new RectInt(0, 0, -1, -1);
            int mips = mipmapCount;
            for (int face = 0; face < Faces; face++)
                for (int mip = 0; mip < mips; mip++)
                    GfxStream.CopyTexture(from, face, mip, whole, to, face, mip, 0, 0);
        }

        /// <summary>The custom render textures and render textures this one's materials read.</summary>
        internal IEnumerable<Texture> Inputs()
        {
            foreach (var m in new[] { m_Material, m_InitMaterial })
                if (m != null)
                    foreach (var slot in m.m_Textures.Values)
                        if (slot.Texture != null) yield return slot.Texture;
            if (m_InitTexture != null) yield return m_InitTexture;
        }
    }

    public enum CustomRenderTextureInitializationSource
    {
        TextureAndColor = 0,
        Material = 1,
    }

    public enum CustomRenderTextureUpdateMode
    {
        OnLoad = 0,
        Realtime = 1,
        OnDemand = 2,
    }

    public enum CustomRenderTextureUpdateZoneSpace
    {
        Normalized = 0,
        Pixel = 1,
    }

    /// <summary>A part of a custom render texture an update draws: center and size (z for
    /// volumes) in the zone space, rotation in degrees, the pass (-1: the texture's) and whether
    /// the buffers swap before it.</summary>
    [Serializable]
    public struct CustomRenderTextureUpdateZone
    {
        public Vector3 updateZoneCenter;
        public Vector3 updateZoneSize;
        public float rotation;
        public int passIndex;
        public bool needSwap;
    }

    /// <summary>The custom render textures the engine updates, and their events.</summary>
    public static partial class CustomRenderTextureManager
    {
        static readonly List<CustomRenderTexture> s_Textures = new List<CustomRenderTexture>();
        static bool s_Registered;

        public static event Action<CustomRenderTexture> textureLoaded;
        public static event Action<CustomRenderTexture> textureUnloaded;
        public static event Action<CustomRenderTexture, int> updateTriggered;
        public static event Action<CustomRenderTexture> initializeTriggered;

        internal static void Register(CustomRenderTexture texture)
        {
            if (!s_Registered)
            {
                s_Registered = true;
                PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateCustomRenderTextures), UpdateAll);
            }
            s_Textures.Add(texture);
        }

        internal static void Unregister(CustomRenderTexture texture)
        {
            if (!s_Textures.Remove(texture)) return;
            if (texture.m_Loaded) textureUnloaded?.Invoke(texture);
            texture.m_Loaded = false;
        }

        /// <summary>The loaded textures (seen by an update point and not unloaded since).</summary>
        public static void GetAllCustomRenderTextures(List<CustomRenderTexture> currentCustomRenderTextures)
        {
            if (currentCustomRenderTextures == null) throw new ArgumentNullException(nameof(currentCustomRenderTextures));
            currentCustomRenderTextures.Clear();
            foreach (var t in s_Textures)
                if (t != null && t.m_Loaded) currentCustomRenderTextures.Add(t);
        }

        internal static void InvokeTriggerUpdate(CustomRenderTexture crt, int updateCount) => updateTriggered?.Invoke(crt, updateCount);
        internal static void InvokeTriggerInitialize(CustomRenderTexture crt) => initializeTriggered?.Invoke(crt);

        /// <summary>PostLateUpdate.UpdateCustomRenderTextures: newly seen textures load (and run
        /// their OnLoad initialization and update), then each texture, after those its materials
        /// read, initializes and updates as its modes and requests say.</summary>
        static void UpdateAll()
        {
            if (s_Textures.Count == 0) return;
            s_Textures.RemoveAll(t => t == null);
            var fresh = new HashSet<CustomRenderTexture>();
            foreach (var t in s_Textures.ToArray())
            {
                if (t.m_Loaded) continue;
                t.m_Loaded = true;
                fresh.Add(t);
                textureLoaded?.Invoke(t);
            }
            float delta = Time.deltaTime;
            foreach (var t in Ordered())
            {
                if (t == null || !t.m_Loaded) continue;
                bool first = fresh.Contains(t);
                if (first) t.Load();
                bool init = t.m_InitPending || t.initializationMode == CustomRenderTextureUpdateMode.Realtime
                    || (first && t.initializationMode == CustomRenderTextureUpdateMode.OnLoad);
                t.m_InitPending = false;
                if (init) t.RunInitialization();
                int updates = t.m_UpdatesPending;
                t.m_UpdatesPending = 0;
                if (first && t.updateMode == CustomRenderTextureUpdateMode.OnLoad) updates++;
                if (t.updateMode == CustomRenderTextureUpdateMode.Realtime)
                {
                    t.m_SinceUpdate += delta;
                    if (t.m_SinceUpdate >= t.updatePeriod)
                    {
                        t.m_SinceUpdate = 0;
                        updates++;
                    }
                }
                t.RunUpdates(updates);
            }
        }

        /// <summary>The textures in registration order, each after the ones it reads.</summary>
        static List<CustomRenderTexture> Ordered()
        {
            var order = new List<CustomRenderTexture>(s_Textures.Count);
            var state = new Dictionary<CustomRenderTexture, int>();
            void Visit(CustomRenderTexture t)
            {
                if (state.TryGetValue(t, out var s)) return;
                state[t] = 1;
                foreach (var input in t.Inputs())
                    if (input is CustomRenderTexture dependency && dependency != t && s_Textures.Contains(dependency))
                        Visit(dependency);
                state[t] = 2;
                order.Add(t);
            }
            foreach (var t in s_Textures.ToArray())
                if (t != null) Visit(t);
            return order;
        }
    }
}
