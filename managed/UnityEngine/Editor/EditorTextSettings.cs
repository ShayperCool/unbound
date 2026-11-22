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

        /// <summary>The OS font family the Editor's system fonts are drawn with on Linux.</summary>
        const string LinuxSystemFontFamily = "Liberation Sans";

        /// <summary>A family of the Editor's own fonts: the fonts whose text is made with the
        /// family's asset, and the faces of the asset (regular, then the bold, italic and bold
        /// italic ones of its weight table). A face is a font of the Editor's resources by
        /// path, or a style of the OS family.</summary>
        sealed class Family
        {
            internal string[] Fonts;
            internal string Regular, Bold, Italic, BoldItalic;
            internal bool Os;
            internal FontAsset Asset;
            internal bool Made;
        }

        /// <summary>
        /// The font families of the Editor (matches the Unity 6000.0.71f1 Editor on Linux,
        /// tests/projects/editorskin font/*). The fonts under Fonts/System of its resources, which
        /// older styles of its skins still name, are references to OS fonts and carry no glyphs:
        /// the regular ones are drawn with the OS family (bold and italic text with its bold and
        /// italic faces), the bold ones with its bold face. Inter-Regular, the Editor's UI font,
        /// has the Inter faces for its styles, bold text being Semi Bold. Every Roboto Mono font,
        /// the bold and italic ones too, is drawn with the regular face's family asset.
        /// On macOS the system fonts' OS family is not measured: they keep the asset made of
        /// their own data.
        /// </summary>
        static readonly Family[] s_Families =
        {
            new Family
            {
                Fonts = new[] { "System Normal", "System Big", "System Small", "System Warning" },
                Os = true, Regular = "Regular", Bold = "Bold", Italic = "Italic", BoldItalic = "Bold Italic",
            },
            new Family { Fonts = new[] { "System Normal Bold", "System Small Bold" }, Os = true, Regular = "Bold" },
            new Family
            {
                Fonts = new[] { "Inter-Regular" },
                Regular = "Fonts/Inter/Inter-Regular.ttf", Bold = "Fonts/Inter/Inter-SemiBold.ttf",
                Italic = "Fonts/Inter/Inter-Italic.ttf", BoldItalic = "Fonts/Inter/Inter-SemiBoldItalic.ttf",
            },
            new Family
            {
                Fonts = new[] { "RobotoMono-Regular", "RobotoMono-Bold", "RobotoMono-Italic", "RobotoMono-BoldItalic" },
                Regular = "Fonts/RobotoMono/RobotoMono-Regular.ttf", Bold = "Fonts/RobotoMono/RobotoMono-Bold.ttf",
                Italic = "Fonts/RobotoMono/RobotoMono-Italic.ttf", BoldItalic = "Fonts/RobotoMono/RobotoMono-BoldItalic.ttf",
            },
        };

        /// <summary>The slots of a font asset's weight table for the regular (400) and bold (700) weights.</summary>
        const int RegularWeight = 4, BoldWeight = 7;

        private protected override FontAsset MakeFontAsset(Font font)
        {
            if (font.m_UaEditorResourceId != 0)
                foreach (var family in s_Families)
                {
                    if (System.Array.IndexOf(family.Fonts, font.name) < 0) continue;
                    var asset = FamilyAsset(family);
                    if (asset != null) return asset;
                    break;
                }
            return base.MakeFontAsset(font);
        }

        static FontAsset FamilyAsset(Family family)
        {
            if (family.Made) return family.Asset;
            family.Made = true;
            if (family.Os && !System.OperatingSystem.IsLinux()) return null;
            var regular = Face(family, family.Regular);
            if (regular == null) return null;
            var bold = Face(family, family.Bold);
            if (bold != null) regular.fontWeightTable[BoldWeight].regularTypeface = bold;
            var italic = Face(family, family.Italic);
            if (italic != null) regular.fontWeightTable[RegularWeight].italicTypeface = italic;
            var boldItalic = Face(family, family.BoldItalic);
            if (boldItalic != null) regular.fontWeightTable[BoldWeight].italicTypeface = boldItalic;
            return family.Asset = regular;
        }

        static FontAsset Face(Family family, string face)
        {
            if (face == null) return null;
            if (!family.Os)
                return FontAssetFactory.ConvertFontToFontAsset(Unbound.UIElements.UiResources.EditorLoad(face) as Font);
            var asset = FontAsset.CreateFontAssetInternal(LinuxSystemFontFamily, face, 90);
            if (asset == null) return null;
            asset.InternalDynamicOS = true;
            FontAssetFactory.SetupFontAssetSettings(asset);
            return asset;
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
