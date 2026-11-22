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
                    s_DefaultTextSettings.RegisterSystemFonts();
                }
                return s_DefaultTextSettings;
            }
        }

        /// <summary>The OS font family the Editor's system fonts are drawn with on Linux.</summary>
        const string LinuxSystemFontFamily = "Liberation Sans";

        static readonly string[] s_SystemFonts =
        {
            "Fonts/System/System Normal.ttf", "Fonts/System/System Big.ttf",
            "Fonts/System/System Small.ttf", "Fonts/System/System Warning.ttf",
        };

        static readonly string[] s_SystemBoldFonts = { "Fonts/System/System Normal Bold.ttf", "Fonts/System/System Small Bold.ttf" };

        /// <summary>
        /// The Editor's system fonts (`Fonts/System/*` of its resources, which older styles of
        /// its skins still name) are references to OS fonts and carry no glyphs: their text is
        /// that of the OS family's font assets, the regular one with the family's bold and
        /// italic faces, the bold fonts with the bold face (matches the Unity 6000.0.71f1 Editor
        /// on Linux, tests/projects/editorskin custom/PreButtonBlue and custom/NotificationText).
        /// Not measured on macOS, where the fonts keep the assets made of their own data.
        /// </summary>
        void RegisterSystemFonts()
        {
            if (!System.OperatingSystem.IsLinux()) return;
            var regular = OsFontAsset("Regular");
            if (regular != null)
            {
                var bold = OsFontAsset("Bold");
                if (bold != null) regular.fontWeightTable[7].regularTypeface = bold;
                var italic = OsFontAsset("Italic");
                if (italic != null) regular.fontWeightTable[4].italicTypeface = italic;
                var boldItalic = OsFontAsset("Bold Italic");
                if (boldItalic != null) regular.fontWeightTable[7].italicTypeface = boldItalic;
                Register(regular, s_SystemFonts);
            }
            Register(OsFontAsset("Bold"), s_SystemBoldFonts);
        }

        static FontAsset OsFontAsset(string style)
        {
            var asset = FontAsset.CreateFontAssetInternal(LinuxSystemFontFamily, style, 90);
            if (asset == null) return null;
            asset.InternalDynamicOS = true;
            FontAssetFactory.SetupFontAssetSettings(asset);
            return asset;
        }

        void Register(FontAsset asset, string[] paths)
        {
            if (asset == null) return;
            foreach (string path in paths)
            {
                var font = Unbound.UIElements.UiResources.EditorLoad(path) as Font;
                if (font == null || m_FontReferences.Exists(r => r.font == font)) continue;
                m_FontReferences.Add(new FontReferenceMap(font, asset));
                m_FontLookup?.TryAdd(font.GetHashCode(), asset);
            }
        }

        static float s_CurrentEditorSharpness;

        internal static void SetCurrentEditorSharpness(float sharpness) => s_CurrentEditorSharpness = sharpness;

        /// <summary>The fallback font assets of the settings. (The Editor's bitmap text rendering
        /// mode, which has fallbacks per pixel size, is not implemented: text is generated from
        /// distance fields, the Editor's default mode.)</summary>
        internal override System.Collections.Generic.List<FontAsset> GetFallbackFontAssets(int textPixelSize = -1) => fallbackFontAssets;

        /// <summary>The sharpness of Editor text: what was set, 0 at first (the preference the
        /// Editor reads it from is not read).</summary>
        internal override float GetEditorTextSharpness() => s_CurrentEditorSharpness;

        /// <summary>The Editor's UI font, the font of the Editor's GUI skin.</summary>
        internal override Font GetEditorFont() => Unbound.Runtime.EditorGuiSkins.EditorFont;
    }
}
