using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEditor
{
    /// <summary>
    /// EditorGUI's texture previews: Unity's C# (DrawPreviewTextureInternal, DrawTextureAlphaInternal,
    /// DrawTextureTransparentInternal, DrawTransparencyCheckerTexture, GetMaterialForSpecialTexture)
    /// drawing with the Editor's preview shaders, compiled from the release's built-in shader
    /// sources (EditorDefaultResources/Previews) like any built-in shader. Measured against the
    /// Unity 6000.0.71f1 Editor in Play mode (tests/projects/editorpreviewtexture): the pixels of a
    /// colour texture, its mips, colour masks, exposure, scale modes, GUI.color, the alpha and
    /// transparent previews, an alpha-only texture and a normal map; the properties set on a
    /// caller's material; the error of a transparent preview without image or aspect; outside OnGUI.
    /// Virtual texturing previews are not supported (Unbound has no virtual texturing).
    /// </summary>
    public sealed partial class EditorGUI
    {
        public static void DrawPreviewTexture(Rect position, Texture image) =>
            DrawPreviewTexture(position, image, null, ScaleMode.StretchToFill, 0);

        public static void DrawPreviewTexture(Rect position, Texture image, Material mat) =>
            DrawPreviewTexture(position, image, mat, ScaleMode.StretchToFill, 0);

        public static void DrawPreviewTexture(Rect position, Texture image, Material mat, ScaleMode scaleMode) =>
            DrawPreviewTexture(position, image, mat, scaleMode, 0);

        public static void DrawPreviewTexture(Rect position, Texture image, Material mat, ScaleMode scaleMode, float imageAspect) =>
            DrawPreviewTexture(position, image, mat, scaleMode, imageAspect, -1);

        public static void DrawPreviewTexture(Rect position, Texture image, Material mat, ScaleMode scaleMode, float imageAspect, float mipLevel) =>
            DrawPreviewTexture(position, image, mat, scaleMode, imageAspect, mipLevel, ColorWriteMask.All);

        public static void DrawPreviewTexture(Rect position, Texture image, Material mat, ScaleMode scaleMode, float imageAspect, float mipLevel, ColorWriteMask colorWriteMask) =>
            DrawPreviewTexture(position, image, mat, scaleMode, imageAspect, mipLevel, colorWriteMask, 0);

        public static void DrawPreviewTexture(Rect position, Texture image, Material mat, ScaleMode scaleMode, float imageAspect, float mipLevel, ColorWriteMask colorWriteMask, float exposure)
        {
            if (!Compat.EditorPreviewsMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUI.DrawPreviewTexture"); return; }
            DrawPreviewTextureInternal(position, image, mat, scaleMode, imageAspect, mipLevel, colorWriteMask, exposure);
        }

        public static void DrawTextureAlpha(Rect position, Texture image) =>
            DrawTextureAlpha(position, image, ScaleMode.StretchToFill, 0, -1);

        public static void DrawTextureAlpha(Rect position, Texture image, ScaleMode scaleMode) =>
            DrawTextureAlpha(position, image, scaleMode, 0, -1);

        public static void DrawTextureAlpha(Rect position, Texture image, ScaleMode scaleMode, float imageAspect) =>
            DrawTextureAlpha(position, image, scaleMode, imageAspect, -1);

        public static void DrawTextureAlpha(Rect position, Texture image, ScaleMode scaleMode, float imageAspect, float mipLevel)
        {
            if (!Compat.EditorPreviewsMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUI.DrawTextureAlpha"); return; }
            DrawTextureAlphaInternal(position, image, scaleMode, imageAspect, mipLevel);
        }

        public static void DrawTextureTransparent(Rect position, Texture image) =>
            DrawTextureTransparent(position, image, ScaleMode.StretchToFill, 0);

        public static void DrawTextureTransparent(Rect position, Texture image, ScaleMode scaleMode) =>
            DrawTextureTransparent(position, image, scaleMode, 0);

        public static void DrawTextureTransparent(Rect position, Texture image, ScaleMode scaleMode, float imageAspect) =>
            DrawTextureTransparent(position, image, scaleMode, imageAspect, -1);

        public static void DrawTextureTransparent(Rect position, Texture image, ScaleMode scaleMode, float imageAspect, float mipLevel) =>
            DrawTextureTransparent(position, image, scaleMode, imageAspect, mipLevel, ColorWriteMask.All);

        public static void DrawTextureTransparent(Rect position, Texture image, ScaleMode scaleMode, float imageAspect, float mipLevel, ColorWriteMask colorWriteMask) =>
            DrawTextureTransparent(position, image, scaleMode, imageAspect, mipLevel, colorWriteMask, 0);

        public static void DrawTextureTransparent(Rect position, Texture image, ScaleMode scaleMode, float imageAspect, float mipLevel, ColorWriteMask colorWriteMask, float exposure)
        {
            if (!Compat.EditorPreviewsMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUI.DrawTextureTransparent"); return; }
            DrawTextureTransparentInternal(position, image, scaleMode, imageAspect, mipLevel, colorWriteMask, exposure);
        }

        internal static void DrawTextureAlphaInternal(Rect position, Texture image, ScaleMode scaleMode, float imageAspect, float mipLevel)
        {
            var mat = UseVTMaterial(image) ? alphaVTMaterial : alphaMaterial;
            DrawPreviewTextureInternal(position, image, mat, scaleMode, imageAspect, mipLevel, ColorWriteMask.All, 0);
        }

        /// <summary>The texture over the transparency checker, blended by its alpha.</summary>
        internal static void DrawTextureTransparentInternal(Rect position, Texture image, ScaleMode scaleMode, float imageAspect, float mipLevel, ColorWriteMask colorWriteMask, float exposure)
        {
            if (imageAspect == 0f && image == null)
            {
                Debug.LogError("Please specify an image or a imageAspect");
                return;
            }
            if (imageAspect == 0f)
                imageAspect = image.width / (float)image.height;
            DrawTransparencyCheckerTexture(position, scaleMode, imageAspect);
            if (image != null)
            {
                var mat = UseVTMaterial(image) ? transparentVTMaterial : transparentMaterial;
                DrawPreviewTexture(position, image, mat, scaleMode, imageAspect, mipLevel, colorWriteMask, exposure);
            }
        }

        internal static void DrawTransparencyCheckerTexture(Rect position, ScaleMode scaleMode, float imageAspect)
        {
            Rect screenRect = new Rect(), sourceRect = new Rect();
            GUI.CalculateScaledTextureRects(position, scaleMode, imageAspect, ref screenRect, ref sourceRect);
            var checker = transparentCheckerTexture;
            GUI.DrawTextureWithTexCoords(
                screenRect,
                checker,
                new Rect(
                    screenRect.width * -.5f / checker.width,
                    screenRect.height * -.5f / checker.height,
                    screenRect.width / checker.width,
                    screenRect.height / checker.height),
                false);
        }

        /// <summary>The texture drawn with a preview material (the caller's, else the one for the
        /// texture's kind), its colour mask, mip, exposure and signed-format flag set on it.</summary>
        internal static void DrawPreviewTextureInternal(Rect position, Texture image, Material mat, ScaleMode scaleMode, float imageAspect, float mipLevel, ColorWriteMask colorWriteMask, float exposure)
        {
            // Outside OnGUI there is no event: Unity's read of it throws (measured).
            RequireGUI();
            if (Event.current.type != EventType.Repaint) return;
            if (imageAspect == 0)
                imageAspect = image.width / (float)image.height;

            Color colorMask = new Color(1, 1, 1, 1);
            if ((colorWriteMask & ColorWriteMask.Red) == 0) colorMask.r = 0;
            if ((colorWriteMask & ColorWriteMask.Green) == 0) colorMask.g = 0;
            if ((colorWriteMask & ColorWriteMask.Blue) == 0) colorMask.b = 0;
            if ((colorWriteMask & ColorWriteMask.Alpha) == 0) colorMask.a = 0;

            if (mat == null)
                mat = GetMaterialForSpecialTexture(image, UseVTMaterial(image) ? colorVTMaterial : colorMaterial);

            mat.SetColor("_ColorMask", colorMask);
            mat.SetFloat("_Mip", mipLevel);
            mat.SetFloat("_Exposure", exposure);
            mat.SetInt("_SNormFormat", GraphicsFormatUtility.IsSNormFormat(image.graphicsFormat) ? 1 : 0);

            RenderTexture rt = image as RenderTexture;
            bool manualResolve = rt != null && rt.bindTextureMS;
            if (manualResolve)
            {
                var desc = rt.descriptor;
                desc.bindMS = false;
                desc.msaaSamples = 1;
                RenderTexture resolved = RenderTexture.GetTemporary(desc);
                resolved.Create();
                rt.ResolveAntiAliasedSurface(resolved);
                image = resolved;
            }

            Rect screenRect = new Rect(), sourceRect = new Rect();
            GUI.CalculateScaledTextureRects(position, scaleMode, imageAspect, ref screenRect, ref sourceRect);
            // Unity shows only the unpadded part of an always-padded texture; Unbound pads none.
            Graphics.DrawTexture(screenRect, image, sourceRect, 0, 0, 0, 0, GUI.color, mat);

            if (manualResolve)
                RenderTexture.ReleaseTemporary(image as RenderTexture);
        }

        /// <summary>Virtual texturing previews: Unbound has no virtual texturing (no texture is VT only).</summary>
        internal static bool UseVTMaterial(Texture texture) => texture is Texture2D tex2d && tex2d.vtOnly;

        /// <summary>The preview material for a texture's kind: encoded lightmaps, normal maps and
        /// alpha-only textures have their own, others the given one.</summary>
        internal static Material GetMaterialForSpecialTexture(Texture t, Material defaultMat = null, bool manualTex2Linear = false, bool useVTMaterialWhenPossible = true)
        {
            bool useVT = useVTMaterialWhenPossible && UseVTMaterial(t);
            if (t == null) return null;

            TextureUsageMode usage = TextureUtil.GetUsageMode(t);
            TextureFormat format = TextureUtil.GetTextureFormat(t);
            if (usage == TextureUsageMode.RealtimeLightmapRGBM || usage == TextureUsageMode.BakedLightmapRGBM || usage == TextureUsageMode.RGBMEncoded)
                return lightmapRGBMMaterial;
            if (usage == TextureUsageMode.BakedLightmapDoubleLDR)
                return lightmapDoubleLDRMaterial;
            if (usage == TextureUsageMode.BakedLightmapFullHDR)
                return lightmapFullHDRMaterial;
            if (TextureUtil.IsNormalMapUsageMode(usage))
            {
                var normalMat = useVT ? normalmapVTMaterial : normalmapMaterial;
                normalMat.SetFloat("_IsPlainNormalmap", usage == TextureUsageMode.NormalmapPlain && format != TextureFormat.BC5 ? 1.0f : 0.0f);
                normalMat.SetFloat("_ManualTex2Linear", manualTex2Linear ? 1.0f : 0.0f);
                return normalMat;
            }
            if (GraphicsFormatUtility.IsAlphaOnlyFormat(format))
            {
                var alphaOnlyMat = useVT ? alphaVTMaterial : alphaMaterial;
                alphaOnlyMat.SetFloat("_ManualTex2Linear", manualTex2Linear ? 1.0f : 0.0f);
                return alphaOnlyMat;
            }
            if (defaultMat != null)
                defaultMat.SetInt("_SNormFormat", GraphicsFormatUtility.IsSNormFormat(t.graphicsFormat) ? 1 : 0);
            return defaultMat;
        }

        /// <summary>The checker under transparent previews, the skin's.</summary>
        internal static Texture2D transparentCheckerTexture => EditorGUIUtility.isProSkin
            ? EditorGUIUtility.LoadRequired("Previews/Textures/textureCheckerDark.png") as Texture2D
            : EditorGUIUtility.LoadRequired("Previews/Textures/textureChecker.png") as Texture2D;

        internal static Material colorMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.Color);
        internal static Material colorVTMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.ColorVT);
        internal static Material alphaMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.Alpha);
        internal static Material alphaVTMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.AlphaVT);
        internal static Material transparentMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.Transparent);
        internal static Material transparentVTMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.TransparentVT);
        internal static Material normalmapMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.Normalmap);
        internal static Material normalmapVTMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.NormalmapVT);
        internal static Material lightmapRGBMMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.LightmapRGBM);
        internal static Material lightmapDoubleLDRMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.LightmapDoubleLDR);
        internal static Material lightmapFullHDRMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.LightmapFullHDR);
        internal static Material guiTextureClipVerticallyMaterial => EditorGUIUtility.GetPreviewMaterial(EditorGUIUtility.PreviewType.GUITextureClipVertically);
    }

    public sealed partial class EditorGUIUtility
    {
        internal enum PreviewType
        {
            Color = 0,
            ColorVT = 1,
            Alpha = 2,
            AlphaVT = 3,
            Transparent = 4,
            TransparentVT = 5,
            Normalmap = 6,
            NormalmapVT = 7,
            LightmapRGBM = 8,
            LightmapDoubleLDR = 9,
            LightmapFullHDR = 10,
            GUITextureClipVertically = 11,
        }

        /// <summary>The shaders of the preview materials, by <see cref="PreviewType"/> (the virtual
        /// texturing ones are null: not supported).</summary>
        static readonly string[] s_PreviewShaders =
        {
            "Hidden/Preview Color2D", null, "Hidden/Preview Alpha", null, "Hidden/Preview Transparent", null,
            "Hidden/Preview Encoded Normals", null, "Hidden/Preview Encoded Lightmap RGBM",
            "Hidden/Preview Encoded Lightmap doubleLDR", "Hidden/Preview Encoded Lightmap HDR",
            "Hidden/Internal-GUITextureClipVertically",
        };

        static readonly Material[] s_PreviewMaterials = new Material[s_PreviewShaders.Length];

        /// <summary>A preview material, made once from its Editor shader (drawn in IMGUI, so its clip
        /// texture is white: Unbound clips GUI draws by their rects).</summary>
        internal static Material GetPreviewMaterial(PreviewType type)
        {
            int index = (int)type;
            var material = s_PreviewMaterials[index];
            if (material != null) return material;
            string shaderName = s_PreviewShaders[index];
            if (shaderName == null)
                throw new NotSupportedException($"Unbound: the {type} preview material needs virtual texturing, which Unbound does not support.");
            var shader = Shader.Find(shaderName)
                ?? throw new InvalidOperationException($"Unbound: the Editor preview shader '{shaderName}' is not among this release's built-in shader sources.");
            using (ObjectRegistry.AsAssets())
                material = new Material(shader) { name = shaderName, hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_GUIClipTexture", Texture2D.whiteTexture);
            material.SetMatrix("unity_GUIClipTextureMatrix", Matrix4x4.zero);
            return s_PreviewMaterials[index] = material;
        }
    }

    internal enum TextureUsageMode
    {
        Default = 0,
        BakedLightmapDoubleLDR = 1,
        BakedLightmapRGBM = 2,
        NormalmapDXT5nm = 3,
        NormalmapPlain = 4,
        RGBMEncoded = 5,
        AlwaysPadded = 6,
        DoubleLDR = 7,
        BakedLightmapFullHDR = 8,
        RealtimeLightmapRGBM = 9,
        NormalmapASTCnm = 10,
        SingleChannelRed = 11,
        SingleChannelAlpha = 12,
    }

    internal static partial class TextureUtil
    {
        /// <summary>How a texture's shaders decode it: an imported 2D texture's or cubemap's usage
        /// mode (encoded lightmaps and HDR, normal maps), Default otherwise. A null texture throws
        /// as a [NotNull] binding argument does.</summary>
        public static TextureUsageMode GetUsageMode(Texture t) => t switch
        {
            null => throw new ArgumentNullException("t"),
            Texture2D t2d => (TextureUsageMode)t2d.m_UsageMode,
            Cubemap cube => (TextureUsageMode)cube.m_UsageMode,
            _ => TextureUsageMode.Default,
        };

        public static bool IsNormalMapUsageMode(TextureUsageMode usageMode) =>
            usageMode == TextureUsageMode.NormalmapDXT5nm || usageMode == TextureUsageMode.NormalmapPlain || usageMode == TextureUsageMode.NormalmapASTCnm;

        /// <summary>A texture's format: its own for the texture classes that have one, else the
        /// format of its graphics format.</summary>
        public static TextureFormat GetTextureFormat(Texture texture) => texture switch
        {
            null => throw new ArgumentNullException("texture"),
            Texture2D t => t.format,
            Cubemap c => c.format,
            Texture2DArray a => a.format,
            Texture3D v => v.format,
            CubemapArray ca => ca.format,
            _ => GraphicsFormatUtility.GetTextureFormat(texture.graphicsFormat),
        };
    }
}
