using System;
using UnityEditor.EditorTools;

namespace UnityEditor
{
    public enum Tool
    {
        View = 0,
        Move = 1,
        Rotate = 2,
        Scale = 3,
        Rect = 4,
        Transform = 5,
        Custom = 6,
        None = -1,
    }

    public enum PivotMode
    {
        Center = 0,
        Pivot = 1,
    }

    public enum PivotRotation
    {
        Local = 0,
        Global = 1,
    }

    /// <summary>The scene view's tools: the active one and the handle pivot settings, kept in the
    /// Editor preferences "PivotMode" and "PivotRotation" as the Editor keeps them, with the events
    /// the Editor raises when they change (measured: tests/projects/editorload tools.*). No scene
    /// view draws them.</summary>
    public sealed partial class Tools
    {
        static PivotMode? s_PivotMode;
        static PivotRotation? s_PivotRotation;

        public static Tool current
        {
            get => ToolManager.Current;
            set => ToolManager.Select(value);
        }

        /// <summary>Raised when the pivot mode changes to another value.</summary>
        public static event Action pivotModeChanged;

        /// <summary>Raised when the pivot rotation changes to another value.</summary>
        public static event Action pivotRotationChanged;

        public static PivotMode pivotMode
        {
            get => s_PivotMode ??= (PivotMode)EditorPrefs.GetInt("PivotMode", 0);
            set
            {
                if (pivotMode == value) return;
                s_PivotMode = value;
                EditorPrefs.SetInt("PivotMode", (int)value);
                pivotModeChanged?.Invoke();
            }
        }

        public static PivotRotation pivotRotation
        {
            get => s_PivotRotation ??= (PivotRotation)EditorPrefs.GetInt("PivotRotation", 0);
            set
            {
                if (pivotRotation == value) return;
                s_PivotRotation = value;
                EditorPrefs.SetInt("PivotRotation", (int)value);
                pivotRotationChanged?.Invoke();
            }
        }
    }
}

namespace UnityEditor.EditorTools
{
    /// <summary>The active tool and tool context. The first query of the context starts it
    /// (raising activeContextChanged); the first read of Tools.current also starts the Move tool
    /// (raising the tool change events); choosing another tool raises them again (measured:
    /// tests/projects/editorload tools.*).</summary>
    public static partial class ToolManager
    {
        static Tool s_Current = Tool.Move;
        static bool s_ToolStarted, s_ContextStarted;

        /// <summary>Raised before the active tool changes.</summary>
        public static event Action activeToolChanging;

        /// <summary>Raised after the active tool changed.</summary>
        public static event Action activeToolChanged;

        /// <summary>Raised when the tool context changes: when the first query starts it.</summary>
        public static event Action activeContextChanged;

        /// <summary>The active tool's class; none until a tool starts.</summary>
        public static Type activeToolType
        {
            get
            {
                if (!s_ToolStarted) return null;
                return EditorType(s_Current switch
                {
                    Tool.View => "UnityEditor.ViewModeTool",
                    Tool.Move => "UnityEditor.MoveTool",
                    Tool.Rotate => "UnityEditor.RotateTool",
                    Tool.Scale => "UnityEditor.ScaleTool",
                    Tool.Rect => "UnityEditor.RectTool",
                    Tool.Transform => "UnityEditor.TransformTool",
                    _ => "UnityEditor.NoneTool",
                });
            }
        }

        public static Type activeContextType
        {
            get
            {
                StartContext();
                return EditorType("UnityEditor.EditorTools.GameObjectToolContext");
            }
        }

        internal static Tool Current
        {
            get
            {
                StartContext();
                if (!s_ToolStarted)
                {
                    s_ToolStarted = true;
                    Change(Tool.Move);
                }
                return s_Current;
            }
        }

        /// <summary>Chooses a built-in tool; Custom, with no custom tool to choose, selects none
        /// (measured: tests/projects/editorload tools.current_Custom).</summary>
        internal static void Select(Tool tool)
        {
            StartContext();
            if (tool == Tool.Custom) tool = Tool.None;
            if (s_ToolStarted && tool == s_Current) return;
            s_ToolStarted = true;
            Change(tool);
        }

        static void StartContext()
        {
            if (s_ContextStarted) return;
            s_ContextStarted = true;
            activeContextChanged?.Invoke();
        }

        static void Change(Tool tool)
        {
            activeToolChanging?.Invoke();
            s_Current = tool;
            activeToolChanged?.Invoke();
        }

        // The built-in tools are the Editor's own classes, found by name.
        static Type EditorType(string name) => typeof(ToolManager).Assembly.GetType(name);
    }
}
