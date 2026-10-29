using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// The root of a UI hierarchy. A root canvas drives its RectTransform from the screen (overlay and
    /// camera modes) and draws the CanvasRenderers below it in hierarchy order; a nested canvas with
    /// overrideSorting draws as a canvas of its own.
    /// </summary>
    [ClassId(223)]
    [RequireComponent(typeof(RectTransform))]
    public sealed partial class Canvas : Behaviour
    {
        public delegate void WillRenderCanvases();

        public static event WillRenderCanvases preWillRenderCanvases;
        public static event WillRenderCanvases willRenderCanvases;

        internal RenderMode m_RenderMode = RenderMode.ScreenSpaceOverlay;
        internal Camera m_Camera;
        internal float m_PlaneDistance = 100f;
        internal bool m_PixelPerfect;
        internal bool m_ReceivesEvents = true;
        internal bool m_OverrideSorting;
        internal bool m_OverridePixelPerfect;
        internal float m_SortingBucketNormalizedSize;
        internal bool m_VertexColorAlwaysGammaSpace;
        internal AdditionalCanvasShaderChannels m_AdditionalShaderChannels;
        internal int m_SortingLayerID;
        internal short m_SortingOrder;
        internal int m_TargetDisplay;
        internal float m_ScaleFactor = 1f;
        internal float m_ReferencePixelsPerUnit = 100f;
        internal bool m_UpdateRectTransformForStandalone = true;

        public Canvas() { }

        internal override void OnEngineEnable()
        {
            CanvasSystem.Register(this);
            Messaging.SendCanvasHierarchyChanged(transform);
        }

        internal override void OnEngineDisable()
        {
            CanvasSystem.Unregister(this);
            if (m_GameObject != null && !m_GameObject.m_Destroyed) Messaging.SendCanvasHierarchyChanged(m_GameObject.m_Transform);
        }

        internal override void OnEngineDestroy()
        {
            CanvasSystem.Unregister(this);
            // The canvas alone goes (its object stays): what was below it changed canvas.
            if (m_GameObject != null && !m_GameObject.m_Destroyed) Messaging.SendCanvasHierarchyChanged(m_GameObject.m_Transform);
        }

        // A nested canvas reports its root's render mode, camera and plane distance, whatever its
        // own (a prefab saved as World Space instantiated under a Screen Space - Camera canvas):
        // its GraphicRaycaster casts with the root's camera (measured, 6000.0).
        public RenderMode renderMode
        {
            get => ParentCanvas() is Canvas parent ? parent.renderMode : m_RenderMode;
            set { m_RenderMode = value; CanvasSystem.MarkLayoutDirty(this); }
        }

        public bool isRootCanvas => ParentCanvas() == null;

        /// <summary>The canvas this one belongs to (itself for a root canvas).</summary>
        public Canvas rootCanvas
        {
            get
            {
                var c = this;
                for (var p = ParentCanvas(); p != null; p = p.ParentCanvas()) c = p;
                return c;
            }
        }

        internal Canvas ParentCanvas()
        {
            for (var t = transform.m_Parent; t != null; t = t.m_Parent)
            {
                var go = t.m_GameObject;
                if (!go.activeInHierarchy) continue;
                foreach (var c in go.m_Components)
                    if (c is Canvas canvas && !canvas.m_Destroyed && canvas.m_Enabled) return canvas;
            }
            return null;
        }

        public Rect pixelRect
        {
            get
            {
                var root = rootCanvas;
                if (root.m_RenderMode == RenderMode.ScreenSpaceCamera && root.m_Camera != null) return root.m_Camera.pixelRect;
                return new Rect(0, 0, Screen.width, Screen.height);
            }
        }

        /// <summary>The size of the display the canvas is shown on: the game view.</summary>
        public Vector2 renderingDisplaySize => new Vector2(Screen.width, Screen.height);

        public float scaleFactor
        {
            get => isRootCanvas ? m_ScaleFactor : rootCanvas.scaleFactor;
            set
            {
                if (value <= 0f) return;
                if (Mathf.Approximately(m_ScaleFactor, value)) return;
                m_ScaleFactor = value;
                CanvasSystem.MarkLayoutDirty(this);
            }
        }

        public float referencePixelsPerUnit { get => m_ReferencePixelsPerUnit; set => m_ReferencePixelsPerUnit = value; }
        public bool overridePixelPerfect { get => m_OverridePixelPerfect; set => m_OverridePixelPerfect = value; }
        public bool vertexColorAlwaysGammaSpace { get => m_VertexColorAlwaysGammaSpace; set => m_VertexColorAlwaysGammaSpace = value; }

        public bool pixelPerfect
        {
            get => isRootCanvas || !m_OverridePixelPerfect ? (isRootCanvas ? m_PixelPerfect : rootCanvas.m_PixelPerfect) : m_PixelPerfect;
            set => m_PixelPerfect = value;
        }

        public float planeDistance
        {
            get => ParentCanvas() is Canvas parent ? parent.planeDistance : m_PlaneDistance;
            set { m_PlaneDistance = value; CanvasSystem.MarkLayoutDirty(this); }
        }
        public int renderOrder => CanvasSystem.RenderOrderOf(this);

        public bool overrideSorting
        {
            get => m_OverrideSorting;
            set
            {
                if (m_OverrideSorting == value) return;
                m_OverrideSorting = value;
                Messaging.SendCanvasHierarchyChanged(transform);
            }
        }

        // A nested canvas that does not override sorting answers the sorting of the canvas it
        // draws with (measured, 6000.0): its GraphicRaycaster's hits sort with that.
        Canvas SortingSource => !m_OverrideSorting && ParentCanvas() is Canvas parent ? parent.SortingSource : this;

        public int sortingOrder
        {
            get => SortingSource.m_SortingOrder;
            set => m_SortingOrder = (short)Mathf.Clamp(value, short.MinValue, short.MaxValue);
        }

        public int targetDisplay { get => m_TargetDisplay; set => m_TargetDisplay = value; }

        public int sortingLayerID
        {
            get => SortingSource.m_SortingLayerID;
            set => m_SortingLayerID = value;
        }

        public string sortingLayerName
        {
            get => SortingLayer.IDToName(sortingLayerID);
            set => m_SortingLayerID = SortingLayer.NameToID(value);
        }

        public int cachedSortingLayerValue => SortingLayer.GetLayerValueFromID(sortingLayerID);

        public AdditionalCanvasShaderChannels additionalShaderChannels
        {
            get => m_AdditionalShaderChannels;
            set => m_AdditionalShaderChannels = value;
        }

        public Camera worldCamera
        {
            get => ParentCanvas() is Canvas parent ? parent.worldCamera : m_Camera;
            set { m_Camera = value; CanvasSystem.MarkLayoutDirty(this); }
        }

        public float normalizedSortingGridSize
        {
            get => m_SortingBucketNormalizedSize;
            set => m_SortingBucketNormalizedSize = value;
        }

        public StandaloneRenderResize updateRectTransformForStandalone { get; set; } = StandaloneRenderResize.Enabled;

        [Obsolete("Setting normalizedSize via a int is not supported. Please use normalizedSortingGridSize", false)]
        public int sortingGridNormalizedSize { get; set; }

        public static void ForceUpdateCanvases() => CanvasSystem.SendWillRenderCanvases();

        public static Material GetDefaultCanvasMaterial() => CanvasSystem.DefaultMaterial;

        public static Material GetETC1SupportedCanvasMaterial() => CanvasSystem.DefaultMaterial;

        [Obsolete("Shared default material now used for text and general UI elements, call Canvas.GetDefaultCanvasMaterial()", false)]
        public static Material GetDefaultCanvasTextMaterial() => CanvasSystem.DefaultMaterial;

        internal static void InvokePreWillRender() => preWillRenderCanvases?.Invoke();

        internal static void InvokeWillRender() => willRenderCanvases?.Invoke();

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_RenderMode = (RenderMode)body.Int("m_RenderMode");
            var camera = body.Ref("m_Camera");
            m_Camera = camera.IsNull ? null : resolver.Resolve(camera, typeof(Camera)) as Camera;
            m_PlaneDistance = body.Float("m_PlaneDistance", 100f);
            m_PixelPerfect = body.Bool("m_PixelPerfect");
            m_ReceivesEvents = body.Bool("m_ReceivesEvents", true);
            m_OverrideSorting = body.Bool("m_OverrideSorting");
            m_OverridePixelPerfect = body.Bool("m_OverridePixelPerfect");
            m_SortingBucketNormalizedSize = body.Float("m_SortingBucketNormalizedSize");
            m_VertexColorAlwaysGammaSpace = body.Bool("m_VertexColorAlwaysGammaSpace");
            m_AdditionalShaderChannels = (AdditionalCanvasShaderChannels)body.Int("m_AdditionalShaderChannelsFlag");
            m_SortingLayerID = body.Int("m_SortingLayerID");
            m_SortingOrder = (short)body.Int("m_SortingOrder");
            m_TargetDisplay = body.Int("m_TargetDisplay");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Canvas)source;
            m_RenderMode = s.m_RenderMode;
            m_Camera = remap(s.m_Camera) as Camera;
            m_PlaneDistance = s.m_PlaneDistance;
            m_PixelPerfect = s.m_PixelPerfect;
            m_ReceivesEvents = s.m_ReceivesEvents;
            m_OverrideSorting = s.m_OverrideSorting;
            m_OverridePixelPerfect = s.m_OverridePixelPerfect;
            m_SortingBucketNormalizedSize = s.m_SortingBucketNormalizedSize;
            m_AdditionalShaderChannels = s.m_AdditionalShaderChannels;
            m_SortingLayerID = s.m_SortingLayerID;
            m_SortingOrder = s.m_SortingOrder;
            m_TargetDisplay = s.m_TargetDisplay;
            m_ScaleFactor = s.m_ScaleFactor;
            m_ReferencePixelsPerUnit = s.m_ReferencePixelsPerUnit;
        }
    }

    /// <summary>Alpha, interactability and raycast blocking for a UI subtree.</summary>
    [ClassId(225)]
    public sealed partial class CanvasGroup : Behaviour, ICanvasRaycastFilter
    {
        internal float m_Alpha = 1f;
        internal bool m_Interactable = true;
        internal bool m_BlocksRaycasts = true;
        internal bool m_IgnoreParentGroups;

        public CanvasGroup() { }

        public float alpha
        {
            get => m_Alpha;
            set
            {
                if (m_Alpha == value) return;
                m_Alpha = value;
                GroupChanged();
            }
        }

        public bool interactable
        {
            get => m_Interactable;
            set
            {
                if (m_Interactable == value) return;
                m_Interactable = value;
                GroupChanged();
            }
        }

        public bool blocksRaycasts
        {
            get => m_BlocksRaycasts;
            set
            {
                if (m_BlocksRaycasts == value) return;
                m_BlocksRaycasts = value;
                GroupChanged();
            }
        }

        public bool ignoreParentGroups
        {
            get => m_IgnoreParentGroups;
            set
            {
                if (m_IgnoreParentGroups == value) return;
                m_IgnoreParentGroups = value;
                GroupChanged();
            }
        }

        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera) => blocksRaycasts;

        /// <summary>Unity sends OnCanvasGroupChanged to the subtree.</summary>
        void GroupChanged()
        {
            if (m_GameObject == null || !m_GameObject.activeInHierarchy) return;
            Messaging.SendToHierarchy(transform, "OnCanvasGroupChanged");
        }

        internal override void OnEngineEnable() => GroupChanged();

        internal override void OnEngineDisable() => GroupChanged();

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Alpha = body.Float("m_Alpha", 1f);
            m_Interactable = body.Bool("m_Interactable", true);
            m_BlocksRaycasts = body.Bool("m_BlocksRaycasts", true);
            m_IgnoreParentGroups = body.Bool("m_IgnoreParentGroups");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (CanvasGroup)source;
            m_Alpha = s.m_Alpha;
            m_Interactable = s.m_Interactable;
            m_BlocksRaycasts = s.m_BlocksRaycasts;
            m_IgnoreParentGroups = s.m_IgnoreParentGroups;
        }
    }

    public partial interface ICanvasRaycastFilter
    {
        bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera);
    }

    /// <summary>The geometry of one UI element: what uGUI's Graphic fills and the canvas draws.</summary>
    [ClassId(222)]
    public sealed partial class CanvasRenderer : Component
    {
        public delegate void OnRequestRebuild();

        public static event OnRequestRebuild onRequestRebuild;

        /// <summary>The geometry drawn: a copy owned by this renderer.</summary>
        internal Mesh m_Mesh;
        Mesh m_Own;
        Mesh m_Assigned;
        internal Material[] m_Materials = new Material[1];
        internal Material[] m_PopMaterials = new Material[1];
        internal int m_MaterialCount;
        internal int m_PopMaterialCount;
        internal Texture m_Texture;
        internal Texture m_AlphaTexture;
        internal Color m_Color = Color.white;
        internal float m_Alpha = 1f;
        internal bool m_Cull;
        internal bool m_HasRectClipping;
        internal Rect m_ClipRect;
        internal Vector2 m_ClippingSoftness;
        internal bool m_CullTransparentMesh;
        internal int m_AbsoluteDepth = -1;
        internal int m_RelativeDepth = -1;

        public CanvasRenderer() { }

        public bool hasPopInstruction { get; set; }

        public int materialCount
        {
            get => m_MaterialCount;
            set
            {
                m_MaterialCount = Math.Max(0, value);
                if (m_Materials.Length < m_MaterialCount) Array.Resize(ref m_Materials, m_MaterialCount);
            }
        }

        public int popMaterialCount
        {
            get => m_PopMaterialCount;
            set
            {
                m_PopMaterialCount = Math.Max(0, value);
                if (m_PopMaterials.Length < m_PopMaterialCount) Array.Resize(ref m_PopMaterials, m_PopMaterialCount);
            }
        }

        public int absoluteDepth => m_AbsoluteDepth;
        [Unbound.EditorFaithful("no-op is equivalent: answering true only makes RectMask2D re-cull, which gives the same result")]
        public bool hasMoved => true;
        public bool cullTransparentMesh { get => m_CullTransparentMesh; set => m_CullTransparentMesh = value; }
        public bool hasRectClipping => m_HasRectClipping;
        public int relativeDepth => m_RelativeDepth;
        public bool cull { get => m_Cull; set => m_Cull = value; }
        public Vector2 clippingSoftness { get => m_ClippingSoftness; set => m_ClippingSoftness = value; }

        [Obsolete("isMask is no longer supported.See EnableClipping for vertex clipping configuration", false)]
        public bool isMask { get; set; }

        public void SetColor(Color color) => m_Color = color;
        public Color GetColor() => m_Color;
        public void EnableRectClipping(Rect rect)
        {
            m_HasRectClipping = true;
            m_ClipRect = rect;
        }

        public void DisableRectClipping() => m_HasRectClipping = false;

        public void SetMaterial(Material material, int index)
        {
            if (index < 0) return;
            if (index >= m_Materials.Length) Array.Resize(ref m_Materials, index + 1);
            m_Materials[index] = material;
            if (m_MaterialCount <= index) m_MaterialCount = index + 1;
        }

        public void SetMaterial(Material material, Texture texture)
        {
            materialCount = Math.Max(1, materialCount);
            SetMaterial(material, 0);
            SetTexture(texture);
        }

        public Material GetMaterial() => GetMaterial(0);

        public Material GetMaterial(int index) => index >= 0 && index < m_Materials.Length ? m_Materials[index] : null;

        public void SetPopMaterial(Material material, int index)
        {
            if (index < 0) return;
            if (index >= m_PopMaterials.Length) Array.Resize(ref m_PopMaterials, index + 1);
            m_PopMaterials[index] = material;
        }

        public Material GetPopMaterial(int index) => index >= 0 && index < m_PopMaterials.Length ? m_PopMaterials[index] : null;

        public void SetTexture(Texture texture) => m_Texture = texture;

        public void SetAlphaTexture(Texture texture) => m_AlphaTexture = texture;

        /// <summary>The geometry is copied, as in Unity: uGUI fills one shared worker mesh for
        /// every graphic.</summary>
        public void SetMesh(Mesh mesh)
        {
            m_Assigned = mesh;
            if (mesh == null)
            {
                m_Mesh = null;
                return;
            }
            if (m_Own == null)
            {
                using (ObjectRegistry.AsAssets())
                    m_Own = new Mesh { name = "CanvasRenderer", hideFlags = HideFlags.HideAndDontSave };
            }
            m_Own.CopyGeometryFrom(mesh);
            m_Mesh = m_Own;
        }

        public Mesh GetMesh() => m_Assigned;

        internal override void OnEngineDestroy()
        {
            if (m_Own != null) DestroyImmediate(m_Own, true);
            m_Own = null;
            m_Mesh = null;
        }

        public void Clear()
        {
            m_Mesh = null;
            m_Texture = null;
            m_MaterialCount = 0;
        }

        public float GetAlpha() => m_Alpha;

        public void SetAlpha(float alpha) => m_Alpha = alpha;

        /// <summary>The product of the alphas of the CanvasGroups above, as Unity applies it.</summary>
        public float GetInheritedAlpha()
        {
            float alpha = 1f;
            for (var t = transform; t != null; t = t.m_Parent)
            {
                foreach (var c in t.m_GameObject.m_Components)
                {
                    if (c is CanvasGroup g && g.m_Enabled && !g.m_Destroyed)
                    {
                        alpha *= g.m_Alpha;
                        if (g.m_IgnoreParentGroups) return alpha;
                    }
                }
            }
            return alpha;
        }

        public static void SplitUIVertexStreams(List<UIVertex> verts, List<Vector3> positions, List<Color32> colors, List<Vector4> uv0S, List<Vector4> uv1S, List<Vector4> uv2S, List<Vector4> uv3S, List<Vector3> normals, List<Vector4> tangents, List<int> indices)
        {
            positions.Clear(); colors.Clear(); uv0S.Clear(); uv1S.Clear(); uv2S.Clear(); uv3S.Clear(); normals.Clear(); tangents.Clear(); indices.Clear();
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                positions.Add(v.position); colors.Add(v.color); uv0S.Add(v.uv0); uv1S.Add(v.uv1); uv2S.Add(v.uv2); uv3S.Add(v.uv3); normals.Add(v.normal); tangents.Add(v.tangent);
            }
            // The stream is a triangle list: each vertex once, in order (measured, 6000.0). Text
            // with an Outline or Shadow comes back through here (VertexHelper.AddUIVertexTriangleStream).
            for (int i = 0; i < verts.Count; i++) indices.Add(i);
        }

        public static void SplitUIVertexStreams(List<UIVertex> verts, List<Vector3> positions, List<Color32> colors, List<Vector4> uv0S, List<Vector4> uv1S, List<Vector3> normals, List<Vector4> tangents, List<int> indices) =>
            SplitUIVertexStreams(verts, positions, colors, uv0S, uv1S, new List<Vector4>(), new List<Vector4>(), normals, tangents, indices);

        public static void CreateUIVertexStream(List<UIVertex> verts, List<Vector3> positions, List<Color32> colors, List<Vector4> uv0S, List<Vector4> uv1S, List<Vector4> uv2S, List<Vector4> uv3S, List<Vector3> normals, List<Vector4> tangents, List<int> indices)
        {
            verts.Clear();
            foreach (var i in indices)
            {
                verts.Add(new UIVertex
                {
                    position = positions[i], color = colors[i], uv0 = uv0S[i], uv1 = i < uv1S.Count ? uv1S[i] : default,
                    uv2 = i < uv2S.Count ? uv2S[i] : default, uv3 = i < uv3S.Count ? uv3S[i] : default,
                    normal = i < normals.Count ? normals[i] : Vector3.back, tangent = i < tangents.Count ? tangents[i] : new Vector4(1, 0, 0, -1),
                });
            }
        }

        public static void CreateUIVertexStream(List<UIVertex> verts, List<Vector3> positions, List<Color32> colors, List<Vector4> uv0S, List<Vector4> uv1S, List<Vector3> normals, List<Vector4> tangents, List<int> indices) =>
            CreateUIVertexStream(verts, positions, colors, uv0S, uv1S, new List<Vector4>(), new List<Vector4>(), normals, tangents, indices);

        public static void AddUIVertexStream(List<UIVertex> verts, List<Vector3> positions, List<Color32> colors, List<Vector4> uv0S, List<Vector4> uv1S, List<Vector3> normals, List<Vector4> tangents) =>
            SplitUIVertexStreams(verts, positions, colors, uv0S, uv1S, new List<Vector4>(), new List<Vector4>(), normals, tangents, new List<int>());

        public static void AddUIVertexStream(List<UIVertex> verts, List<Vector3> positions, List<Color32> colors, List<Vector4> uv0S, List<Vector4> uv1S, List<Vector4> uv2S, List<Vector4> uv3S, List<Vector3> normals, List<Vector4> tangents) =>
            SplitUIVertexStreams(verts, positions, colors, uv0S, uv1S, uv2S, uv3S, normals, tangents, new List<int>());

        [Obsolete("UI System now uses meshes.Generate a mesh and use 'SetMesh' instead", false)]
        public void SetVertices(List<UIVertex> vertices) => SetVertices(vertices.ToArray(), vertices.Count);

        /// <summary>As Unity does: every four vertices are a quad (two triangles), set as the mesh.</summary>
        [Obsolete("UI System now uses meshes.Generate a mesh and use 'SetMesh' instead", false)]
        public void SetVertices(UIVertex[] vertices, int size)
        {
            var positions = new List<Vector3>();
            var colors = new List<Color32>();
            var uv0 = new List<Vector4>();
            var uv1 = new List<Vector4>();
            var uv2 = new List<Vector4>();
            var uv3 = new List<Vector4>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var indices = new List<int>();
            for (int i = 0; i + 3 < size; i += 4)
            {
                for (int k = 0; k < 4; k++)
                {
                    var v = vertices[i + k];
                    positions.Add(v.position);
                    colors.Add(v.color);
                    uv0.Add(v.uv0);
                    uv1.Add(v.uv1);
                    uv2.Add(v.uv2);
                    uv3.Add(v.uv3);
                    normals.Add(v.normal);
                    tangents.Add(v.tangent);
                }
                indices.Add(i); indices.Add(i + 1); indices.Add(i + 2);
                indices.Add(i + 2); indices.Add(i + 3); indices.Add(i);
            }
            var mesh = new Mesh();
            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.SetIndices(indices.ToArray(), MeshTopology.Triangles, 0);
            SetMesh(mesh);
            Object.DestroyImmediate(mesh);
        }

        internal static void RequestRebuild() => onRequestRebuild?.Invoke();

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_CullTransparentMesh = body.Bool("m_CullTransparentMesh");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            m_CullTransparentMesh = ((CanvasRenderer)source).m_CullTransparentMesh;
        }
    }

    /// <summary>The vertex the UI builds meshes from.</summary>
    public partial struct UIVertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector4 tangent;
        public Color32 color;
        public Vector4 uv0;
        public Vector4 uv1;
        public Vector4 uv2;
        public Vector4 uv3;

        static readonly Color32 s_DefaultColor = new Color32(255, 255, 255, 255);
        static readonly Vector4 s_DefaultTangent = new Vector4(1f, 0f, 0f, -1f);

        public static UIVertex simpleVert = new UIVertex
        {
            position = Vector3.zero,
            normal = Vector3.back,
            tangent = s_DefaultTangent,
            color = s_DefaultColor,
            uv0 = Vector4.zero,
            uv1 = Vector4.zero,
            uv2 = Vector4.zero,
            uv3 = Vector4.zero,
        };
    }

    /// <summary>Screen points against RectTransforms, for the UI's raycasts and drags.</summary>
    public sealed partial class RectTransformUtility
    {
        static readonly Vector3[] s_Corners = new Vector3[4];

        public static Vector2 PixelAdjustPoint(Vector2 point, Transform elementTransform, Canvas canvas)
        {
            if (canvas == null || !canvas.pixelPerfect || canvas.renderMode == RenderMode.WorldSpace) return point;
            float scale = canvas.scaleFactor;
            var world = elementTransform.TransformPoint(point);
            var screen = canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.worldCamera == null
                ? (Vector2)world
                : (Vector2)canvas.worldCamera.WorldToScreenPoint(world);
            screen = new Vector2(Mathf.Round(screen.x), Mathf.Round(screen.y));
            Vector3 back;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.worldCamera == null) back = screen;
            else back = canvas.worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, canvas.worldCamera.WorldToScreenPoint(world).z));
            var local = elementTransform.InverseTransformPoint(back);
            return new Vector2(local.x, local.y);
        }

        public static Rect PixelAdjustRect(RectTransform rectTransform, Canvas canvas)
        {
            if (canvas == null || !canvas.pixelPerfect || canvas.renderMode == RenderMode.WorldSpace) return rectTransform.rect;
            var r = rectTransform.rect;
            var min = PixelAdjustPoint(r.min, rectTransform, canvas);
            var max = PixelAdjustPoint(r.max, rectTransform, canvas);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint) => RectangleContainsScreenPoint(rect, screenPoint, null);

        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam) => RectangleContainsScreenPoint(rect, screenPoint, cam, Vector4.zero);

        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam, Vector4 offset)
        {
            if (rect == null) return false;
            if (!ScreenPointToLocalPointInRectangle(rect, screenPoint, cam, out var local)) return false;
            var r = rect.rect;
            return local.x >= r.xMin - offset.x && local.x <= r.xMax + offset.z && local.y >= r.yMin - offset.y && local.y <= r.yMax + offset.w;
        }

        public static bool ScreenPointToWorldPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector3 worldPoint)
        {
            worldPoint = Vector2.zero;
            var ray = ScreenPointToRay(cam, screenPoint);
            var plane = new Plane(rect.rotation * Vector3.back, rect.position);
            if (!plane.Raycast(ray, out var distance))
            {
                // Overlay canvases face the screen: the point is on the plane at the ray's origin.
                if (cam == null)
                {
                    worldPoint = new Vector3(screenPoint.x, screenPoint.y, rect.position.z);
                    return true;
                }
                return false;
            }
            worldPoint = ray.GetPoint(distance);
            return true;
        }

        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (!ScreenPointToWorldPointInRectangle(rect, screenPoint, cam, out var world)) return false;
            localPoint = rect.InverseTransformPoint(world);
            return true;
        }

        public static Ray ScreenPointToRay(Camera cam, Vector2 screenPos)
        {
            if (cam != null) return cam.ScreenPointToRay(screenPos);
            var pos = (Vector3)screenPos;
            pos.z -= 100f;
            return new Ray(pos, Vector3.forward);
        }

        public static Vector2 WorldToScreenPoint(Camera cam, Vector3 worldPoint) => cam == null ? new Vector2(worldPoint.x, worldPoint.y) : (Vector2)cam.WorldToScreenPoint(worldPoint);

        public static Bounds CalculateRelativeRectTransformBounds(Transform root, Transform child)
        {
            var rects = child.GetComponentsInChildren<RectTransform>(false);
            if (rects.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var vMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var vMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var toLocal = root.worldToLocalMatrix;
            foreach (var r in rects)
            {
                r.GetWorldCorners(s_Corners);
                foreach (var c in s_Corners)
                {
                    var v = toLocal.MultiplyPoint3x4(c);
                    vMin = Vector3.Min(v, vMin);
                    vMax = Vector3.Max(v, vMax);
                }
            }
            var b = new Bounds(vMin, Vector3.zero);
            b.Encapsulate(vMax);
            return b;
        }

        public static Bounds CalculateRelativeRectTransformBounds(Transform trans) => CalculateRelativeRectTransformBounds(trans, trans);

        public static void FlipLayoutOnAxis(RectTransform rect, int axis, bool keepPositioning, bool recursive)
        {
            if (rect == null) return;
            if (recursive)
                for (int i = 0; i < rect.childCount; i++)
                    if (rect.GetChild(i) is RectTransform child) FlipLayoutOnAxis(child, axis, false, true);
            var pivot = rect.pivot;
            pivot[axis] = 1f - pivot[axis];
            rect.pivot = pivot;
            if (keepPositioning) return;
            var anchored = rect.anchoredPosition;
            anchored[axis] = -anchored[axis];
            rect.anchoredPosition = anchored;
            var min = rect.anchorMin;
            var max = rect.anchorMax;
            float temp = min[axis];
            min[axis] = 1f - max[axis];
            max[axis] = 1f - temp;
            rect.anchorMin = min;
            rect.anchorMax = max;
        }

        public static void FlipLayoutAxes(RectTransform rect, bool keepPositioning, bool recursive)
        {
            if (rect == null) return;
            if (recursive)
                for (int i = 0; i < rect.childCount; i++)
                    if (rect.GetChild(i) is RectTransform child) FlipLayoutAxes(child, false, true);
            rect.pivot = Swap(rect.pivot);
            rect.sizeDelta = Swap(rect.sizeDelta);
            if (keepPositioning) return;
            rect.anchoredPosition = Swap(rect.anchoredPosition);
            rect.anchorMin = Swap(rect.anchorMin);
            rect.anchorMax = Swap(rect.anchorMax);
        }

        static Vector2 Swap(Vector2 v) => new Vector2(v.y, v.x);
    }

    public static partial class UISystemProfilerApi
    {
        public enum SampleType
        {
            Layout = 0,
            Render = 1,
        }

        [Unbound.EditorFaithful("profiler markers only: nothing a game observes (no profiler is attached)")]
        public static void BeginSample(SampleType type) { }
        [Unbound.EditorFaithful("profiler markers only: nothing a game observes (no profiler is attached)")]
        public static void EndSample(SampleType type) { }
        [Unbound.EditorFaithful("profiler markers only: nothing a game observes (no profiler is attached)")]
        public static void AddMarker(string name, Object obj) { }
    }
}
