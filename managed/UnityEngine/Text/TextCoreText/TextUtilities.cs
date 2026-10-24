using System;
using System.Collections.Generic;

namespace UnityEngine.TextCore.Text
{
    internal static partial class TextUtilities
    {
        /// <summary>TextCore's case folding for hashes: ASCII letters to upper case, the ASCII
        /// characters that cannot be part of a name (controls, quotes, backslash, DEL) to '-', the
        /// rest unchanged.</summary>
        internal static char ToUpperFast(char c)
        {
            if (c > 127) return c;
            if (c < 32 || c == '"' || c == '\'' || c == '\\' || c == 127) return '-';
            return c >= 'a' && c <= 'z' ? (char)(c - 32) : c;
        }

        internal static char ToLowerFast(char c)
        {
            if (c > 127) return c;
            if (c < 32 || c == '"' || c == '\'' || c == '\\' || c == 127) return '-';
            return c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
        }

        internal static uint ToUpperASCIIFast(uint c) => c > 127 ? c : ToUpperFast((char)c);

        internal static uint ToLowerASCIIFast(uint c) => c > 127 ? c : ToLowerFast((char)c);

        /// <summary>The hash TextCore names things by: <c>h = (h &lt;&lt; 5) + h ^ upper(c)</c>.</summary>
        public static int GetHashCodeCaseInSensitive(string s)
        {
            int hash = 0;
            if (s == null) return 0;
            for (int i = 0; i < s.Length; i++) hash = ((hash << 5) + hash) ^ ToUpperFast(s[i]);
            return hash;
        }

        public static int GetHashCodeCaseSensitive(string s)
        {
            int hash = 0;
            if (s == null) return 0;
            for (int i = 0; i < s.Length; i++) hash = ((hash << 5) + hash) ^ s[i];
            return hash;
        }

        internal static int GetTextFontWeightIndex(TextFontWeight fontWeight) => fontWeight switch
        {
            TextFontWeight.Thin => 1,
            TextFontWeight.ExtraLight => 2,
            TextFontWeight.Light => 3,
            TextFontWeight.Regular => 4,
            TextFontWeight.Medium => 5,
            TextFontWeight.SemiBold => 6,
            TextFontWeight.Bold => 7,
            TextFontWeight.Heavy => 8,
            TextFontWeight.Black => 9,
            _ => 4,
        };
    }

    public static partial class TextShaderUtilities
    {
        public static int ID_MainTex;
        public static int ID_FaceDilate;
        public static int ID_WeightNormal;
        public static int ID_WeightBold;
        public static int ID_OutlineWidth;
        public static int ID_OutlineSoftness;
        public static int ID_Padding;
        public static int ID_GradientScale;
        public static int ID_ScaleX;
        public static int ID_ScaleY;
        public static int ID_Sharpness;
        public static int ID_TextureWidth;
        public static int ID_TextureHeight;
        public static int ID_ClipRect;
        public static bool isInitialized;

        internal static readonly string k_SDFText = "Hidden/TextCore/Distance Field SSD";
        internal static readonly string k_BitmapText = "Hidden/Internal-GUITextureClipText";
        internal static readonly string k_SpriteText = "Hidden/TextCore/Sprite";

        static Shader s_MobileSdf;
        static Shader s_MobileBitmap;
        static Shader s_Sprite;

        static TextShaderUtilities() => GetShaderPropertyIDs();

        internal static void GetShaderPropertyIDs()
        {
            if (isInitialized) return;
            isInitialized = true;
            ID_MainTex = Shader.PropertyToID("_MainTex");
            ID_FaceDilate = Shader.PropertyToID("_FaceDilate");
            ID_WeightNormal = Shader.PropertyToID("_WeightNormal");
            ID_WeightBold = Shader.PropertyToID("_WeightBold");
            ID_OutlineWidth = Shader.PropertyToID("_OutlineWidth");
            ID_OutlineSoftness = Shader.PropertyToID("_OutlineSoftness");
            ID_Padding = Shader.PropertyToID("_Padding");
            ID_GradientScale = Shader.PropertyToID("_GradientScale");
            ID_ScaleX = Shader.PropertyToID("_ScaleX");
            ID_ScaleY = Shader.PropertyToID("_ScaleY");
            ID_Sharpness = Shader.PropertyToID("_Sharpness");
            ID_TextureWidth = Shader.PropertyToID("_TextureWidth");
            ID_TextureHeight = Shader.PropertyToID("_TextureHeight");
            ID_ClipRect = Shader.PropertyToID("_ClipRect");
        }

        /// <summary>The shaders of font asset materials. UI Toolkit draws text with its own shader
        /// (reading the material's atlas and properties); when the Editor resource shader is not
        /// available a placeholder of the same name stands in.</summary>
        internal static Shader ShaderRef_MobileSDF => s_MobileSdf ??= FindOrCreate(k_SDFText);

        internal static Shader ShaderRef_MobileBitmap => s_MobileBitmap ??= FindOrCreate(k_BitmapText);

        internal static Shader ShaderRef_Sprite => s_Sprite ??= FindOrCreate(k_SpriteText);

        static Shader FindOrCreate(string name)
        {
            Shader shader = null;
            try
            {
                shader = Shader.Find(name);
            }
            catch (Exception)
            {
            }
            if (shader != null) return shader;
            shader = Shader.Create(name, 3000);
            Shader.Register(shader);
            return shader;
        }
    }

    /// <summary>The font assets in use, by name and by family and style.</summary>
    internal partial class TextResourceManager
    {
        static readonly Dictionary<int, FontAsset> s_ByName = new Dictionary<int, FontAsset>();
        static readonly Dictionary<long, FontAsset> s_ByFamily = new Dictionary<long, FontAsset>();
        static readonly List<FontAsset> s_FontAssets = new List<FontAsset>();

        internal static void AddFontAsset(FontAsset fontAsset)
        {
            if (fontAsset == null || s_FontAssets.Contains(fontAsset)) return;
            s_FontAssets.Add(fontAsset);
            s_ByName.TryAdd(fontAsset.hashCode, fontAsset);
            s_ByFamily.TryAdd(FamilyKey(fontAsset.familyNameHashCode, fontAsset.styleNameHashCode), fontAsset);
        }

        public static void RemoveFontAsset(FontAsset fontAsset)
        {
            if (fontAsset == null || !s_FontAssets.Remove(fontAsset)) return;
            if (s_ByName.TryGetValue(fontAsset.hashCode, out var named) && named == fontAsset) s_ByName.Remove(fontAsset.hashCode);
            long key = FamilyKey(fontAsset.familyNameHashCode, fontAsset.styleNameHashCode);
            if (s_ByFamily.TryGetValue(key, out var family) && family == fontAsset) s_ByFamily.Remove(key);
        }

        internal static bool TryGetFontAssetByName(int nameHashcode, out FontAsset fontAsset) => s_ByName.TryGetValue(nameHashcode, out fontAsset);

        internal static bool TryGetFontAssetByFamilyName(int familyNameHashCode, int styleNameHashCode, out FontAsset fontAsset) =>
            s_ByFamily.TryGetValue(FamilyKey(familyNameHashCode, styleNameHashCode), out fontAsset);

        static long FamilyKey(int family, int style) => ((long)family << 32) | (uint)style;
    }

    /// <summary>The materials of a font asset's other atlas textures (and of fallback fonts in a
    /// font asset's preset): copies of the source material with that atlas, made once.</summary>
    internal static partial class MaterialManager
    {
        static readonly Dictionary<long, Material> s_FallbackMaterials = new Dictionary<long, Material>();

        public static Material GetFallbackMaterial(Material sourceMaterial, Material targetMaterial)
        {
            long key = ((long)sourceMaterial.GetHashCode() << 32) | (uint)targetMaterial.GetHashCode();
            if (s_FallbackMaterials.TryGetValue(key, out var material) && material != null) return material;
            if (sourceMaterial.HasProperty(TextShaderUtilities.ID_GradientScale) && targetMaterial.HasProperty(TextShaderUtilities.ID_GradientScale))
            {
                var texture = targetMaterial.GetTexture(TextShaderUtilities.ID_MainTex);
                material = new Material(sourceMaterial) { hideFlags = HideFlags.HideAndDontSave };
                material.name = material.name + " + " + (texture != null ? texture.name : "");
                material.SetTexture(TextShaderUtilities.ID_MainTex, texture);
                foreach (var id in new[] { TextShaderUtilities.ID_GradientScale, TextShaderUtilities.ID_TextureWidth, TextShaderUtilities.ID_TextureHeight, TextShaderUtilities.ID_WeightNormal, TextShaderUtilities.ID_WeightBold })
                    material.SetFloat(id, targetMaterial.GetFloat(id));
            }
            else
            {
                material = new Material(targetMaterial);
            }
            s_FallbackMaterials[key] = material;
            return material;
        }

        public static Material GetFallbackMaterial(FontAsset fontAsset, Material sourceMaterial, int atlasIndex)
        {
            Texture texture = fontAsset.atlasTextures[atlasIndex];
            long key = ((long)sourceMaterial.GetHashCode() << 32) | (uint)texture.GetHashCode();
            if (s_FallbackMaterials.TryGetValue(key, out var material) && material != null) return material;
            material = new Material(sourceMaterial) { hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture(TextShaderUtilities.ID_MainTex, texture);
            material.name = material.name + " + " + texture.name;
            s_FallbackMaterials[key] = material;
            return material;
        }
    }
}
