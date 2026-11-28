using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    public partial class EditorUtility
    {
        public static void OpenPropertyEditor(Object obj) => PropertyEditor.OpenPropertyEditor(obj);
    }

    internal partial class PropertyEditor
    {
        const float WindowStagger = 30f;
        static PropertyEditor s_LastFocusedInspector;

        Object m_InspectedObject;
        ActiveEditorTracker m_FocusedTracker;

        public ActiveEditorTracker tracker
        {
            get
            {
                if (m_FocusedTracker != null) return m_FocusedTracker;
                Stubs.NotImplemented("UnityEditor.PropertyEditor.tracker");
                return null;
            }
        }

        internal virtual Object GetInspectedObject()
        {
            if (m_FocusedTracker == null) Stubs.NotImplemented("UnityEditor.PropertyEditor.GetInspectedObject");
            return m_InspectedObject;
        }

        internal static PropertyEditor OpenPropertyEditor(Object obj, bool showWindow = true)
        {
            if (!Compat.FocusedPropertyEditorMeasured)
            {
                Stubs.Ignored("UnityEditor.EditorUtility.OpenPropertyEditor", "focused inspector opening is unmeasured for this release family");
                return null;
            }
            if (!EditorSession.IsPlaying)
            {
                Stubs.Ignored("UnityEditor.EditorUtility.OpenPropertyEditor in Edit mode", "is unmeasured");
                return null;
            }
            if (obj == null) return null;

            var inspector = CreateInstance<PropertyEditor>();
            inspector.m_InspectedObject = obj;
            inspector.m_FocusedTracker = new ActiveEditorTracker(obj);
            inspector.titleContent = new GUIContent(obj.name, AssetPreview.MiniThumbnail(obj), TargetTooltip(obj));
            if (showWindow)
            {
                inspector.ShowFocusedInspector();
                if (Compat.FocusedPropertyEditorStaggerMeasured && s_LastFocusedInspector != null)
                {
                    var previous = s_LastFocusedInspector.position;
                    var current = inspector.position;
                    current.position = previous.position + new Vector2(WindowStagger, WindowStagger);
                    inspector.position = current;
                }
                s_LastFocusedInspector = inspector;
            }
            Stubs.Ignored("UnityEditor.EditorUtility.OpenPropertyEditor", "cannot draw or edit properties in a focused inspector window");
            return inspector;
        }

        static string TargetTooltip(Object obj)
        {
            if (obj is GameObject go) return HierarchyTooltip(go);
            if (obj is Component component)
                return $"{HierarchyTooltip(component.gameObject)} ({ObjectNames.GetInspectorTitle(component)})";
            return AssetDatabase.GetAssetPath(obj);
        }

        static string HierarchyTooltip(GameObject go)
        {
            var scene = go.scene.name;
            if (string.IsNullOrEmpty(scene)) scene = "Unsaved Scene";
            return scene + "/" + AnimationUtility.CalculateTransformPath(go.transform, null);
        }
    }
}
