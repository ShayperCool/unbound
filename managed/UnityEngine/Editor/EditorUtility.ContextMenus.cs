using System;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    public partial class EditorUtility
    {
        public static void DisplayCustomMenu(Rect position, GUIContent[] options, int selected,
            SelectMenuItemFunction callback, object userData) =>
            DisplayCustomMenu(position, options, null, selected, callback, userData);

        public static void DisplayCustomMenu(Rect position, GUIContent[] options, int selected,
            SelectMenuItemFunction callback, object userData, bool showHotkey) =>
            DisplayCustomMenu(position, options, null, selected, callback, userData, showHotkey);

        /// <summary>Prepares the options and updates the mouse state as in the batch-mode Editor.
        /// Selection requires a menu window (tests/projects/displaycustommenu).</summary>
        public static void DisplayCustomMenu(Rect position, GUIContent[] options, Func<int, bool> checkEnabled,
            int selected, SelectMenuItemFunction callback, object userData, bool showHotkey = false)
        {
            if (options == null) throw MonoExceptions.NullReference();
            // Unity reads every option before evaluating the enable predicate.
            foreach (var option in options)
                if (option == null) throw MonoExceptions.NullReference();

            if (checkEnabled != null)
                for (int i = 0; i < options.Length; i++) checkEnabled(i);

            FinishCustomMenuRequest("UnityEditor.EditorUtility.DisplayCustomMenu");
        }

        public static void DisplayCustomMenuWithSeparators(Rect position, string[] options, bool[] enabled,
            bool[] separator, int[] selected, SelectMenuItemFunction callback, object userData) =>
            FinishCustomMenuRequest("UnityEditor.EditorUtility.DisplayCustomMenuWithSeparators");

        static void FinishCustomMenuRequest(string member)
        {
            ResetMouseDown();
            Stubs.Ignored(member, "cannot show an Editor context menu");
        }

        internal static void ResetMouseDown()
        {
            Tools.s_ButtonDown = -1;
            GUIUtility.hotControl = 0;
            Stubs.Ignored("UnityEditor.EditorUtility.ResetMouseDown", "cannot reset Editor shortcuts or deliver mouse-reset notifications");
        }
    }
}
