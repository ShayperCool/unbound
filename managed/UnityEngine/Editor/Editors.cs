using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// An inspector of objects: the [CustomEditor] class for their type (else the Editor's own for
    /// built-in types, a generic one for others), its targets and its SerializedObject, OnEnable when
    /// made and OnDisable when destroyed. Unbound shows no inspectors: OnInspectorGUI draws only in an
    /// OnGUI (outside one it throws, as the Editor's does).
    /// </summary>
    public partial class Editor : ScriptableObject
    {
        Object[] m_Targets = Array.Empty<Object>();
        internal Object m_Context;
        internal SerializedObject m_SerializedObject;

        public Editor() { }

        public Object target
        {
            get => m_Targets.Length > 0 ? m_Targets[0] : null;
            set
            {
                m_Targets = new[] { value };
                m_SerializedObject = null;
            }
        }

        public Object[] targets => m_Targets;

        public SerializedObject serializedObject => m_SerializedObject ??= m_Targets.Length > 0 && m_Targets.All(t => t != null) ? new SerializedObject(m_Targets, m_Context) : null;

        public bool hasUnsavedChanges { get; protected set; }

        public void Initialize(Object[] targets)
        {
            m_Targets = targets ?? Array.Empty<Object>();
            m_SerializedObject = null;
        }

        public static Editor CreateEditor(Object targetObject) => CreateEditor(targetObject, null);

        public static Editor CreateEditor(Object targetObject, Type editorType) => targetObject == null ? null : Make(new[] { targetObject }, null, editorType);

        public static Editor CreateEditor(Object[] targetObjects) => CreateEditor(targetObjects, null);

        public static Editor CreateEditor(Object[] targetObjects, Type editorType) =>
            targetObjects == null || targetObjects.Length == 0 || targetObjects.Any(o => o == null) ? null : Make(targetObjects, null, editorType);

        public static Editor CreateEditorWithContext(Object[] targetObjects, Object context) => CreateEditorWithContext(targetObjects, context, null);

        public static Editor CreateEditorWithContext(Object[] targetObjects, Object context, Type editorType) =>
            targetObjects == null || targetObjects.Length == 0 || targetObjects.Any(o => o == null) ? null : Make(targetObjects, context, editorType);

        public static void CreateCachedEditor(Object targetObject, Type editorType, ref Editor previousEditor) =>
            CreateCachedEditorWithContext(new[] { targetObject }, null, editorType, ref previousEditor);

        public static void CreateCachedEditor(Object[] targetObjects, Type editorType, ref Editor previousEditor) =>
            CreateCachedEditorWithContext(targetObjects, null, editorType, ref previousEditor);

        public static void CreateCachedEditorWithContext(Object targetObject, Object context, Type editorType, ref Editor previousEditor) =>
            CreateCachedEditorWithContext(new[] { targetObject }, context, editorType, ref previousEditor);

        /// <summary>Keeps the previous editor when it inspects the same objects with the same class.</summary>
        public static void CreateCachedEditorWithContext(Object[] targetObjects, Object context, Type editorType, ref Editor previousEditor)
        {
            if (previousEditor != null && previousEditor.m_Targets.SequenceEqual(targetObjects) && previousEditor.m_Context == context
                && (editorType == null || previousEditor.GetType() == editorType))
                return;
            if (previousEditor != null) DestroyImmediate(previousEditor);
            previousEditor = CreateEditorWithContext(targetObjects, context, editorType);
        }

        static Editor Make(Object[] targets, Object context, Type editorType)
        {
            var type = editorType ?? CustomEditors.Find(targets[0].GetType(), targets.Length > 1);
            if (type == null || type.IsAbstract || !typeof(Editor).IsAssignableFrom(type))
            {
                if (editorType != null) Debug.LogError($"'{editorType.Name}' is not an Editor type.");
                return null;
            }
            var editor = (Editor)CreateInstanceWithoutCallbacks(type);
            editor.hideFlags = HideFlags.HideAndDontSave;
            editor.m_Targets = (Object[])targets.Clone();
            editor.m_Context = context;
            editor.RunCreationCallbacks();
            return editor;
        }

        /// <summary>The fields of the targets, each with its own control: only inside an OnGUI.</summary>
        public bool DrawDefaultInspector()
        {
            EditorGUI.RequireGUI();
            var so = serializedObject;
            if (so == null) return false;
            EditorGUI.BeginChangeCheck();
            so.UpdateIfRequiredOrScript();
            var it = so.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                if (it.propertyPath == "m_Script") continue;
                EditorGUILayout.PropertyField(it, true);
            }
            so.ApplyModifiedProperties();
            return EditorGUI.EndChangeCheck();
        }

        [Unbound.EditorFaithful("no inspector window draws editors (as the batch-mode Editor)")]
        public void Repaint() { }

        public virtual void OnInspectorGUI() => DrawDefaultInspector();

        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual bool RequiresConstantRepaint() => false;
    }

    /// <summary>Which editor inspects a type: a [CustomEditor] class for it (or for a base class with
    /// editorForChildClasses), fallbacks last; else the Editor's built-in inspector of the type.</summary>
    internal static class CustomEditors
    {
        static List<(Type Inspected, Type Editor, bool Children, bool Fallback, bool Multi)> s_Editors;

        static readonly Dictionary<Type, string> s_BuiltIn = new Dictionary<Type, string>
        {
            [typeof(Transform)] = "UnityEditor.TransformInspector",
            [typeof(GameObject)] = "UnityEditor.GameObjectInspector",
            [typeof(Material)] = "UnityEditor.MaterialEditor",
        };

        static List<(Type, Type, bool, bool, bool)> Editors
        {
            get
            {
                if (s_Editors != null) return s_Editors;
                var list = new List<(Type, Type, bool, bool, bool)>();
                foreach (var type in TypeCache.GetTypesWithAttribute<CustomEditor>())
                {
                    if (type.IsAbstract || !typeof(Editor).IsAssignableFrom(type)) continue;
                    bool multi = type.IsDefined(typeof(CanEditMultipleObjects), true);
                    foreach (var attribute in type.GetCustomAttributes<CustomEditor>(false))
                        if (attribute.m_InspectedType != null)
                            list.Add((attribute.m_InspectedType, type, attribute.m_EditorForChildClasses, attribute.isFallback, multi));
                }
                return s_Editors = list;
            }
        }

        internal static Type Find(Type target, bool multiEdit)
        {
            foreach (bool fallback in new[] { false, true })
            {
                for (var t = target; t != null; t = t.BaseType)
                {
                    foreach (var (inspected, editor, children, isFallback, multi) in Editors)
                        if (inspected == t && isFallback == fallback && (t == target || children) && (!multiEdit || multi)) return editor;
                }
            }
            for (var t = target; t != null; t = t.BaseType)
                if (s_BuiltIn.TryGetValue(t, out var name)) return typeof(Editor).Assembly.GetType(name);
            return typeof(Editor).Assembly.GetType("UnityEditor.GenericInspector");
        }
    }

    public partial class CustomEditor : Attribute
    {
        internal Type m_InspectedType;
        internal bool m_EditorForChildClasses;

        public CustomEditor(Type inspectedType) => m_InspectedType = inspectedType;

        public CustomEditor(Type inspectedType, bool editorForChildClasses)
        {
            m_InspectedType = inspectedType;
            m_EditorForChildClasses = editorForChildClasses;
        }

        public bool isFallback { get; set; }
    }

    /// <summary>The inspector window's editors: Unbound has no inspector window, so none.</summary>
    public sealed partial class ActiveEditorTracker
    {
        static ActiveEditorTracker s_Shared;

        public ActiveEditorTracker() { }

        [Unbound.EditorFaithful("no inspector window is open (measured: tests/projects/editorui ActiveEditorTracker.activeEditors)")]
        public Editor[] activeEditors => Array.Empty<Editor>();

        public static ActiveEditorTracker sharedTracker => s_Shared ??= new ActiveEditorTracker();
    }

    internal partial class TransformInspector : Editor
    {
        public TransformInspector() { }

        [Unbound.EditorFaithful("the built-in inspector only prepares its drawing, which no window does")]
        public void OnEnable() { }
    }

    internal partial class GameObjectInspector : Editor
    {
        public GameObjectInspector() { }

        [Unbound.EditorFaithful("the built-in inspector only prepares its drawing (previews, header), which no window does")]
        public void OnEnable() { }

        internal void OnDisable() { }
    }

    internal partial class GenericInspector : Editor
    {
        public GenericInspector() { }
    }

    public partial class MaterialEditor : Editor
    {
        public MaterialEditor() { }

        /// <summary>Sets <paramref name="mat"/>'s GI flags from its _EmissionColor.</summary>
        public static void FixupEmissiveFlag(Material mat)
        {
            if (mat == null) throw new ArgumentNullException(nameof(mat));
            mat.globalIlluminationFlags = FixupEmissiveFlag(mat.GetColor("_EmissionColor"), mat.globalIlluminationFlags);
        }

        /// <summary>Baked emission of a black colour is marked black; other flags keep only their
        /// emissive bits (Unity's rule).</summary>
        public static MaterialGlobalIlluminationFlags FixupEmissiveFlag(Color col, MaterialGlobalIlluminationFlags flags)
        {
            if ((flags & MaterialGlobalIlluminationFlags.BakedEmissive) != 0 && col.maxColorComponent == 0f)
                flags |= MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            else if (flags != MaterialGlobalIlluminationFlags.EmissiveIsBlack)
                flags &= MaterialGlobalIlluminationFlags.AnyEmissive;
            return flags;
        }

        [Unbound.EditorFaithful("the material inspector only prepares its drawing (previews, property lists), which no window does")]
        public virtual void Awake() { }

        [Unbound.EditorFaithful("the material inspector only prepares its drawing (previews, property lists), which no window does")]
        public virtual void OnEnable() { }

        [Unbound.EditorFaithful("the material inspector only releases its drawing state, which no window made")]
        public virtual void OnDisable() { }
    }

    /// <summary>A context or drop-down menu: its items, shown by nothing (Unbound has no editor windows).</summary>
    public sealed partial class GenericMenu
    {
        readonly List<(GUIContent Content, bool Separator, bool On, bool Enabled, MenuFunction Func, MenuFunction2 Func2, object Data)> m_Items =
            new List<(GUIContent, bool, bool, bool, MenuFunction, MenuFunction2, object)>();

        public GenericMenu() { }

        public bool allowDuplicateNames { get; set; }

        public delegate void MenuFunction();

        public delegate void MenuFunction2(object userData);

        public void AddItem(GUIContent content, bool on, MenuFunction func) => m_Items.Add((content, false, on, true, func, null, null));

        public void AddItem(GUIContent content, bool on, MenuFunction2 func, object userData) => m_Items.Add((content, false, on, true, null, func, userData));

        public void AddDisabledItem(GUIContent content) => m_Items.Add((content, false, false, false, null, null, null));

        public void AddDisabledItem(GUIContent content, bool on) => m_Items.Add((content, false, on, false, null, null, null));

        public void AddSeparator(string path) => m_Items.Add((new GUIContent(path), true, false, false, null, null, null));

        public int GetItemCount() => m_Items.Count;

        [Unbound.EditorFaithful("no window shows the menu (measured: tests/projects/editorui GenericMenu.ShowAsContext)")]
        public void ShowAsContext() { }

        [Unbound.EditorFaithful("no window shows the menu (measured: tests/projects/editorui GenericMenu.DropDown)")]
        public void DropDown(Rect position) { }
    }

    /// <summary>Marks an editor that can inspect several objects at once.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed partial class CanEditMultipleObjects : Attribute
    {
        public CanEditMultipleObjects() { }
    }

    public enum MessageType
    {
        None = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    public enum DragAndDropVisualMode
    {
        None = 0,
        Copy = 1,
        Link = 2,
        Move = 16,
        Generic = 4,
        Rejected = 32,
    }

    /// <summary>Drag and drop between editor views: its data (no view takes a drop in Unbound).</summary>
    public partial class DragAndDrop
    {
        static Object[] s_Objects = Array.Empty<Object>();
        static string[] s_Paths = Array.Empty<string>();
        static readonly Dictionary<string, object> s_Data = new Dictionary<string, object>();

        public static Object[] objectReferences { get => s_Objects; set => s_Objects = value ?? Array.Empty<Object>(); }

        public static string[] paths { get => s_Paths; set => s_Paths = value ?? Array.Empty<string>(); }

        public static DragAndDropVisualMode visualMode { get; set; }

        public static int activeControlID { get; set; }

        public static void PrepareStartDrag()
        {
            s_Objects = Array.Empty<Object>();
            s_Paths = Array.Empty<string>();
            s_Data.Clear();
        }

        /// <summary>A drag starts from an editor view's event: there is none outside one.</summary>
        public static void StartDrag(string title) => EditorGUI.RequireGUI();

        [Unbound.EditorFaithful("no view takes a drop (measured: tests/projects/editorui DragAndDrop.AcceptDrag)")]
        public static void AcceptDrag() { }

        public static object GetGenericData(string type) => type != null && s_Data.TryGetValue(type, out var v) ? v : null;

        public static void SetGenericData(string type, object data) => s_Data[type] = data;
    }

    public sealed partial class Handles
    {
        static Color s_Color = Color.white;
        static Matrix4x4 s_Matrix = Matrix4x4.identity;

        public static Color color { get => s_Color; set => s_Color = value; }

        public delegate void CapFunction(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType);

        public static Matrix4x4 matrix { get => s_Matrix; set => s_Matrix = value; }

        /// <summary>Handles draw into a scene view's GUI: outside one the Editor's throw.</summary>
        static void RequireSceneGUI() => EditorGUI.RequireGUI();

        public static void DrawLine(Vector3 p1, Vector3 p2) => RequireSceneGUI();

        public static void DrawLine(Vector3 p1, Vector3 p2, float thickness) => RequireSceneGUI();

        public static void Label(Vector3 position, string text)
        {
            if (Event.current == null) throw new ArgumentException("You can only call GUI functions from inside OnGUI.");
        }

        public static Vector3 PositionHandle(Vector3 position, Quaternion rotation)
        {
            RequireSceneGUI();
            return position;
        }

        public static bool Button(Vector3 position, Quaternion direction, float size, float pickSize, CapFunction capFunction)
        {
            RequireSceneGUI();
            return false;
        }

        public static void CubeHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType) =>
            Unbound.Runtime.Stubs.Ignored("Handles.CubeHandleCap", "is not drawn (no scene view)");

        public partial struct DrawingScope : IDisposable
        {
            bool m_Disposed;
            readonly Color m_OriginalColor;
            readonly Matrix4x4 m_OriginalMatrix;

            public DrawingScope(Color color) : this(color, s_Matrix) { }

            public DrawingScope(Matrix4x4 matrix) : this(s_Color, matrix) { }

            public DrawingScope(Color color, Matrix4x4 matrix)
            {
                m_Disposed = false;
                m_OriginalColor = s_Color;
                m_OriginalMatrix = s_Matrix;
                s_Matrix = matrix;
                s_Color = color;
            }

            public Color originalColor => m_OriginalColor;

            public Matrix4x4 originalMatrix => m_OriginalMatrix;

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                s_Color = m_OriginalColor;
                s_Matrix = m_OriginalMatrix;
            }
        }
    }

    public sealed partial class HandleUtility
    {
        /// <summary>A handle's size at a point: without a scene camera, the Editor's 20.</summary>
        [Unbound.EditorFaithful("measured: tests/projects/editorui HandleUtility.GetHandleSize")]
        public static float GetHandleSize(Vector3 position) => 20f;

        [Unbound.EditorFaithful("without a scene camera (measured: tests/projects/editorui HandleUtility.WorldToGUIPoint)")]
        public static Vector2 WorldToGUIPoint(Vector3 world) => Vector2.zero;
    }
}
