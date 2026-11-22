using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>What draws a preview of objects: an <see cref="ObjectPreview"/> or an Editor.</summary>
    internal partial interface IPreviewable
    {
        void Initialize(Object[] targets);
        void Cleanup();
        bool MoveNextTarget();
        void ResetTarget();
        bool HasPreviewGUI();
        GUIContent GetPreviewTitle();
        void DrawPreview(Rect previewArea);
        VisualElement CreatePreview(VisualElement container);
        void OnPreviewGUI(Rect r, GUIStyle background);
        void OnInteractivePreviewGUI(Rect r, GUIStyle background);
        void OnPreviewSettings();
        string GetInfoString();
        void ReloadPreviewInstances();
        Object target { get; }
    }

    /// <summary>Marks an <see cref="ObjectPreview"/> as the preview of objects of a type.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed partial class CustomPreviewAttribute : Attribute
    {
        internal Type m_Type;

        public CustomPreviewAttribute(Type type) => m_Type = type;
    }

    /// <summary>
    /// The preview of one or several objects, as in the Unity 6000.0.71f1 Editor
    /// (tests/projects/editorpreview): the base class has no preview GUI, titles itself after its
    /// target (or "N Types" for several), and <see cref="DrawPreview(Rect)"/> draws one target over
    /// the whole area or the first targets in a grid of squares, walking the targets with
    /// <see cref="MoveNextTarget"/>. Unbound's GUI passes have no Repaint, so nothing is painted:
    /// the calls and their rects are what a preview sees.
    /// </summary>
    public partial class ObjectPreview : IPreviewable
    {
        const int kPreviewLabelHeight = 12;
        const int kPreviewMinSize = 55;
        const int kGridTargetCount = 25;
        const int kGridSpacing = 10;

        protected Object[] m_Targets;
        protected int m_ReferenceTargetIndex;

        public ObjectPreview() { }

        public virtual void Cleanup() => GC.SuppressFinalize(this);

        public virtual void Initialize(Object[] targets)
        {
            m_ReferenceTargetIndex = 0;
            m_Targets = targets;
        }

        /// <summary>Moves to the next target; whether one more follows it.</summary>
        public virtual bool MoveNextTarget()
        {
            m_ReferenceTargetIndex++;
            return m_ReferenceTargetIndex < m_Targets.Length - 1;
        }

        public virtual void ResetTarget() => m_ReferenceTargetIndex = 0;

        public virtual Object target => m_Targets[m_ReferenceTargetIndex];

        public virtual bool HasPreviewGUI() => false;

        public virtual VisualElement CreatePreview(VisualElement inspectorPreviewWindow) => null;

        public virtual GUIContent GetPreviewTitle() => new GUIContent { text = PreviewTitle(m_Targets, target) };

        // One target's name; for several their count and type: a script's class, else the object's
        // class name in words.
        static string PreviewTitle(Object[] targets, Object target)
        {
            if (targets.Length == 1) return target.name;
            string type = ObjectNames.IsScriptObject(target) ? target.GetType().Name : ObjectNames.NicifyVariableName(ObjectNames.GetClassName(target));
            return targets.Length + " " + type + "s";
        }

        public virtual void OnPreviewGUI(Rect r, GUIStyle background)
        {
            if (Event.current.type == EventType.Repaint) background.Draw(r, false, false, false, false);
        }

        public virtual void OnInteractivePreviewGUI(Rect r, GUIStyle background) => OnPreviewGUI(r, background);

        public virtual void OnPreviewSettings() { }

        public virtual string GetInfoString() => "";

        public void DrawPreview(Rect previewArea) => DrawPreview(this, previewArea, m_Targets);

        public virtual void ReloadPreviewInstances() { }

        // The Editor skin's preview styles; without that skin, empty styles of their names.
        static readonly Dictionary<string, GUIStyle> s_Styles = new Dictionary<string, GUIStyle>();

        static GUIStyle Style(string name)
        {
            var style = GUI.skin.FindStyle(name);
            if (style != null) return style;
            if (!s_Styles.TryGetValue(name, out style)) s_Styles[name] = style = new GUIStyle { name = name };
            return style;
        }

        internal static void DrawPreview(IPreviewable defaultPreview, Rect previewArea, Object[] targets)
        {
            string text = string.Empty;
            var evt = Event.current;
            if (targets.Length > 1)
            {
                // The first targets (25 at most) in a grid of squares, each over its name.
                var inner = new RectOffset(16, 16, 20, 25).Remove(previewArea);
                int maxRows = Mathf.Max(1, Mathf.FloorToInt((inner.height + kGridSpacing) / (kPreviewMinSize + kGridSpacing + kPreviewLabelHeight)));
                int maxCols = Mathf.Max(1, Mathf.FloorToInt((inner.width + kGridSpacing) / (kPreviewMinSize + kGridSpacing)));
                int needed = Mathf.Min(targets.Length, kGridTargetCount);
                bool fixedSize = needed >= maxRows * maxCols;
                int columns = maxCols, rows = maxRows;
                if (!fixedSize) GridDivision(inner, needed, kPreviewLabelHeight, out columns, out rows);
                int count = Mathf.Min(columns * rows, targets.Length);
                inner.width += kGridSpacing;
                inner.height += kGridSpacing;
                var cell = new Vector2(Mathf.FloorToInt(inner.width / columns - kGridSpacing), Mathf.FloorToInt(inner.height / rows - kGridSpacing));
                float size = Mathf.Min(cell.x, cell.y - kPreviewLabelHeight);
                if (fixedSize) size = Mathf.Min(size, kPreviewMinSize);
                // A double click on a target selects it.
                bool selectingOne = evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount == 2 && previewArea.Contains(evt.mousePosition);

                defaultPreview.ResetTarget();
                for (int i = 0; i < count; i++)
                {
                    var r = new Rect(inner.x + (i % columns) * inner.width / columns, inner.y + (i / columns) * inner.height / rows, cell.x, cell.y);
                    if (selectingOne && r.Contains(Event.current.mousePosition))
                    {
                        Selection.objects = defaultPreview.target is AssetImporter importer
                            ? new[] { AssetDatabase.LoadAssetAtPath<Object>(importer.assetPath) }
                            : new[] { defaultPreview.target };
                    }
                    r.height -= kPreviewLabelHeight;
                    var square = new Rect(r.x + (r.width - size) * 0.5f, r.y + (r.height - size) * 0.5f, size, size);
                    GUI.BeginGroup(square);
                    Editor.m_AllowMultiObjectAccess = false;
                    try
                    {
                        defaultPreview.OnInteractivePreviewGUI(new Rect(0, 0, size, size), Style("PreBackgroundSolid"));
                    }
                    finally
                    {
                        Editor.m_AllowMultiObjectAccess = true;
                    }
                    GUI.EndGroup();
                    r.y = square.yMax;
                    r.height = 16;
                    GUI.Label(r, targets[i].name, Style("PreMiniLabel"));
                    defaultPreview.MoveNextTarget();
                }
                // Back at the first target: the code around reads `target` again.
                defaultPreview.ResetTarget();
                if (Event.current.type == EventType.Repaint) text = string.Format("Previewing {0} of {1} Objects", count, targets.Length);
            }
            else
            {
                defaultPreview.OnInteractivePreviewGUI(previewArea, Style("PreBackground"));
                if (Event.current.type == EventType.Repaint)
                {
                    text = defaultPreview.GetInfoString();
                    if (text != string.Empty) text = string.Format("{0}\n{1}", defaultPreview.target.name, text.Replace("\n", "   "));
                }
            }
            // The info line over the preview's bottom is painted only (a drop-shadow label in the
            // Editor skin's overlay style): Unbound's GUI passes have no Repaint.
            if (Event.current.type == EventType.Repaint && text != string.Empty)
                Unbound.Runtime.Stubs.NotImplemented("UnityEditor.ObjectPreview.DrawPreview (info label)");
        }

        // Columns and rows for at least `minimum` cells, as near to squares as the rect allows.
        static void GridDivision(Rect rect, int minimum, int labelHeight, out int xCount, out int yCount)
        {
            float approxSize = Mathf.Sqrt(rect.width * rect.height / minimum);
            xCount = Mathf.FloorToInt(rect.width / approxSize);
            yCount = Mathf.FloorToInt(rect.height / (approxSize + labelHeight));
            while (xCount * yCount < minimum)
            {
                float ifWider = RatioDistance((xCount + 1) / rect.width, yCount / (rect.height - yCount * labelHeight));
                float ifTaller = RatioDistance(xCount / rect.width, (yCount + 1) / (rect.height - (yCount + 1) * labelHeight));
                if (ifWider < ifTaller)
                {
                    xCount++;
                    if (xCount * yCount > minimum) yCount = Mathf.CeilToInt((float)minimum / xCount);
                }
                else
                {
                    yCount++;
                    if (xCount * yCount > minimum) xCount = Mathf.CeilToInt((float)minimum / yCount);
                }
            }
        }

        static float RatioDistance(float x, float y) => Mathf.Max(x / y, y / x);
    }

    public partial class Editor : IPreviewable
    {
        // The preview an editor without one of its own answers with.
        internal IPreviewable m_DummyPreview;
        int m_ReferenceTargetIndex;

        /// <summary>False while a preview of several targets draws one of them: the editor is asked
        /// about that one target, and reading <see cref="targets"/> then is an error.</summary>
        internal static bool m_AllowMultiObjectAccess = true;

        internal virtual IPreviewable preview
        {
            get
            {
                if (m_DummyPreview == null)
                {
                    m_DummyPreview = new ObjectPreview();
                    m_DummyPreview.Initialize(targets);
                }
                return m_DummyPreview;
            }
        }

        /// <summary>The target a preview of several is at; setting it wraps around the targets.</summary>
        internal virtual int referenceTargetIndex
        {
            get => Mathf.Clamp(m_ReferenceTargetIndex, 0, m_Targets.Length - 1);
            set => m_ReferenceTargetIndex = (Math.Abs(value * m_Targets.Length) + value) % m_Targets.Length;
        }

        internal virtual string targetTitle =>
            m_Targets.Length == 1 || !m_AllowMultiObjectAccess
                ? ObjectNames.GetInspectorTitle(target)
                : m_Targets.Length + " " + ObjectNames.NicifyVariableName(ObjectNames.GetTypeName(target)) + "s";

        public virtual bool HasPreviewGUI() => preview.HasPreviewGUI();

        public virtual VisualElement CreatePreview(VisualElement inspectorPreviewWindow) => preview.CreatePreview(inspectorPreviewWindow);

        public virtual GUIContent GetPreviewTitle() => preview.GetPreviewTitle();

        public virtual Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height) => null;

        public virtual void OnPreviewGUI(Rect r, GUIStyle background) => preview.OnPreviewGUI(r, background);

        public virtual void OnInteractivePreviewGUI(Rect r, GUIStyle background) => OnPreviewGUI(r, background);

        public virtual void OnPreviewSettings() => preview.OnPreviewSettings();

        public virtual string GetInfoString() => preview.GetInfoString();

        public virtual void DrawPreview(Rect previewArea) => ObjectPreview.DrawPreview(this, previewArea, targets);

        public virtual void ReloadPreviewInstances() => preview.ReloadPreviewInstances();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public void Cleanup() =>
            throw new InvalidOperationException("You shouldn't call Cleanup for Editors. Dispose resources in Editor.OnDisable");

        public bool MoveNextTarget()
        {
            referenceTargetIndex++;
            return referenceTargetIndex < targets.Length;
        }

        public void ResetTarget() => referenceTargetIndex = 0;

        /// <summary>A field for each visible top-level property but the named ones.</summary>
        protected internal static void DrawPropertiesExcluding(SerializedObject obj, params string[] propertyToExclude)
        {
            var property = obj.GetIterator();
            for (bool enter = true; property.NextVisible(enter); enter = false)
            {
                // No names at all fail as the Editor's search of them does.
                if (System.Linq.Enumerable.Contains(propertyToExclude, property.name)) continue;
                EditorGUILayout.PropertyField(property, true);
            }
        }
    }
}
