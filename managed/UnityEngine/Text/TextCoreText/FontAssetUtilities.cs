using System.Collections.Generic;
using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.Text
{
    /// <summary>How UI Toolkit and IMGUI make font assets of Fonts.</summary>
    internal partial class FontAssetFactory
    {
        /// <summary>The font asset a TextSettings caches for a Font: dynamic, 90 points sampling,
        /// padding 9, a 1024 x 1024 SDF atlas, more atlases as it fills.</summary>
        internal static FontAsset ConvertFontToFontAsset(Font font)
        {
            if (font == null) return null;
            var fontAsset = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.DEFAULT, 1024, 1024);
            if (fontAsset != null) SetupFontAssetSettings(fontAsset);
            return fontAsset;
        }

        internal static void SetupFontAssetSettings(FontAsset fontAsset)
        {
            if (fontAsset == null) return;
            SetHideFlags(fontAsset);
            fontAsset.isMultiAtlasTexturesEnabled = true;
            fontAsset.IsEditorFont = true;
        }

        public static void SetHideFlags(FontAsset fontAsset)
        {
            if (fontAsset == null) return;
            fontAsset.hideFlags = HideFlags.DontSave;
            if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
                fontAsset.atlasTextures[0].hideFlags = HideFlags.DontSave;
            if (fontAsset.material != null) fontAsset.material.hideFlags = HideFlags.DontSave;
        }
    }

    /// <summary>Finds the character for a code point in a font asset, its weight and italic
    /// typefaces and its fallbacks, adding it to dynamic font assets that lack it.</summary>
    internal static partial class FontAssetUtilities
    {
        static HashSet<int> s_SearchedAssets;

        static void BeginSearch(bool includeFallbacks)
        {
            if (!includeFallbacks) return;
            if (s_SearchedAssets == null) s_SearchedAssets = new HashSet<int>();
            else s_SearchedAssets.Clear();
        }

        internal static Character GetCharacterFromFontAsset(uint unicode, FontAsset sourceFontAsset, bool includeFallbacks, FontStyles fontStyle, TextFontWeight fontWeight, out bool isAlternativeTypeface, bool populateLigatures)
        {
            BeginSearch(includeFallbacks);
            return FindCharacter(unicode, sourceFontAsset, includeFallbacks, fontStyle, fontWeight, out isAlternativeTypeface, populateLigatures);
        }

        /// <summary>A styled lookup first tries the font asset's cache for that style, then the
        /// typeface of the style's weight (its cache, then adding to it), then the regular character
        /// (whose glyph the generator emboldens or slants).</summary>
        static Character FindCharacter(uint unicode, FontAsset fontAsset, bool includeFallbacks, FontStyles fontStyle, TextFontWeight fontWeight, out bool isAlternativeTypeface, bool populateLigatures)
        {
            bool onMainThread = !TextGenerator.IsExecutingJob;
            isAlternativeTypeface = false;
            Character character;
            bool italic = (fontStyle & FontStyles.Italic) == FontStyles.Italic;
            if (italic || fontWeight != TextFontWeight.Regular)
            {
                if (!onMainThread && fontAsset.m_CharacterLookupDictionary == null) return null;
                if (TryCached(fontAsset, unicode, fontStyle, fontWeight, onMainThread, out character, out bool stop)) return character;
                if (stop) return null;
                var weights = fontAsset.fontWeightTable;
                int weightIndex = TextUtilities.GetTextFontWeightIndex(fontWeight);
                var typeface = weights != null && weightIndex < weights.Length ? (italic ? weights[weightIndex].italicTypeface : weights[weightIndex].regularTypeface) : null;
                if (typeface != null)
                {
                    if (!onMainThread && typeface.m_CharacterLookupDictionary == null) return null;
                    if (TryCached(typeface, unicode, fontStyle, fontWeight, onMainThread, out character, out stop))
                    {
                        isAlternativeTypeface = true;
                        return character;
                    }
                    if (stop) return null;
                    if (typeface.atlasPopulationMode == AtlasPopulationMode.Dynamic || typeface.atlasPopulationMode == AtlasPopulationMode.DynamicOS)
                    {
                        if (!onMainThread)
                        {
                            if (!typeface.m_MissingUnicodesFromFontFile.Contains(unicode)) return null;
                        }
                        else if (typeface.TryAddCharacterInternal(unicode, fontStyle, fontWeight, out character, populateLigatures))
                        {
                            isAlternativeTypeface = true;
                            return character;
                        }
                    }
                    else if (typeface.GetCharacterInLookupCache(unicode, FontStyles.Normal, TextFontWeight.Regular, out character))
                    {
                        if (character.textAsset != null)
                        {
                            isAlternativeTypeface = true;
                            return character;
                        }
                        if (!onMainThread) return null;
                        typeface.RemoveCharacterInLookupCache(unicode, fontStyle, fontWeight);
                    }
                }
            }
            if (!onMainThread && fontAsset.m_CharacterLookupDictionary == null) return null;
            if (fontAsset.GetCharacterInLookupCache(unicode, FontStyles.Normal, TextFontWeight.Regular, out character))
            {
                if (character.textAsset != null) return character;
                if (!onMainThread) return null;
                fontAsset.RemoveCharacterInLookupCache(unicode, FontStyles.Normal, TextFontWeight.Regular);
            }
            character = null;
            if (fontAsset.atlasPopulationMode == AtlasPopulationMode.Dynamic || fontAsset.atlasPopulationMode == AtlasPopulationMode.DynamicOS)
            {
                if (!onMainThread) return null;
                if (fontAsset.TryAddCharacterInternal(unicode, FontStyles.Normal, TextFontWeight.Regular, out character, populateLigatures)) return character;
            }
            if (character == null && !onMainThread) return null;
            if (!includeFallbacks || fontAsset.fallbackFontAssetTable == null) return null;
            var fallbacks = fontAsset.fallbackFontAssetTable;
            for (int i = 0; i < fallbacks.Count; i++)
            {
                var fallback = fallbacks[i];
                if (fallback == null || !s_SearchedAssets.Add(fallback.GetHashCode())) continue;
                character = FindCharacter(unicode, fallback, true, fontStyle, fontWeight, out isAlternativeTypeface, populateLigatures);
                if (character != null) return character;
            }
            return null;
        }

        /// <summary>A cached character with a font asset; a cached one without (its asset was
        /// unloaded) is dropped, or ends the search off the main thread.</summary>
        static bool TryCached(FontAsset fontAsset, uint unicode, FontStyles fontStyle, TextFontWeight fontWeight, bool onMainThread, out Character character, out bool stop)
        {
            stop = false;
            if (!fontAsset.GetCharacterInLookupCache(unicode, fontStyle, fontWeight, out character)) return false;
            if (character.textAsset != null) return true;
            if (!onMainThread)
            {
                stop = true;
                return false;
            }
            fontAsset.RemoveCharacterInLookupCache(unicode, fontStyle, fontWeight);
            character = null;
            return false;
        }

        public static Character GetCharacterFromFontAssets(uint unicode, FontAsset sourceFontAsset, List<FontAsset> fontAssets, List<FontAsset> OSFallbackList, bool includeFallbacks, FontStyles fontStyle, TextFontWeight fontWeight, out bool isAlternativeTypeface) =>
            GetCharacterFromFontAssetsInternal(unicode, sourceFontAsset, fontAssets, OSFallbackList, includeFallbacks, fontStyle, fontWeight, out isAlternativeTypeface);

        /// <summary>The first of a list of font assets (then of the OS fallbacks) with the character.</summary>
        internal static Character GetCharacterFromFontAssetsInternal(uint unicode, FontAsset sourceFontAsset, List<FontAsset> fontAssets, List<FontAsset> OSFallbackList, bool includeFallbacks, FontStyles fontStyle, TextFontWeight fontWeight, out bool isAlternativeTypeface, bool populateLigatures = true)
        {
            BeginSearch(includeFallbacks);
            return FindInList(unicode, fontAssets, includeFallbacks, fontStyle, fontWeight, out isAlternativeTypeface, populateLigatures)
                ?? FindInList(unicode, OSFallbackList, includeFallbacks, fontStyle, fontWeight, out isAlternativeTypeface, populateLigatures);
        }

        static Character FindInList(uint unicode, List<FontAsset> fontAssets, bool includeFallbacks, FontStyles fontStyle, TextFontWeight fontWeight, out bool isAlternativeTypeface, bool populateLigatures)
        {
            isAlternativeTypeface = false;
            if (fontAssets == null) return null;
            foreach (var fontAsset in fontAssets)
            {
                if (fontAsset == null) continue;
                var character = FindCharacter(unicode, fontAsset, includeFallbacks, fontStyle, fontWeight, out isAlternativeTypeface, populateLigatures);
                if (character != null) return character;
            }
            return null;
        }

        /// <summary>Emoji fallbacks: the first font asset of the list with the character (sprite
        /// assets are not supported).</summary>
        internal static TextElement GetTextElementFromTextAssets(uint unicode, FontAsset sourceFontAsset, List<TextAsset> textAssets, bool includeFallbacks, FontStyles fontStyle, TextFontWeight fontWeight, out bool isAlternativeTypeface, bool populateLigatures)
        {
            isAlternativeTypeface = false;
            if (textAssets == null) return null;
            BeginSearch(includeFallbacks);
            foreach (var textAsset in textAssets)
            {
                if (textAsset == null || textAsset.GetType() != typeof(FontAsset)) continue;
                var fontAsset = (FontAsset)textAsset;
                var character = FindCharacter(unicode, fontAsset, includeFallbacks, fontStyle, fontWeight, out isAlternativeTypeface, populateLigatures);
                if (character != null) return character;
            }
            return null;
        }

        /// <summary>The code point at <paramref name="index"/>, advancing past a surrogate pair.</summary>
        public static uint GetCodePoint(string text, ref int index)
        {
            char c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                return (uint)char.ConvertToUtf32(c, text[++index]);
            return c;
        }

        /// <summary>Code units are read as UTF-16 (a value past 0xFFFF is truncated, as in Unity).</summary>
        public static uint GetCodePoint(uint[] codesPoints, ref int index)
        {
            char c = (char)codesPoints[index];
            if (char.IsHighSurrogate(c) && index + 1 < codesPoints.Length && char.IsLowSurrogate((char)codesPoints[index + 1]))
                return (uint)char.ConvertToUtf32(c, (char)codesPoints[++index]);
            return c;
        }
    }
}
