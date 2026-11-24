using System;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>EditorGUI.HelpBox and EditorGUILayout.HelpBox (Scripting API 6000.0; measured in
    /// tests/projects/imguiskin `helpBox/*` and tests/projects/editorstyles `helpBox/*`, Unity
    /// 6000.0.71f1).</summary>
    public sealed partial class EditorGUI
    {
        public static void HelpBox(Rect position, string message, MessageType type)
        {
            if (Unmeasured("UnityEditor.EditorGUI.HelpBox")) return;
            RequireGUI();
            HelpBox(position, EditorGUIUtility.TempHelpContent(message, EditorGUIUtility.GetHelpIcon(type)));
        }

        public static void HelpBox(Rect position, GUIContent content)
        {
            if (Unmeasured("UnityEditor.EditorGUI.HelpBox")) return;
            RequireGUI();
            GUI.Label(position, content, EditorStyles.helpBox);
        }
    }

    public sealed partial class EditorGUILayout
    {
        public static void HelpBox(string message, MessageType type) => HelpBox(message, type, true);

        public static void HelpBox(string message, MessageType type, bool wide)
        {
            if (EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.HelpBox")) return;
            EditorGUI.RequireGUI();
            HelpBox(EditorGUIUtility.TempHelpContent(message, EditorGUIUtility.GetHelpIcon(type)), wide);
        }

        public static void HelpBox(GUIContent content, bool wide = true)
        {
            if (EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.HelpBox")) return;
            EditorGUI.RequireGUI();
            LabelField(wide ? GUIContent.none : EditorGUIUtility.blankContent, content, EditorStyles.helpBox);
        }
    }

    public sealed partial class EditorGUIUtility
    {
        static readonly GUIContent s_TempHelpContent = new GUIContent();
        static readonly Texture2D[] s_LightHelpIcons = new Texture2D[3];
        static readonly Texture2D[] s_DarkHelpIcons = new Texture2D[3];

        internal static GUIContent blankContent { get; } = new GUIContent(" ");

        internal static Texture2D infoIcon => LoadHelpIcon(0, "console.infoicon");
        internal static Texture2D warningIcon => LoadHelpIcon(1, "console.warnicon");
        internal static Texture2D errorIcon => LoadHelpIcon(2, "console.erroricon");

        internal static Texture2D GetHelpIcon(MessageType type) => type switch
        {
            MessageType.Info => infoIcon,
            MessageType.Warning => warningIcon,
            MessageType.Error => errorIcon,
            _ => null,
        };

        internal static GUIContent TempHelpContent(string message, Texture image)
        {
            s_TempHelpContent.text = message;
            s_TempHelpContent.image = image;
            s_TempHelpContent.tooltip = string.Empty;
            return s_TempHelpContent;
        }

        static Texture2D LoadHelpIcon(int slot, string name)
        {
            bool dark = isProSkin;
            var cache = dark ? s_DarkHelpIcons : s_LightHelpIcons;
            if (cache[slot] != null) return cache[slot];
            string path = $"icons/{(dark ? "d_" : "")}{name}.png";
            var texture = Unbound.UIElements.UiResources.EditorLoad(path) as Texture2D
                ?? throw new InvalidOperationException($"Unbound: Editor help icon '{path}' is unavailable.");
            cache[slot] = texture;
            return texture;
        }
    }
}
