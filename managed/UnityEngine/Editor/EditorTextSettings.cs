using UnityEngine;
using UnityEngine.TextCore.Text;

namespace UnityEditor
{
    /// <summary>
    /// The text settings of the Editor's own text: IMGUI makes the font assets of its fonts in
    /// <see cref="defaultTextSettings"/> (the Unity 6000.0.71f1 Editor's IMGUI text settings are of
    /// this type, tests/projects/imguiskin calcText/imguiAsset/settings). The object is made here
    /// rather than loaded from the Editor's resources: its name, its fallback lists and the
    /// sharpness preference are not measured.
    /// </summary>
    [InitializeOnLoad]
    internal partial class EditorTextSettings : TextSettings
    {
        static EditorTextSettings s_DefaultTextSettings;

        public EditorTextSettings() { }

        internal static EditorTextSettings defaultTextSettings
        {
            get
            {
                if (s_DefaultTextSettings == null)
                {
                    s_DefaultTextSettings = ScriptableObject.CreateInstance<EditorTextSettings>();
                    s_DefaultTextSettings.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_DefaultTextSettings;
            }
        }
    }
}
