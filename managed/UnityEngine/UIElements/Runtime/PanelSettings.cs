using System;
using System.Collections.Generic;

// Panel settings and UI documents: the asset that owns a runtime panel (its scale, target, theme
// and sort order) and the component that puts a UXML document on it.

namespace UnityEngine.UIElements
{
    public partial class PanelSettings : ScriptableObject
    {
        [SerializeField] ThemeStyleSheet themeUss;
        [SerializeField] bool m_DisableNoThemeWarning;
        [SerializeField] RenderTexture m_TargetTexture;
        [SerializeField] PanelRenderMode m_RenderMode;
        [SerializeField] int m_WorldSpaceLayer;
        [SerializeField] PanelScaleMode m_ScaleMode = PanelScaleMode.ConstantPhysicalSize;
        [SerializeField] float m_ReferenceSpritePixelsPerUnit = 100f;
        [SerializeField] float m_PixelsPerUnit = 100f;
        [SerializeField] float m_Scale = 1f;
        [SerializeField] float m_ReferenceDpi = 96f;
        [SerializeField] float m_FallbackDpi = 96f;
        [SerializeField] Vector2Int m_ReferenceResolution = new Vector2Int(1200, 800);
        [SerializeField] PanelScreenMatchMode m_ScreenMatchMode;
        [SerializeField] float m_Match;
        [SerializeField] float m_SortingOrder;
        [SerializeField] int m_TargetDisplay;
        [SerializeField] BindingLogLevel m_BindingLogLevel;
        [SerializeField] bool m_ClearDepthStencil = true;
        [SerializeField] bool m_ClearColor;
        [SerializeField] Color m_ColorClearValue = Color.clear;
        [SerializeField] uint m_VertexBudget;

        public bool forceGammaRendering;

        /// <summary>The panel's text settings (null: the default panel text settings).</summary>
        public PanelTextSettings textSettings;

        BaseRuntimePanel m_Panel;
        ThemeStyleSheet m_OldThemeUss;
        Rect m_TargetRect;
        float m_ResolvedScale;
        internal readonly List<UIDocument> m_AttachedUIDocuments = new List<UIDocument>();
        Func<Vector2, Vector2> m_AssignedScreenToPanel;

        internal float ScreenDPI { get; set; }

        public ThemeStyleSheet themeStyleSheet
        {
            get => themeUss;
            set
            {
                themeUss = value;
                ApplyThemeStyleSheet();
            }
        }

        public RenderTexture targetTexture
        {
            get => m_TargetTexture;
            set
            {
                m_TargetTexture = value;
                if (m_Panel != null) m_Panel.targetTexture = value;
            }
        }

        public PanelRenderMode renderMode { get => m_RenderMode; set => m_RenderMode = value; }

        public PanelScaleMode scaleMode { get => m_ScaleMode; set => m_ScaleMode = value; }

        public float referenceSpritePixelsPerUnit { get => m_ReferenceSpritePixelsPerUnit; set => m_ReferenceSpritePixelsPerUnit = value; }

        public float scale { get => m_Scale; set => m_Scale = value; }

        public float referenceDpi { get => m_ReferenceDpi; set => m_ReferenceDpi = value >= 1f ? value : 96f; }

        public float fallbackDpi { get => m_FallbackDpi; set => m_FallbackDpi = value >= 1f ? value : 96f; }

        public Vector2Int referenceResolution { get => m_ReferenceResolution; set => m_ReferenceResolution = value; }

        public PanelScreenMatchMode screenMatchMode { get => m_ScreenMatchMode; set => m_ScreenMatchMode = value; }

        public float match { get => m_Match; set => m_Match = value; }

        public float sortingOrder
        {
            get => m_SortingOrder;
            set
            {
                m_SortingOrder = value;
                if (m_Panel != null) m_Panel.sortingPriority = value;
            }
        }

        public int targetDisplay
        {
            get => m_TargetDisplay;
            set
            {
                m_TargetDisplay = value;
                if (m_Panel != null) m_Panel.targetDisplay = value;
            }
        }

        public BindingLogLevel bindingLogLevel { get => m_BindingLogLevel; set => m_BindingLogLevel = value; }

        public bool clearDepthStencil { get => m_ClearDepthStencil; set => m_ClearDepthStencil = value; }

        [Unbound.EditorFaithful("Unity's constant")]
        public float depthClearValue => 0.99f;

        public bool clearColor { get => m_ClearColor; set => m_ClearColor = value; }

        public Color colorClearValue { get => m_ColorClearValue; set => m_ColorClearValue = value; }

        public uint vertexBudget { get => m_VertexBudget; set => m_VertexBudget = value; }

        internal BaseRuntimePanel panel
        {
            get
            {
                if (m_Panel == null)
                {
                    m_Panel = (BaseRuntimePanel)UIElementsRuntimeUtility.FindOrCreateRuntimePanel(this, RuntimePanel.Create);
                    m_Panel.sortingPriority = m_SortingOrder;
                    m_Panel.targetDisplay = m_TargetDisplay;
                    var root = m_Panel.visualTree;
                    root.name = name;
                    ApplyPanelSettings();
                    ApplyThemeStyleSheet(root);
                    if (m_TargetTexture != null) m_Panel.targetTexture = m_TargetTexture;
                    if (m_AssignedScreenToPanel != null) m_Panel.screenToPanelSpace = m_AssignedScreenToPanel;
                }
                return m_Panel;
            }
        }

        internal VisualElement visualTree => panel.visualTree;

        internal bool isInitialized => m_Panel != null;

        void OnEnable()
        {
            ScreenDPI = Screen.dpi;
            if (m_Panel != null) m_Panel.targetTexture = m_TargetTexture;
        }

        void OnDisable() => DisposePanel();

        internal void DisposePanel()
        {
            if (m_Panel == null) return;
            UIElementsRuntimeUtility.DisposeRuntimePanel(this);
            m_Panel = null;
        }

        public void SetScreenToPanelSpaceFunction(Func<Vector2, Vector2> screentoPanelSpaceFunction)
        {
            m_AssignedScreenToPanel = screentoPanelSpaceFunction;
            panel.screenToPanelSpace = m_AssignedScreenToPanel;
        }

        void ApplyThemeStyleSheet(VisualElement root = null)
        {
            if (m_Panel == null) return;
            root ??= m_Panel.visualTree;
            if (m_OldThemeUss != themeUss && m_OldThemeUss != null) root.styleSheets.Remove(m_OldThemeUss);
            if (themeUss != null)
            {
                themeUss.isDefaultStyleSheet = true;
                root.styleSheets.Add(themeUss);
            }
            m_OldThemeUss = themeUss;
        }

        internal void ApplyPanelSettings()
        {
            var oldRect = m_TargetRect;
            float oldScale = m_ResolvedScale;
            m_TargetRect = GetDisplayRect();
            m_ResolvedScale = renderMode == PanelRenderMode.WorldSpace ? 1f : ResolveScale(m_TargetRect, ScreenDPI);
            var p = panel;
            if (renderMode != PanelRenderMode.WorldSpace)
            {
                var root = p.visualTree;
                if (root.style.width.value.value == 0f || m_ResolvedScale != oldScale || m_TargetRect.width != oldRect.width || m_TargetRect.height != oldRect.height)
                {
                    p.scale = m_ResolvedScale == 0f ? 0f : 1f / m_ResolvedScale;
                    root.style.left = 0f;
                    root.style.top = 0f;
                    root.style.width = m_TargetRect.width * m_ResolvedScale;
                    root.style.height = m_TargetRect.height * m_ResolvedScale;
                }
            }
            p.targetTexture = targetTexture;
            p.targetDisplay = targetDisplay;
            p.drawsInCameras = renderMode == PanelRenderMode.WorldSpace;
            p.pixelsPerUnit = m_PixelsPerUnit;
            p.worldSpaceLayer = m_WorldSpaceLayer;
            p.uaClear = new UaPanelClear { clearColor = m_ClearColor, clearDepthStencil = m_ClearDepthStencil, color = m_ColorClearValue };
            p.referenceSpritePixelsPerUnit = referenceSpritePixelsPerUnit;
            p.uaForceGamma = targetTexture != null && forceGammaRendering;
        }

        internal float ResolveScale(Rect targetRect, float screenDpi)
        {
            float s = 1f;
            switch (scaleMode)
            {
                case PanelScaleMode.ConstantPhysicalSize:
                {
                    float dpi = screenDpi == 0f ? fallbackDpi : screenDpi;
                    if (dpi != 0f) s = referenceDpi / dpi;
                    break;
                }
                case PanelScaleMode.ScaleWithScreenSize:
                    if (referenceResolution.x * referenceResolution.y != 0)
                    {
                        var ratio = new Vector2(targetRect.width / referenceResolution.x, targetRect.height / referenceResolution.y);
                        float m;
                        switch (screenMatchMode)
                        {
                            case PanelScreenMatchMode.Expand: m = Mathf.Min(ratio.x, ratio.y); break;
                            case PanelScreenMatchMode.Shrink: m = Mathf.Max(ratio.x, ratio.y); break;
                            default: m = Mathf.Lerp(ratio.x, ratio.y, Mathf.Clamp01(match)); break;
                        }
                        if (m != 0f) s = 1f / m;
                    }
                    break;
            }
            return scale > 0f ? s / scale : 0f;
        }

        internal Rect GetDisplayRect()
        {
            if (m_TargetTexture != null) return new Rect(0f, 0f, m_TargetTexture.width, m_TargetTexture.height);
            return new Rect(0f, 0f, Screen.width, Screen.height);
        }

        internal void AttachAndInsertUIDocumentToVisualTree(UIDocument document)
        {
            m_AttachedUIDocuments.Remove(document);
            document.rootVisualElement?.RemoveFromHierarchy();
            int index = 0;
            foreach (var attached in m_AttachedUIDocuments)
            {
                if (document.sortingOrder > attached.sortingOrder) { index++; continue; }
                if (!(document.sortingOrder < attached.sortingOrder) && document.m_UIDocumentCreationIndex > attached.m_UIDocumentCreationIndex) { index++; continue; }
                break;
            }
            m_AttachedUIDocuments.Insert(index, document);
            var root = visualTree;
            if (document.rootVisualElement == null) return;
            int insertAt = 0;
            for (int k = index - 1; k >= 0; k--)
            {
                var previous = m_AttachedUIDocuments[k].rootVisualElement;
                if (previous != null && previous.hierarchy.parent == root)
                {
                    insertAt = root.hierarchy.IndexOf(previous) + 1;
                    break;
                }
            }
            if (insertAt > root.hierarchy.childCount) insertAt = root.hierarchy.childCount;
            root.hierarchy.Insert(insertAt, document.rootVisualElement);
        }

        internal void DetachUIDocument(UIDocument document)
        {
            m_AttachedUIDocuments.Remove(document);
            document.rootVisualElement?.RemoveFromHierarchy();
            if (m_AttachedUIDocuments.Count == 0) UIElementsRuntimeUtility.MarkPotentiallyEmpty(this);
        }
    }

    public sealed partial class UIDocument : MonoBehaviour
    {
        static int s_CurrentUIDocumentCounter;
        internal readonly int m_UIDocumentCreationIndex;

        [SerializeField] PanelSettings m_PanelSettings;
        [SerializeField] UIDocument m_ParentUI;
        [SerializeField] VisualTreeAsset sourceAsset;
        [SerializeField] float m_SortingOrder;

        VisualElement m_RootVisualElement;

        UIDocument() { m_UIDocumentCreationIndex = s_CurrentUIDocumentCounter++; }

        public PanelSettings panelSettings
        {
            get => m_PanelSettings;
            set
            {
                if (m_PanelSettings == value) return;
                if (m_PanelSettings != null && m_RootVisualElement != null) m_PanelSettings.DetachUIDocument(this);
                m_PanelSettings = value;
                if (m_PanelSettings != null && m_RootVisualElement != null && isActiveAndEnabled) m_PanelSettings.AttachAndInsertUIDocumentToVisualTree(this);
            }
        }

        public UIDocument parentUI => m_ParentUI;

        public VisualTreeAsset visualTreeAsset
        {
            get => sourceAsset;
            set
            {
                sourceAsset = value;
                if (m_RootVisualElement != null || isActiveAndEnabled) RecreateUI();
            }
        }

        public VisualElement rootVisualElement => m_RootVisualElement;

        public float sortingOrder
        {
            get => m_SortingOrder;
            set
            {
                if (m_SortingOrder == value) return;
                m_SortingOrder = value;
                if (m_PanelSettings != null && m_RootVisualElement != null && isActiveAndEnabled) m_PanelSettings.AttachAndInsertUIDocumentToVisualTree(this);
            }
        }

        void Awake()
        {
            var parent = transform.parent;
            m_ParentUI = parent != null ? parent.GetComponentInParent<UIDocument>(true) : null;
        }

        void OnEnable()
        {
            if (m_ParentUI != null && m_PanelSettings == null) m_PanelSettings = m_ParentUI.m_PanelSettings;
            if (m_RootVisualElement == null) RecreateUI();
            else AddRootVisualElementToTree();
        }

        void OnDisable()
        {
            if (m_RootVisualElement == null) return;
            RemoveFromHierarchy();
            m_RootVisualElement = null;
        }

        void RecreateUI()
        {
            if (m_RootVisualElement != null)
            {
                RemoveFromHierarchy();
                m_RootVisualElement = null;
            }
            if (sourceAsset != null)
            {
                m_RootVisualElement = sourceAsset.Instantiate();
                if (m_RootVisualElement == null) Debug.LogError("The UXML file set for the UIDocument could not be cloned.");
            }
            if (m_RootVisualElement == null) m_RootVisualElement = new TemplateContainer { name = gameObject.name + "-container" };
            else m_RootVisualElement.name = gameObject.name + "-container";
            m_RootVisualElement.pickingMode = PickingMode.Ignore;
            if (isActiveAndEnabled) AddRootVisualElementToTree();
            m_RootVisualElement.EnableInClassList("unity-ui-document__root", m_ParentUI == null);
        }

        void AddRootVisualElementToTree()
        {
            if (!enabled) return;
            if (m_ParentUI != null)
            {
                if (m_ParentUI.m_RootVisualElement != null) m_ParentUI.m_RootVisualElement.Add(m_RootVisualElement);
            }
            else if (m_PanelSettings != null)
            {
                m_PanelSettings.AttachAndInsertUIDocumentToVisualTree(this);
            }
        }

        void RemoveFromHierarchy()
        {
            if (m_ParentUI != null) m_RootVisualElement?.RemoveFromHierarchy();
            else if (m_PanelSettings != null) m_PanelSettings.DetachUIDocument(this);
        }
    }
}
