using System;
using System.Collections.Generic;
using Unity.Jobs.LowLevel.Unsafe;

namespace UnityEngine.TextCore.Text
{
    /// <summary>
    /// A piece of text a UI element shows, measured and generated through shared per-thread
    /// settings, generators and text infos. Generated text infos are kept in a temporary cache
    /// (reused after two frames unused) or, for text that is queried (fields), a permanent one.
    /// Unity's advanced text generator (native, ICU/HarfBuzz) is not available: handles always use
    /// the standard generator.
    /// </summary>
    internal partial class TextHandle
    {
        internal static TextHandleTemporaryCache s_TemporaryCache = new TextHandleTemporaryCache();
        internal static TextHandlePermanentCache s_PermanentCache = new TextHandlePermanentCache();
        static TextGenerationSettings[] s_Settings;
        static TextGenerator[] s_Generators;
        static TextInfo[] s_TextInfosCommon;

        Rect m_ScreenRect;
        float m_LineHeightDefault;
        bool m_IsPlaceholder;
        protected bool m_IsEllided;
        internal IntPtr textGenerationInfo = IntPtr.Zero;
        internal int m_PreviousGenerationSettingsHash;
        protected bool isDirty;

        public TextHandle() { }

        ~TextHandle()
        {
            try
            {
                RemoveTextInfoFromTemporaryCache();
                RemoveTextInfoFromPermanentCache();
            }
            catch (Exception)
            {
            }
        }

        internal static TextGenerationSettings[] settingsArray
        {
            get
            {
                if (s_Settings == null) InitArray(ref s_Settings, () => new TextGenerationSettings());
                return s_Settings;
            }
        }

        internal static TextGenerator[] generators
        {
            get
            {
                if (s_Generators == null) InitArray(ref s_Generators, () => new TextGenerator());
                return s_Generators;
            }
        }

        internal static TextInfo[] textInfosCommon
        {
            get
            {
                if (s_TextInfosCommon == null) InitArray(ref s_TextInfosCommon, () => new TextInfo());
                return s_TextInfosCommon;
            }
        }

        internal static TextInfo textInfoCommon => textInfosCommon[JobsUtility.ThreadIndex];

        static TextGenerator generator => generators[JobsUtility.ThreadIndex];

        /// <summary>The settings of the calling thread, which UI Toolkit fills before each call.</summary>
        internal static TextGenerationSettings settings => settingsArray[JobsUtility.ThreadIndex];

        internal Vector2 preferredSize { get; set; }

        internal LinkedListNode<TextInfo> TextInfoNode { get; set; }

        internal bool IsCachedPermanent { get; set; }

        internal bool IsCachedTemporary { get; set; }

        internal bool useAdvancedText => IsAdvancedTextEnabledForElement();

        internal int characterCount => textInfo.characterCount;

        /// <summary>The handle's cached text info, else the thread's shared one.</summary>
        internal TextInfo textInfo => TextInfoNode == null ? textInfoCommon : TextInfoNode.Value;

        public virtual bool IsPlaceholder => m_IsPlaceholder;

        internal static void InitThreadArrays()
        {
            if (s_Settings != null && s_Generators != null && s_TextInfosCommon != null) return;
            InitArray(ref s_Settings, () => new TextGenerationSettings());
            InitArray(ref s_Generators, () => new TextGenerator());
            InitArray(ref s_TextInfosCommon, () => new TextInfo());
        }

        static void InitArray<T>(ref T[] array, Func<T> create)
        {
            if (array != null) return;
            array = new T[JobsUtility.ThreadIndexCount];
            for (int i = 0; i < array.Length; i++) array[i] = create();
        }

        public virtual void AddTextInfoToPermanentCache() => s_PermanentCache.AddTextInfoToCache(this);

        public void AddTextInfoToTemporaryCache(int hashCode) => s_TemporaryCache.AddTextInfoToCache(this, hashCode);

        public void RemoveTextInfoFromTemporaryCache() => s_TemporaryCache.RemoveTextInfoFromCache(this);

        public void RemoveTextInfoFromPermanentCache() => s_PermanentCache.RemoveTextInfoFromCache(this);

        public static void UpdateCurrentFrame() => s_TemporaryCache.UpdateCurrentFrame();

        internal bool IsTextInfoAllocated() => textInfo != null;

        public void SetDirty() => isDirty = true;

        public bool IsDirty(int hashCode) =>
            m_PreviousGenerationSettingsHash != hashCode || isDirty || (!IsCachedTemporary && !IsCachedPermanent);

        public float ComputeTextWidth(TextGenerationSettings tgs)
        {
            UpdatePreferredValues(tgs);
            return preferredSize.x;
        }

        public float ComputeTextHeight(TextGenerationSettings tgs)
        {
            UpdatePreferredValues(tgs);
            return preferredSize.y;
        }

        /// <summary>Measures with the thread's generator and shared text info.</summary>
        protected void UpdatePreferredValues(TextGenerationSettings tgs) => preferredSize = generator.GetPreferredValues(tgs, textInfoCommon);

        internal TextInfo Update() => UpdateWithHash(settings.GetHashCode());

        /// <summary>Generates the text of the thread's settings into this handle's text info unless
        /// it already holds that text (same settings hash, not dirty, cached).</summary>
        internal TextInfo UpdateWithHash(int hashCode)
        {
            var current = settings;
            m_ScreenRect = current.screenRect;
            m_LineHeightDefault = GetLineHeightDefault(current);
            m_IsPlaceholder = current.isPlaceholder;
            if (!IsDirty(hashCode)) return textInfo;
            if (current.fontAsset == null || current.fontAsset.characterLookupTable == null)
            {
                Debug.LogWarning("Can't Generate Mesh, No Font Asset has been assigned.");
                return textInfo;
            }
            generator.GenerateText(current, textInfo);
            m_PreviousGenerationSettingsHash = hashCode;
            isDirty = false;
            m_IsEllided = generator.isTextTruncated;
            return textInfo;
        }

        /// <summary>Adds the text's glyphs to its font assets (on the main thread, before jobs).</summary>
        internal bool PrepareFontAsset()
        {
            var current = settings;
            if (current.fontAsset == null) return false;
            if (!IsDirty(current.GetHashCode())) return true;
            return generator.PrepareFontAsset(current);
        }

        /// <summary>IMGUI's preferred size from the generated text: widest line, first ascender to
        /// last descender, rounded up past the hundredth.</summary>
        internal void UpdatePreferredSize()
        {
            var info = textInfo;
            if (info.characterCount <= 0) return;
            float ascender = float.MinValue;
            float descender = info.textElementInfo[info.characterCount - 1].descender;
            float width = 0f;
            for (int i = 0; i < info.lineCount; i++)
            {
                var line = info.lineInfo[i];
                ascender = Mathf.Max(ascender, info.textElementInfo[line.firstVisibleCharacterIndex].ascender);
                descender = Mathf.Min(descender, info.textElementInfo[line.firstVisibleCharacterIndex].descender);
                width = settings.isIMGUI ? Mathf.Max(width, line.length) : Mathf.Max(width, line.lineExtents.max.x - line.lineExtents.min.x);
            }
            float height = ascender - descender;
            width = (float)(int)((double)width * 100.0 + 1.0) / 100f;
            height = (float)(int)((double)height * 100.0 + 1.0) / 100f;
            preferredSize = new Vector2(width, height);
        }

        /// <summary>Text units per pixel of a font asset's distance field at a font size.</summary>
        internal static float ConvertPixelUnitsToTextCoreRelativeUnits(float fontSize, FontAsset fontAsset)
        {
            float paddingRatio = 1f / (float)fontAsset.atlasPadding;
            float sizeRatio = fontAsset.faceInfo.pointSize / fontSize;
            return paddingRatio * sizeRatio;
        }

        internal static float GetLineHeightDefault(TextGenerationSettings settings) =>
            settings != null && settings.fontAsset != null ? (float)((double)settings.fontAsset.faceInfo.lineHeight / settings.fontAsset.faceInfo.pointSize * settings.fontSize) : 0f;

        public virtual Vector2 GetCursorPositionFromStringIndexUsingCharacterHeight(int index, bool inverseYAxis = true)
        {
            AddTextInfoToPermanentCache();
            return textInfo.GetCursorPositionFromStringIndexUsingCharacterHeight(index, m_ScreenRect, m_LineHeightDefault, inverseYAxis);
        }

        public Vector2 GetCursorPositionFromStringIndexUsingLineHeight(int index, bool useXAdvance = false, bool inverseYAxis = true)
        {
            AddTextInfoToPermanentCache();
            return textInfo.GetCursorPositionFromStringIndexUsingLineHeight(index, m_ScreenRect, m_LineHeightDefault, useXAdvance, inverseYAxis);
        }

        internal Rect[] GetHighlightRectangles(int cursorIndex, int selectIndex)
        {
            Debug.LogError("Cannot use GetHighlightRectangles while using Standard Text");
            return Array.Empty<Rect>();
        }

        public int GetCursorIndexFromPosition(Vector2 position, bool inverseYAxis = true) => textInfo.GetCursorIndexFromPosition(position, m_ScreenRect, inverseYAxis);

        public int LineDownCharacterPosition(int originalLogicalPos) => textInfo.LineDownCharacterPosition(originalLogicalPos);

        public int LineUpCharacterPosition(int originalLogicalPos) => textInfo.LineUpCharacterPosition(originalLogicalPos);

        public int FindWordIndex(int cursorIndex) => textInfo.FindWordIndex(cursorIndex);

        public int FindNearestLine(Vector2 position) => textInfo.FindNearestLine(position);

        public int FindNearestCharacterOnLine(Vector2 position, int line, bool visibleOnly) => textInfo.FindNearestCharacterOnLine(position, line, visibleOnly);

        public int FindIntersectingLink(Vector3 position, bool inverseYAxis = true) => textInfo.FindIntersectingLink(position, m_ScreenRect, inverseYAxis);

        public int GetCorrespondingStringIndex(int index) => textInfo.GetCorrespondingStringIndex(index);

        public int GetCorrespondingCodePointIndex(int stringIndex) => textInfo.GetCorrespondingCodePointIndex(stringIndex);

        public LineInfo GetLineInfoFromCharacterIndex(int index) => textInfo.GetLineInfoFromCharacterIndex(index);

        public int GetLineNumber(int index) => textInfo.GetLineNumber(index);

        public float GetLineHeight(int lineNumber) => textInfo.GetLineHeight(lineNumber);

        public float GetLineHeightFromCharacterIndex(int index) => textInfo.GetLineHeightFromCharacterIndex(index);

        public float GetCharacterHeightFromIndex(int index) => textInfo.GetCharacterHeightFromIndex(index);

        public string Substring(int startIndex, int length) => textInfo.Substring(startIndex, length);

        public int IndexOf(char value, int startIndex) => textInfo.IndexOf(value, startIndex);

        public int LastIndexOf(char value, int startIndex) => textInfo.LastIndexOf(value, startIndex);

        internal virtual bool IsAdvancedTextEnabledForElement() => false;
    }

    /// <summary>Text infos of handles that are generated every frame, reused once a handle has
    /// not been drawn for two frames.</summary>
    internal partial class TextHandleTemporaryCache
    {
        internal LinkedList<TextInfo> s_TextInfoPool = new LinkedList<TextInfo>();
        internal const int s_MinFramesInCache = 2;
        internal int currentFrame;
        readonly object m_SyncRoot = new object();

        public TextHandleTemporaryCache() { }

        public void ClearTemporaryCache()
        {
            for (int i = 0; i < s_TextInfoPool.Count; i++) s_TextInfoPool.First.Value.RemoveFromCache();
            s_TextInfoPool.Clear();
        }

        public void AddTextInfoToCache(TextHandle textHandle, int hashCode)
        {
            lock (m_SyncRoot)
            {
                if (textHandle.IsCachedPermanent) return;
                if (!TextGenerator.IsExecutingJob) currentFrame = Time.frameCount;
                if (s_TextInfoPool.Count > 0 && (currentFrame - s_TextInfoPool.Last.Value.lastTimeInCache < 0.0 || currentFrame - s_TextInfoPool.First.Value.lastTimeInCache < 0.0))
                    ClearTemporaryCache();
                if (textHandle.IsCachedTemporary)
                {
                    RefreshCaching(textHandle);
                    return;
                }
                if (s_TextInfoPool.Count > 0 && currentFrame - s_TextInfoPool.Last.Value.lastTimeInCache > s_MinFramesInCache)
                {
                    RecycleTextInfoFromCache(textHandle);
                }
                else
                {
                    var info = new TextInfo();
                    textHandle.TextInfoNode = new LinkedListNode<TextInfo>(info);
                    s_TextInfoPool.AddFirst(textHandle.TextInfoNode);
                    info.lastTimeInCache = currentFrame;
                    info.removedFromCache += textHandle.RemoveTextInfoFromTemporaryCache;
                }
            }
            textHandle.IsCachedTemporary = true;
            textHandle.SetDirty();
            textHandle.UpdateWithHash(hashCode);
        }

        public virtual void RemoveTextInfoFromCache(TextHandle textHandle)
        {
            lock (m_SyncRoot)
            {
                if (!textHandle.IsCachedTemporary) return;
                textHandle.IsCachedTemporary = false;
                var node = textHandle.TextInfoNode;
                if (node != null)
                {
                    node.Value.lastTimeInCache = 0.0;
                    node.Value.removedFromCache = null;
                    if (node.List == s_TextInfoPool) s_TextInfoPool.Remove(node);
                    s_TextInfoPool.AddLast(node);
                }
                textHandle.TextInfoNode = null;
            }
        }

        void RefreshCaching(TextHandle textHandle)
        {
            if (!TextGenerator.IsExecutingJob) currentFrame = Time.frameCount;
            textHandle.TextInfoNode.Value.lastTimeInCache = currentFrame;
            s_TextInfoPool.Remove(textHandle.TextInfoNode);
            s_TextInfoPool.AddFirst(textHandle.TextInfoNode);
        }

        void RecycleTextInfoFromCache(TextHandle textHandle)
        {
            if (!TextGenerator.IsExecutingJob) currentFrame = Time.frameCount;
            var node = s_TextInfoPool.Last;
            textHandle.TextInfoNode = node;
            node.Value.RemoveFromCache();
            s_TextInfoPool.RemoveLast();
            s_TextInfoPool.AddFirst(node);
            textHandle.IsCachedTemporary = true;
            node.Value.removedFromCache += textHandle.RemoveTextInfoFromTemporaryCache;
            node.Value.lastTimeInCache = currentFrame;
        }

        public void UpdateCurrentFrame() => currentFrame = Time.frameCount;
    }

    /// <summary>Text infos of handles whose text is queried (text fields): kept until released.</summary>
    internal partial class TextHandlePermanentCache
    {
        internal LinkedList<TextInfo> s_TextInfoPool = new LinkedList<TextInfo>();
        readonly object m_SyncRoot = new object();

        public TextHandlePermanentCache() { }

        public virtual void AddTextInfoToCache(TextHandle textHandle)
        {
            lock (m_SyncRoot)
            {
                if (textHandle.IsCachedPermanent) return;
                if (textHandle.IsCachedTemporary) textHandle.RemoveTextInfoFromTemporaryCache();
                if (s_TextInfoPool.Count > 0)
                {
                    textHandle.TextInfoNode = s_TextInfoPool.Last;
                    s_TextInfoPool.RemoveLast();
                }
                else
                {
                    textHandle.TextInfoNode = new LinkedListNode<TextInfo>(new TextInfo());
                }
            }
            textHandle.IsCachedPermanent = true;
            textHandle.SetDirty();
            textHandle.Update();
        }

        public void RemoveTextInfoFromCache(TextHandle textHandle)
        {
            lock (m_SyncRoot)
            {
                if (!textHandle.IsCachedPermanent) return;
                s_TextInfoPool.AddFirst(textHandle.TextInfoNode);
                textHandle.TextInfoNode = null;
                textHandle.IsCachedPermanent = false;
            }
        }
    }
}
