using System;
using System.Collections.Generic;
using UnityEngine.TextCore.Text;
using TextCoreSettings = UnityEngine.TextCore.Text.TextGenerationSettings;

namespace UnityEngine
{
    /// <summary>
    /// TextCore measurement handle for IMGUI styles: converts a <see cref="GUIStyle"/> into
    /// <see cref="TextGenerationSettings"/>, caches handles by settings hash, and reports preferred
    /// text size and line height.
    /// </summary>
    internal partial class IMGUITextHandle : TextHandle
    {
        const int MaxCachedHandles = 512;

        static readonly Dictionary<int, IMGUITextHandle> s_Handles = new Dictionary<int, IMGUITextHandle>();
        static readonly LinkedList<TextHandleTuple> s_Lru = new LinkedList<TextHandleTuple>();
        static TextSettings s_DefaultTextSettings;

        internal LinkedListNode<TextHandleTuple> tuple;
        internal static Func<Object> GetEditorTextSettings;
        internal static Func<int, FontAsset, FontAsset> GetBlurryFontAssetMapping;
        internal bool isCachedOnNative;

        internal partial class TextHandleTuple
        {
            public float lastTimeUsed;
            public int hashCode;

            public TextHandleTuple(float lastTimeUsed, int hashCode)
            {
                this.lastTimeUsed = lastTimeUsed;
                this.hashCode = hashCode;
            }
        }

        public IMGUITextHandle() { }

        internal static void EmptyCache() => EmptyManagedCache();

        internal static void EmptyManagedCache()
        {
            s_Handles.Clear();
            s_Lru.Clear();
        }

        static TextSettings ResolveTextSettings()
        {
            if (GetEditorTextSettings != null && GetEditorTextSettings() is TextSettings editorSettings && editorSettings != null)
                return editorSettings;
            if (s_DefaultTextSettings == null)
                s_DefaultTextSettings = ScriptableObject.CreateInstance<TextSettings>();
            return s_DefaultTextSettings;
        }

        internal static bool TryConvertStyleToSettings(TextCoreSettings tgs, GUIStyle style, Color textColor, string text, Rect rect)
        {
            var textSettings = ResolveTextSettings();
            tgs.textSettings = textSettings;
            if (textSettings == null) return false;

            var font = style.ResolvedFont;
            if (font == null) return false;

            tgs.fontSize = style.fontSize > 0 ? style.fontSize : (font.fontSize > 0 ? font.fontSize : 13f);
            tgs.fontStyle = TextGeneratorUtilities.LegacyStyleToNewStyle(style.fontStyle);
            tgs.fontAsset = textSettings.GetCachedFontAsset(font);
            if (tgs.fontAsset == null) return false;

            tgs.screenRect = new Rect(0f, 0f, rect.width, rect.height);
            tgs.text = text ?? "";

            var alignment = style.alignment;
            if (style.imagePosition == ImagePosition.ImageAbove)
            {
                alignment = alignment switch
                {
                    TextAnchor.MiddleLeft or TextAnchor.LowerLeft => TextAnchor.UpperLeft,
                    TextAnchor.MiddleCenter or TextAnchor.LowerCenter => TextAnchor.UpperCenter,
                    TextAnchor.MiddleRight or TextAnchor.LowerRight => TextAnchor.UpperRight,
                    _ => alignment,
                };
            }
            tgs.textAlignment = TextGeneratorUtilities.LegacyAlignmentToNewAlignment(alignment);
            tgs.overflowMode = style.clipping switch
            {
                TextClipping.Clip => TextOverflowMode.Masking,
                TextClipping.Ellipsis => TextOverflowMode.Ellipsis,
                _ => TextOverflowMode.Overflow,
            };
            tgs.textWrappingMode = rect.width > 0f && style.wordWrap
                ? TextWrappingMode.PreserveWhitespace
                : TextWrappingMode.PreserveWhitespaceNoWrap;
            tgs.richText = style.richText;
            tgs.parseControlCharacters = false;
            tgs.isPlaceholder = false;
            tgs.isRightToLeft = false;
            tgs.characterSpacing = 0f;
            tgs.wordSpacing = 0f;
            tgs.paragraphSpacing = 0f;
            tgs.color = textColor;
            tgs.isIMGUI = true;
            tgs.shouldConvertToLinearSpace = false;
            return true;
        }

        internal static IMGUITextHandle GetTextHandle(GUIStyle style, Rect position, string content, Color32 textColor)
        {
            bool isCached = false;
            return GetTextHandleInternal(style, position, content, textColor, isCalledFromNative: false, ref isCached);
        }

        internal static IMGUITextHandle GetTextHandle(GUIStyle style, Rect position, string content, Color32 textColor, ref bool isCached) =>
            GetTextHandleInternal(style, position, content, textColor, isCalledFromNative: true, ref isCached);

        static IMGUITextHandle GetTextHandleInternal(GUIStyle style, Rect position, string content, Color32 textColor, bool isCalledFromNative, ref bool isCached)
        {
            isCached = false;
            var tgs = TextHandle.settings;
            if (!TryConvertStyleToSettings(tgs, style, textColor, content, position))
                return null;
            int hash = tgs.GetHashCode();
            if (s_Handles.TryGetValue(hash, out var existing))
            {
                s_Lru.Remove(existing.tuple);
                s_Lru.AddLast(existing.tuple);
                isCached = !isCalledFromNative || existing.isCachedOnNative;
                if (isCalledFromNative && !existing.isCachedOnNative)
                {
                    existing.UpdateWithHash(hash);
                    existing.UpdatePreferredSize();
                    existing.isCachedOnNative = true;
                }
                return existing;
            }
            if (s_Handles.Count >= MaxCachedHandles && s_Lru.First != null)
            {
                var oldest = s_Lru.First.Value;
                s_Lru.RemoveFirst();
                s_Handles.Remove(oldest.hashCode);
            }
            var created = new IMGUITextHandle();
            var node = new LinkedListNode<TextHandleTuple>(new TextHandleTuple(Time.realtimeSinceStartup, hash));
            created.tuple = node;
            s_Handles[hash] = created;
            s_Lru.AddLast(node);
            created.UpdateWithHash(hash);
            created.UpdatePreferredSize();
            created.isCachedOnNative = isCalledFromNative;
            return created;
        }

        internal static float GetLineHeight(GUIStyle style)
        {
            var tgs = TextHandle.settings;
            if (!TryConvertStyleToSettings(tgs, style, Color.white, "", Rect.zero))
                return 0f;
            return TextHandle.GetLineHeightDefault(tgs);
        }

        internal Vector2 GetPreferredSize() => preferredSize;

        internal static bool TryMeasurePreferredSize(GUIStyle style, string content, Rect outerRect, out Vector2 size)
        {
            var inner = style.padding.Remove(outerRect);
            var handle = GetTextHandle(style, inner, content, Color.white);
            if (handle == null)
            {
                size = Vector2.zero;
                return false;
            }
            size = handle.GetPreferredSize();
            return true;
        }
    }
}
