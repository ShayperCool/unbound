using UnityEngine;


namespace UnityEditor.IMGUI.Controls
{
    public partial class MultiColumnHeader
    {
        public static partial class DefaultGUI
        {
            [Unbound.EditorFaithful("Unity's C#: the header's fixed metric")]
            public static float defaultHeight { get { return 27f; } }
            [Unbound.EditorFaithful("Unity's C#: the header's fixed metric")]
            public static float minimumHeight { get { return 20f; } }
            [Unbound.EditorFaithful("Unity's C#: the header's fixed metric")]
            public static float columnContentMargin { get { return 3f; } }
            internal static float labelSpaceFromBottom { get { return 3; } }
        }

        public static partial class DefaultStyles
        {
            // Column header uses same colors as label, but slightly dimmed to let list items stand out more (similar to win 10)
            // Use middle alignment to ensure labels are aligned regardless of icon size
            public static GUIStyle columnHeader = UnityEditor.TreeViewEditorStyles.Get("MultiColumnHeader");
            public static GUIStyle columnHeaderRightAligned = UnityEditor.TreeViewEditorStyles.Get("MultiColumnHeaderRight");
            public static GUIStyle columnHeaderCenterAligned = UnityEditor.TreeViewEditorStyles.Get("MultiColumnHeaderCenter");
            public static GUIStyle background = UnityEditor.TreeViewEditorStyles.Get("MultiColumnTopBar"); // Use bar background with 1 pixel line at the bottom to let items have someting to clip against when scrolled
            // Arrow uses same colors as label (derived) aligned at top
            internal static GUIStyle arrowStyle = UnityEditor.TreeViewEditorStyles.Get("MultiColumnArrow");
        }
    }
}
