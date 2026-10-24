using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>What every renderer has: materials, sorting, visibility, bounds.</summary>
    public partial class Renderer : Component
    {
        internal bool m_Enabled = true;
        internal Material[] m_Materials = Array.Empty<Material>();
        /// <summary>Instances `material`/`materials` made for this renderer.</summary>
        internal readonly HashSet<Material> m_OwnedMaterials = new HashSet<Material>();
        internal int m_SortingLayerID;
        internal short m_SortingOrder;
        internal MaterialPropertyBlock m_PropertyBlock;
        ShadowCastingMode m_CastShadows = ShadowCastingMode.On;
        bool m_ReceiveShadows = true;
        uint m_RenderingLayerMask = 1;
        bool m_IsVisible;
        internal bool m_Registered;

        public Renderer() { }

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value) return;
                m_Enabled = value;
                if (m_GameObject != null && m_GameObject.activeInHierarchy)
                {
                    if (value) OnEngineEnable();
                    else OnEngineDisable();
                }
            }
        }

        internal override void OnEngineEnable()
        {
            if (!m_Enabled || m_Registered) return;
            m_Registered = true;
            RenderRegistry.Renderers.Add(this);
        }

        internal override void OnEngineDisable()
        {
            if (!m_Registered) return;
            m_Registered = false;
            RenderRegistry.Renderers.Remove(this);
            if (m_IsVisible)
            {
                m_IsVisible = false;
            }
        }

        internal override void OnEngineDestroy()
        {
            OnEngineDisable();
            foreach (var m in m_OwnedMaterials) if (m) Destroy(m);
            m_OwnedMaterials.Clear();
            if (m_BlockMaterials != null)
                foreach (var b in m_BlockMaterials) if (b.Drawn) Destroy(b.Drawn);
            m_BlockMaterials = null;
        }

        public bool isVisible => m_IsVisible;
        public bool forceRenderingOff { get; set; }
        public ShadowCastingMode shadowCastingMode { get => m_CastShadows; set => m_CastShadows = value; }
        public bool receiveShadows { get => m_ReceiveShadows; set => m_ReceiveShadows = value; }
        public bool staticShadowCaster { get; set; }
        public MotionVectorGenerationMode motionVectorGenerationMode { get; set; } = MotionVectorGenerationMode.Object;
        public LightProbeUsage lightProbeUsage { get; set; } = LightProbeUsage.BlendProbes;
        public ReflectionProbeUsage reflectionProbeUsage { get; set; } = ReflectionProbeUsage.BlendProbes;
        public uint renderingLayerMask { get => m_RenderingLayerMask; set => m_RenderingLayerMask = value; }
        public int rendererPriority { get; set; }
        public bool allowOcclusionWhenDynamic { get; set; } = true;
        public bool isPartOfStaticBatch => false;
        public int lightmapIndex { get; set; } = -1;
        public int realtimeLightmapIndex { get; set; } = -1;
        public Vector4 lightmapScaleOffset { get; set; } = new Vector4(1, 1, 0, 0);
        public Vector4 realtimeLightmapScaleOffset { get; set; } = new Vector4(1, 1, 0, 0);
        public Transform probeAnchor { get; set; }
        public GameObject lightProbeProxyVolumeOverride { get; set; }
        public Matrix4x4 localToWorldMatrix => transform.localToWorldMatrix;
        public Matrix4x4 worldToLocalMatrix => transform.worldToLocalMatrix;

        public int sortingLayerID
        {
            get => m_SortingLayerID;
            set => m_SortingLayerID = value;
        }

        public string sortingLayerName
        {
            get => SortingLayer.IDToName(m_SortingLayerID);
            set => m_SortingLayerID = SortingLayer.NameToID(value);
        }

        public int sortingOrder
        {
            get => m_SortingOrder;
            set => m_SortingOrder = (short)Mathf.Clamp(value, short.MinValue, short.MaxValue);
        }

        internal int sortingGroupID { get; set; }
        internal int sortingGroupOrder { get; set; }

        // ------------------------------------------------------------------ materials

        public Material sharedMaterial
        {
            get => m_Materials.Length > 0 ? m_Materials[0] : null;
            set
            {
                if (m_Materials.Length == 0) m_Materials = new Material[1];
                m_Materials[0] = value;
            }
        }

        public Material[] sharedMaterials
        {
            get => (Material[])m_Materials.Clone();
            set => m_Materials = value != null ? (Material[])value.Clone() : Array.Empty<Material>();
        }

        /// <summary>The first material, instantiated for this renderer on first access.</summary>
        public Material material
        {
            get
            {
                if (m_Materials.Length == 0) return null;
                m_Materials[0] = Own(m_Materials[0]);
                return m_Materials[0];
            }
            set => sharedMaterial = value;
        }

        public Material[] materials
        {
            get
            {
                for (int i = 0; i < m_Materials.Length; i++) m_Materials[i] = Own(m_Materials[i]);
                return (Material[])m_Materials.Clone();
            }
            set => sharedMaterials = value;
        }

        Material Own(Material shared)
        {
            if (shared == null || m_OwnedMaterials.Contains(shared)) return shared;
            var instance = new Material(shared) { name = shared.name + " (Instance)" };
            m_OwnedMaterials.Add(instance);
            return instance;
        }

        public void GetMaterials(List<Material> m)
        {
            m.Clear();
            m.AddRange(materials);
        }

        public void GetSharedMaterials(List<Material> m)
        {
            m.Clear();
            m.AddRange(m_Materials);
        }

        public void SetMaterials(List<Material> materials) => sharedMaterials = materials.ToArray();

        public void SetSharedMaterials(List<Material> materials) => sharedMaterials = materials.ToArray();

        public bool HasPropertyBlock() => m_PropertyBlock != null && !m_PropertyBlock.isEmpty;

        /// <summary>Per material slot: the material drawn under the property block, and what it was
        /// made from (source, its version, the block).</summary>
        (Material Drawn, Material Source, int Version, MaterialPropertyBlock Block)[] m_BlockMaterials;

        /// <summary>The material slot <paramref name="index"/> draws with: the material itself, or,
        /// under a property block, a copy with the block's values (Unity applies them per draw).</summary>
        internal Material DrawnMaterial(int index, Material source)
        {
            var block = m_PropertyBlock;
            if (block == null || block.isEmpty || source == null) return source;
            if (m_BlockMaterials == null || m_BlockMaterials.Length <= index)
                Array.Resize(ref m_BlockMaterials, Math.Max(index + 1, m_Materials.Length));
            var cached = m_BlockMaterials[index];
            if (cached.Drawn != null && ReferenceEquals(cached.Source, source) && cached.Version == source.m_Version && ReferenceEquals(cached.Block, block))
                return cached.Drawn;
            if (cached.Drawn != null) DestroyImmediate(cached.Drawn);
            Material drawn;
            using (ObjectRegistry.AsAssets())
                drawn = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            foreach (var p in block.m_Values)
            {
                switch (p.Value)
                {
                    case float f: drawn.SetFloat(p.Key, f); break;
                    case int i: drawn.SetInteger(p.Key, i); break;
                    case Vector4 v: drawn.SetVector(p.Key, v); break;
                    case Matrix4x4 m: drawn.SetMatrix(p.Key, m); break;
                    case Texture t: drawn.SetTexture(p.Key, t); break;
                }
            }
            m_BlockMaterials[index] = (drawn, source, source.m_Version, block);
            return drawn;
        }

        public void SetPropertyBlock(MaterialPropertyBlock properties) => m_PropertyBlock = properties?.Copy();

        public void SetPropertyBlock(MaterialPropertyBlock properties, int materialIndex) => SetPropertyBlock(properties);

        public void GetPropertyBlock(MaterialPropertyBlock properties)
        {
            properties.Clear();
            if (m_PropertyBlock != null) properties.CopyFrom(m_PropertyBlock);
        }

        public void GetPropertyBlock(MaterialPropertyBlock properties, int materialIndex) => GetPropertyBlock(properties);

        public void GetClosestReflectionProbes(List<Rendering.ReflectionProbeBlendInfo> result) => result.Clear();

        public void ResetBounds() { }
        public void ResetLocalBounds() { }

        // ------------------------------------------------------------------ bounds

        public Bounds bounds
        {
            get
            {
                var local = localBounds;
                var m = transform.localToWorldMatrix;
                var c = m.MultiplyPoint3x4(local.center);
                var e = local.extents;
                var x = m.MultiplyVector(new Vector3(e.x, 0, 0));
                var y = m.MultiplyVector(new Vector3(0, e.y, 0));
                var z = m.MultiplyVector(new Vector3(0, 0, e.z));
                var extents = new Vector3(
                    Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
                return new Bounds(c, extents * 2f);
            }
            set { }
        }

        public virtual Bounds localBounds
        {
            get => GetLocalBounds();
            set { }
        }

        internal virtual Bounds GetLocalBounds() => new Bounds(Vector3.zero, Vector3.zero);

        // ------------------------------------------------------------------ rendering

        /// <summary>Adds this renderer's draws for a camera. Overridden by each renderer kind.</summary>
        internal virtual void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into) { }

        static readonly List<Renderer> s_WillRender = new List<Renderer>();

        /// <summary>OnWillRenderObject to the enabled scripts on the objects of the renderers a camera
        /// sees (its culling mask lets them through and their bounds touch its frustum), before any
        /// of them is drawn (Unity sends it from the camera's culling, once per camera). Scripts may
        /// change the scene meanwhile.</summary>
        internal static void SendWillRenderObject(Matrix4x4 viewProjection, int cullingMask)
        {
            s_WillRender.Clear();
            s_WillRender.AddRange(RenderRegistry.Renderers);
            foreach (var renderer in s_WillRender)
            {
                if (!renderer || renderer.forceRenderingOff || !renderer.m_Enabled) continue;
                var go = renderer.m_GameObject;
                if ((cullingMask & (1 << go.m_Layer)) == 0 || !go.activeInHierarchy) continue;
                if (!FrameBuilder.InFrustum(viewProjection, renderer.bounds)) continue;
                var components = go.m_Components;
                for (int i = 0; i < components.Count; i++)
                {
                    if (!(components[i] is MonoBehaviour script) || !script.isActiveAndEnabled) continue;
                    var call = ScriptMessages.For(script.GetType()).OnWillRenderObject;
                    if (call == null) continue;
                    try { call(script); }
                    catch (Exception e) { Debug.LogException(e, script); }
                }
            }
            s_WillRender.Clear();
        }

        internal void SetVisible(bool visible)
        {
            if (m_IsVisible == visible) return;
            m_IsVisible = visible;
            Messaging.SendFast(m_GameObject, visible ? "OnBecameVisible" : "OnBecameInvisible");
        }

        internal void ReadRendererData(YNode body, IReferenceResolver resolver)
        {
            m_Enabled = body.Bool("m_Enabled", true);
            var list = new List<Material>();
            foreach (var item in body.Get("m_Materials")?.Seq() ?? Array.Empty<YNode>())
            {
                var r = item.AsRef();
                list.Add(r.IsNull ? null : resolver.Resolve(r, typeof(Material)) as Material);
            }
            m_Materials = list.ToArray();
            m_SortingLayerID = body.Int("m_SortingLayerID");
            m_SortingOrder = (short)body.Int("m_SortingOrder");
            m_CastShadows = (ShadowCastingMode)body.Int("m_CastShadows", 1);
            m_ReceiveShadows = body.Bool("m_ReceiveShadows", true);
            m_RenderingLayerMask = (uint)body.Long("m_RenderingLayerMask", 1);
        }

        internal void CopyRendererData(Renderer source)
        {
            m_Enabled = source.m_Enabled;
            m_Materials = (Material[])source.m_Materials.Clone();
            m_SortingLayerID = source.m_SortingLayerID;
            m_SortingOrder = source.m_SortingOrder;
            m_CastShadows = source.m_CastShadows;
            m_ReceiveShadows = source.m_ReceiveShadows;
            m_RenderingLayerMask = source.m_RenderingLayerMask;
            m_PropertyBlock = source.m_PropertyBlock?.Copy();
        }
    }

    [ClassId(33)]
    public sealed partial class MeshFilter : Component
    {
        internal Mesh m_Mesh;
        bool m_OwnsMesh;

        public MeshFilter() { }

        public Mesh sharedMesh
        {
            get => m_Mesh;
            set
            {
                m_Mesh = value;
                m_OwnsMesh = false;
            }
        }

        /// <summary>The mesh, instantiated for this filter on first access.</summary>
        public Mesh mesh
        {
            get
            {
                if (m_Mesh == null) return null;
                if (!m_OwnsMesh)
                {
                    m_Mesh = (Mesh)((Instantiation.ICloneable)m_Mesh).CloneObject();
                    m_Mesh.name = m_Mesh.name + " Instance";
                    m_OwnsMesh = true;
                }
                return m_Mesh;
            }
            set => sharedMesh = value;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var r = body.Ref("m_Mesh");
            m_Mesh = r.IsNull ? null : resolver.Resolve(r, typeof(Mesh)) as Mesh;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (MeshFilter)source;
            m_Mesh = s.m_Mesh;
        }
    }

    [ClassId(23)]
    public partial class MeshRenderer : Renderer
    {
        public MeshRenderer() { }

        public Mesh additionalVertexStreams { get; set; }
        public Mesh enlightenVertexStream { get; set; }
        public int subMeshStartIndex => 0;
        public float scaleInLightmap { get; set; } = 1f;
        public ReceiveGI receiveGI { get; set; } = ReceiveGI.Lightmaps;
        public bool stitchLightmapSeams { get; set; }

        internal override Bounds GetLocalBounds()
        {
            var filter = m_GameObject.GetComponent<MeshFilter>();
            return filter != null && filter.m_Mesh != null ? filter.m_Mesh.bounds : default;
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var filter = m_GameObject.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.m_Mesh : null;
            if (mesh == null) return;
            FrameBuilder.AddMeshDraws(this, mesh, transform.localToWorldMatrix, Color.white, camera, into);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver) => ReadRendererData(body, resolver);

        internal override void CopyFrom(Component source, Func<Object, Object> remap) => CopyRendererData((Renderer)source);
    }

    [ClassId(212)]
    public sealed partial class SpriteRenderer : Renderer
    {
        Sprite m_Sprite;
        Color m_Color = Color.white;
        bool m_FlipX;
        bool m_FlipY;
        SpriteDrawMode m_DrawMode;
        Vector2 m_Size = Vector2.one;
        SpriteMaskInteraction m_MaskInteraction;
        SpriteTileMode m_TileMode;
        float m_AdaptiveModeThreshold = 0.5f;
        SpriteSortPoint m_SpriteSortPoint;
        Mesh m_SlicedMesh;
        (Sprite, Vector2, SpriteDrawMode) m_SlicedKey;

        public SpriteRenderer() { }

        public Sprite sprite
        {
            get => m_Sprite;
            set => m_Sprite = value;
        }

        public Color color { get => m_Color; set => m_Color = value; }
        public bool flipX { get => m_FlipX; set => m_FlipX = value; }
        public bool flipY { get => m_FlipY; set => m_FlipY = value; }
        public SpriteDrawMode drawMode { get => m_DrawMode; set => m_DrawMode = value; }
        public Vector2 size { get => m_Size; set => m_Size = value; }
        public SpriteMaskInteraction maskInteraction { get => m_MaskInteraction; set => m_MaskInteraction = value; }
        public SpriteTileMode tileMode { get => m_TileMode; set => m_TileMode = value; }
        public float adaptiveModeThreshold { get => m_AdaptiveModeThreshold; set => m_AdaptiveModeThreshold = value; }
        public SpriteSortPoint spriteSortPoint { get => m_SpriteSortPoint; set => m_SpriteSortPoint = value; }

        public event Action<SpriteRenderer> spriteChangeEvent;

        /// <summary>A sprite renderer starts with the built-in Sprites-Default material (a scene's or
        /// prefab's own materials replace it when read).</summary>
        internal override void OnAttached()
        {
            base.OnAttached();
            var material = BuiltinObjects.SpritesDefault;
            if (material != null) m_Materials = new[] { material };
        }

        public void RegisterSpriteChangeCallback(UnityEngine.Events.UnityAction<SpriteRenderer> callback) { }

        public void UnregisterSpriteChangeCallback(UnityEngine.Events.UnityAction<SpriteRenderer> callback) { }

        internal override Bounds GetLocalBounds()
        {
            if (m_Sprite == null) return default;
            if (m_DrawMode != SpriteDrawMode.Simple) return new Bounds(Vector3.zero, new Vector3(m_Size.x, m_Size.y, 0f));
            return m_Sprite.bounds;
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (m_Sprite == null || m_Sprite.texture == null) return;
            var mesh = m_DrawMode == SpriteDrawMode.Simple ? m_Sprite.RenderMesh() : SlicedMesh();
            var model = transform.localToWorldMatrix;
            if (m_FlipX || m_FlipY)
                model *= Matrix4x4.Scale(new Vector3(m_FlipX ? -1f : 1f, m_FlipY ? -1f : 1f, 1f));
            FrameBuilder.AddMeshDraws(this, mesh, model, m_Color, camera, into, m_Sprite.texture);
        }

        /// <summary>Nine-slice geometry for Sliced and Tiled draw modes (tiled is drawn sliced for now).</summary>
        Mesh SlicedMesh()
        {
            var key = (m_Sprite, m_Size, m_DrawMode);
            if (m_SlicedMesh != null && m_SlicedKey.Equals(key)) return m_SlicedMesh;
            m_SlicedKey = key;
            var s = m_Sprite;
            float ppu = s.pixelsPerUnit;
            var b = s.border / ppu;
            var rect = s.rect;
            var tex = s.texture;
            float w = m_Size.x, h = m_Size.y;
            float px = s.pivot.x / rect.width, py = s.pivot.y / rect.height;
            float x0 = -px * w, y0 = -py * h;
            float[] xs = { x0, x0 + b.x, x0 + w - b.z, x0 + w };
            float[] ys = { y0, y0 + b.y, y0 + h - b.w, y0 + h };
            float[] us = { rect.x / tex.width, (rect.x + s.border.x) / tex.width, (rect.xMax - s.border.z) / tex.width, rect.xMax / tex.width };
            float[] vs = { rect.y / tex.height, (rect.y + s.border.y) / tex.height, (rect.yMax - s.border.w) / tex.height, rect.yMax / tex.height };
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < 3; i++)
                {
                    int start = vertices.Count;
                    vertices.Add(new Vector3(xs[i], ys[j]));
                    vertices.Add(new Vector3(xs[i], ys[j + 1]));
                    vertices.Add(new Vector3(xs[i + 1], ys[j + 1]));
                    vertices.Add(new Vector3(xs[i + 1], ys[j]));
                    uvs.Add(new Vector2(us[i], vs[j]));
                    uvs.Add(new Vector2(us[i], vs[j + 1]));
                    uvs.Add(new Vector2(us[i + 1], vs[j + 1]));
                    uvs.Add(new Vector2(us[i + 1], vs[j]));
                    tris.AddRange(new[] { start, start + 1, start + 2, start + 2, start + 3, start });
                }
            }
            m_SlicedMesh ??= new Mesh();
            m_SlicedMesh.Clear();
            m_SlicedMesh.vertices = vertices.ToArray();
            m_SlicedMesh.uv = uvs.ToArray();
            m_SlicedMesh.triangles = tris.ToArray();
            return m_SlicedMesh;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            var r = body.Ref("m_Sprite");
            m_Sprite = r.IsNull ? null : resolver.Resolve(r, typeof(Sprite)) as Sprite;
            m_Color = body.Color("m_Color", Color.white);
            m_FlipX = body.Bool("m_FlipX");
            m_FlipY = body.Bool("m_FlipY");
            m_DrawMode = (SpriteDrawMode)body.Int("m_DrawMode");
            m_Size = body.Get("m_Size") != null ? body.Vector2("m_Size") : Vector2.one;
            m_AdaptiveModeThreshold = body.Float("m_AdaptiveModeThreshold", 0.5f);
            m_TileMode = (SpriteTileMode)body.Int("m_SpriteTileMode");
            m_MaskInteraction = (SpriteMaskInteraction)body.Int("m_MaskInteraction");
            m_SpriteSortPoint = (SpriteSortPoint)body.Int("m_SpriteSortPoint");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (SpriteRenderer)source;
            CopyRendererData(s);
            m_Sprite = s.m_Sprite;
            m_Color = s.m_Color;
            m_FlipX = s.m_FlipX;
            m_FlipY = s.m_FlipY;
            m_DrawMode = s.m_DrawMode;
            m_Size = s.m_Size;
            m_MaskInteraction = s.m_MaskInteraction;
            m_TileMode = s.m_TileMode;
            m_SpriteSortPoint = s.m_SpriteSortPoint;
        }
    }

    /// <summary>Per-renderer overrides of material properties.</summary>
    public sealed partial class MaterialPropertyBlock
    {
        internal readonly Dictionary<int, object> m_Values = new Dictionary<int, object>();

        public bool isEmpty => m_Values.Count == 0;

        public void Clear() => m_Values.Clear();

        internal MaterialPropertyBlock Copy()
        {
            var b = new MaterialPropertyBlock();
            b.CopyFrom(this);
            return b;
        }

        internal void CopyFrom(MaterialPropertyBlock other)
        {
            foreach (var p in other.m_Values) m_Values[p.Key] = p.Value;
        }

        public void SetFloat(string name, float value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetFloat(int nameID, float value) => m_Values[nameID] = value;
        public void SetInt(string name, int value) => m_Values[Shader.PropertyToID(name)] = (float)value;
        public void SetInt(int nameID, int value) => m_Values[nameID] = (float)value;
        public void SetInteger(string name, int value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetInteger(int nameID, int value) => m_Values[nameID] = value;
        public void SetVector(string name, Vector4 value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetVector(int nameID, Vector4 value) => m_Values[nameID] = value;
        public void SetColor(string name, Color value) => m_Values[Shader.PropertyToID(name)] = (Vector4)value;
        public void SetColor(int nameID, Color value) => m_Values[nameID] = (Vector4)value;
        public void SetMatrix(string name, Matrix4x4 value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetMatrix(int nameID, Matrix4x4 value) => m_Values[nameID] = value;
        public void SetTexture(string name, Texture value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetTexture(int nameID, Texture value) => m_Values[nameID] = value;
        public void SetFloatArray(string name, float[] values) => m_Values[Shader.PropertyToID(name)] = values;
        public void SetFloatArray(int nameID, float[] values) => m_Values[nameID] = values;
        public void SetVectorArray(string name, Vector4[] values) => m_Values[Shader.PropertyToID(name)] = values;
        public void SetVectorArray(int nameID, Vector4[] values) => m_Values[nameID] = values;

        public bool HasProperty(string name) => m_Values.ContainsKey(Shader.PropertyToID(name));
        public bool HasProperty(int nameID) => m_Values.ContainsKey(nameID);
        public bool HasFloat(string name) => HasProperty(name);
        public bool HasFloat(int nameID) => HasProperty(nameID);
        public bool HasColor(string name) => HasProperty(name);
        public bool HasColor(int nameID) => HasProperty(nameID);
        public bool HasVector(string name) => HasProperty(name);
        public bool HasVector(int nameID) => HasProperty(nameID);
        public bool HasTexture(string name) => HasProperty(name);
        public bool HasTexture(int nameID) => HasProperty(nameID);

        public float GetFloat(string name) => GetFloat(Shader.PropertyToID(name));
        public float GetFloat(int nameID) => m_Values.TryGetValue(nameID, out var v) && v is float f ? f : 0f;
        public int GetInt(string name) => (int)GetFloat(name);
        public int GetInt(int nameID) => (int)GetFloat(nameID);
        public Vector4 GetVector(string name) => GetVector(Shader.PropertyToID(name));
        public Vector4 GetVector(int nameID) => m_Values.TryGetValue(nameID, out var v) && v is Vector4 x ? x : Vector4.zero;
        public Color GetColor(string name) => GetVector(name);
        public Color GetColor(int nameID) => GetVector(nameID);
        public Matrix4x4 GetMatrix(string name) => GetMatrix(Shader.PropertyToID(name));
        public Matrix4x4 GetMatrix(int nameID) => m_Values.TryGetValue(nameID, out var v) && v is Matrix4x4 m ? m : Matrix4x4.identity;
        public Texture GetTexture(string name) => GetTexture(Shader.PropertyToID(name));
        public Texture GetTexture(int nameID) => m_Values.TryGetValue(nameID, out var v) ? v as Texture : null;
    }

    /// <summary>The project's sorting layers (ProjectSettings/TagManager.asset `m_SortingLayers`).</summary>
    public partial struct SortingLayer
    {
        int m_Id;

        internal static readonly List<(string Name, int Id)> s_Layers = new List<(string, int)> { ("Default", 0) };

        public int id => m_Id;
        public string name => IDToName(m_Id);
        public int value => GetLayerValueFromID(m_Id);

        public static SortingLayer[] layers
        {
            get
            {
                var result = new SortingLayer[s_Layers.Count];
                for (int i = 0; i < result.Length; i++) result[i].m_Id = s_Layers[i].Id;
                return result;
            }
        }

        public static int GetLayerValueFromID(int id)
        {
            int defaultIndex = s_Layers.FindIndex(l => l.Id == 0);
            int index = s_Layers.FindIndex(l => l.Id == id);
            return index < 0 ? 0 : index - defaultIndex;
        }

        public static int GetLayerValueFromName(string name) => GetLayerValueFromID(NameToID(name));

        public static int NameToID(string name)
        {
            foreach (var l in s_Layers) if (l.Name == name) return l.Id;
            return 0;
        }

        public static string IDToName(int id)
        {
            foreach (var l in s_Layers) if (l.Id == id) return l.Name;
            return "<unknown layer>";
        }

        public static bool IsValid(int id) => s_Layers.Exists(l => l.Id == id);
    }

    public enum ReceiveGI
    {
        Lightmaps = 1,
        LightProbes = 2,
    }
}

namespace Unbound.Runtime
{
    internal static class RenderRegistry
    {
        internal static readonly HashSet<UnityEngine.Renderer> Renderers = new HashSet<UnityEngine.Renderer>();
    }
}

namespace UnityEngine.Rendering
{
    public partial struct ReflectionProbeBlendInfo
    {
        public ReflectionProbe probe;
        public float weight;
    }
}
