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

    /// <summary>The scene view's tools: the active one and the handle pivot settings, with the
    /// events the Editor raises when they change (measured: tests/projects/editorload tools.*).
    /// No scene view draws them.</summary>
    public sealed partial class Tools
    {
        static PivotMode s_PivotMode = PivotMode.Center;
        static PivotRotation s_PivotRotation = PivotRotation.Local;

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
            get => s_PivotMode;
            set
            {
                if (s_PivotMode == value) return;
                s_PivotMode = value;
                pivotModeChanged?.Invoke();
            }
        }

        public static PivotRotation pivotRotation
        {
            get => s_PivotRotation;
            set
            {
                if (s_PivotRotation == value) return;
                s_PivotRotation = value;
                pivotRotationChanged?.Invoke();
            }
        }
    }
}

namespace UnityEditor.EditorTools
{
    /// <summary>The active tool and tool context. The first query starts the Move tool, raising
    /// the tool change events once; choosing another tool raises them again (measured:
    /// tests/projects/editorload tools.*).</summary>
    public static partial class ToolManager
    {
        static Tool s_Current = Tool.Move;
        static bool s_Started;

        /// <summary>Raised before the active tool changes.</summary>
        public static event Action activeToolChanging;

        /// <summary>Raised after the active tool changed.</summary>
        public static event Action activeToolChanged;

        /// <summary>Raised when the tool context changes. The Editor raises it once in the first
        /// frames of Play mode, which Unbound does not reproduce (tests/projects/editorload
        /// later.events, a recorded residual); Unbound's context does not change.</summary>
        public static event Action activeContextChanged;

        public static Type activeToolType
        {
            get
            {
                Start();
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
                Start();
                return EditorType("UnityEditor.EditorTools.GameObjectToolContext");
            }
        }

        internal static Tool Current
        {
            get
            {
                Start();
                return s_Current;
            }
        }

        internal static void Select(Tool tool)
        {
            Start();
            if (tool == s_Current) return;
            Change(tool);
        }

        static void Start()
        {
            if (s_Started) return;
            s_Started = true;
            Change(Tool.Move);
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
