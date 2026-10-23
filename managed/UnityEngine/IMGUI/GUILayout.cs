using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A sizing option of a GUILayout element (width, height, expansion).</summary>
    public sealed partial class GUILayoutOption
    {
        internal enum Type
        {
            fixedWidth,
            fixedHeight,
            minWidth,
            maxWidth,
            minHeight,
            maxHeight,
            stretchWidth,
            stretchHeight,
            alignStart,
            alignMiddle,
            alignEnd,
            alignJustify,
            equalSize,
            spacing,
        }

        internal Type type;
        internal object value;

        internal GUILayoutOption(Type type, object value)
        {
            this.type = type;
            this.value = value;
        }
    }

    /// <summary>
    /// An element of an automatic layout: its size range (without margins), whether it takes extra
    /// space, and the rect the layout gives it.
    /// </summary>
    internal partial class GUILayoutEntry
    {
        public float minWidth, maxWidth, minHeight, maxHeight;
        public Rect rect = new Rect(0, 0, 0, 0);
        public int stretchWidth, stretchHeight;
        public bool consideredForMargin = true;
        internal bool userMaxWidth, userMaxHeight;
        GUIStyle m_Style = GUIStyle.none;

        internal static Rect kDummyRect = new Rect(0, 0, 1, 1);

        public GUILayoutEntry(float _minWidth, float _maxWidth, float _minHeight, float _maxHeight, GUIStyle _style)
        {
            minWidth = _minWidth;
            maxWidth = _maxWidth;
            minHeight = _minHeight;
            maxHeight = _maxHeight;
            style = _style ?? GUIStyle.none;
        }

        public GUILayoutEntry(float _minWidth, float _maxWidth, float _minHeight, float _maxHeight, GUIStyle _style, GUILayoutOption[] options)
            : this(_minWidth, _maxWidth, _minHeight, _maxHeight, _style)
        {
            ApplyOptions(options);
        }

        public GUIStyle style
        {
            get => m_Style;
            set
            {
                m_Style = value ?? GUIStyle.none;
                ApplyStyleSettings(m_Style);
            }
        }

        public virtual int marginLeft => m_Style.margin.left;
        public virtual int marginRight => m_Style.margin.right;
        public virtual int marginTop => m_Style.margin.top;
        public virtual int marginBottom => m_Style.margin.bottom;
        public int marginHorizontal => marginLeft + marginRight;
        public int marginVertical => marginTop + marginBottom;

        public virtual void CalcWidth() { }

        public virtual void CalcHeight() { }

        public virtual void SetHorizontal(float x, float width)
        {
            rect.x = x;
            rect.width = width;
        }

        public virtual void SetVertical(float y, float height)
        {
            rect.y = y;
            rect.height = height;
        }

        protected virtual void ApplyStyleSettings(GUIStyle style)
        {
            stretchWidth = style.fixedWidth == 0f && style.stretchWidth ? 1 : 0;
            stretchHeight = style.fixedHeight == 0f && style.stretchHeight ? 1 : 0;
        }

        public virtual void ApplyOptions(GUILayoutOption[] options)
        {
            if (options == null) return;
            foreach (var option in options)
            {
                switch (option.type)
                {
                    case GUILayoutOption.Type.fixedWidth:
                        minWidth = maxWidth = (float)option.value;
                        stretchWidth = 0;
                        break;
                    case GUILayoutOption.Type.fixedHeight:
                        minHeight = maxHeight = (float)option.value;
                        stretchHeight = 0;
                        break;
                    case GUILayoutOption.Type.minWidth:
                        minWidth = (float)option.value;
                        if (maxWidth < minWidth) maxWidth = minWidth;
                        break;
                    case GUILayoutOption.Type.maxWidth:
                        maxWidth = (float)option.value;
                        if (minWidth > maxWidth) minWidth = maxWidth;
                        userMaxWidth = true;
                        break;
                    case GUILayoutOption.Type.minHeight:
                        minHeight = (float)option.value;
                        if (maxHeight < minHeight) maxHeight = minHeight;
                        break;
                    case GUILayoutOption.Type.maxHeight:
                        maxHeight = (float)option.value;
                        if (minHeight > maxHeight) minHeight = maxHeight;
                        userMaxHeight = true;
                        break;
                    case GUILayoutOption.Type.stretchWidth:
                        stretchWidth = (int)option.value;
                        break;
                    case GUILayoutOption.Type.stretchHeight:
                        stretchHeight = (int)option.value;
                        break;
                }
            }
            if (maxWidth != 0f && maxWidth < minWidth) maxWidth = minWidth;
            if (maxHeight != 0f && maxHeight < minHeight) maxHeight = minHeight;
        }

        public override string ToString() =>
            $"{GetType().Name} {rect} - W: {minWidth}-{maxWidth} {(stretchWidth != 0 ? "+" : "")}, H: {minHeight}-{maxHeight} {(stretchHeight != 0 ? "+" : "")}";
    }

    /// <summary>
    /// A word-wrapping element: its height depends on the width the layout gives it.
    /// </summary>
    internal sealed partial class GUIWordWrapSizer : GUILayoutEntry
    {
        readonly GUIContent m_Content;
        readonly float m_ForcedMinHeight, m_ForcedMaxHeight;

        public GUIWordWrapSizer(GUIStyle style, GUIContent content, GUILayoutOption[] options) : base(0f, 0f, 0f, 0f, style)
        {
            m_Content = new GUIContent(content);
            ApplyOptions(options);
            m_ForcedMinHeight = minHeight;
            m_ForcedMaxHeight = maxHeight;
        }

        public override void CalcWidth()
        {
            if (minWidth == 0f || maxWidth == 0f)
            {
                style.CalcMinMaxWidth(m_Content, out float min, out float max);
                min = Mathf.Ceil(min);
                max = Mathf.Ceil(max);
                if (minWidth == 0f) minWidth = min;
                if (maxWidth == 0f) maxWidth = max;
            }
        }

        public override void CalcHeight()
        {
            if (m_ForcedMinHeight == 0f || m_ForcedMaxHeight == 0f)
            {
                float h = style.CalcHeight(m_Content, rect.width);
                minHeight = m_ForcedMinHeight == 0f ? h : m_ForcedMinHeight;
                maxHeight = m_ForcedMaxHeight == 0f ? h : m_ForcedMaxHeight;
            }
        }
    }

    /// <summary>An element keeping an aspect ratio: its height follows its width.</summary>
    internal sealed partial class GUIAspectSizer : GUILayoutEntry
    {
        readonly float m_Aspect;

        public GUIAspectSizer(float aspect, GUILayoutOption[] options) : base(0f, 0f, 0f, 0f, GUIStyle.none)
        {
            m_Aspect = aspect;
            ApplyOptions(options);
        }

        public override void CalcHeight() => minHeight = maxHeight = rect.width / m_Aspect;
    }

    /// <summary>
    /// A row or column of entries. Neighbouring margins collapse to the larger; a group without a
    /// style passes its children's outer margins on as its own, a styled one pads them. Extra space
    /// goes first towards each entry's preferred size, then to the stretching entries.
    /// </summary>
    internal partial class GUILayoutGroup : GUILayoutEntry
    {
        public List<GUILayoutEntry> entries = new List<GUILayoutEntry>();
        public bool isVertical = true;
        public bool resetCoords;
        public float spacing;
        public bool sameSize = true;
        public bool isWindow;
        public int windowID = -1;
        int m_Cursor;
        protected int m_StretchableCountX = 100;
        protected int m_StretchableCountY = 100;
        protected bool m_UserSpecifiedWidth;
        protected bool m_UserSpecifiedHeight;
        protected float m_ChildMinWidth = 100f;
        protected float m_ChildMaxWidth = 100f;
        protected float m_ChildMinHeight = 100f;
        protected float m_ChildMaxHeight = 100f;
        protected int m_MarginLeft;
        protected int m_MarginRight;
        protected int m_MarginTop;
        protected int m_MarginBottom;

        public GUILayoutGroup() : base(0f, 0f, 0f, 0f, GUIStyle.none) { }

        public GUILayoutGroup(GUIStyle _style, GUILayoutOption[] options) : base(0f, 0f, 0f, 0f, _style)
        {
            if (options != null) ApplyOptions(options);
            m_MarginLeft = _style.margin.left;
            m_MarginRight = _style.margin.right;
            m_MarginTop = _style.margin.top;
            m_MarginBottom = _style.margin.bottom;
        }

        public override int marginLeft => m_MarginLeft;
        public override int marginRight => m_MarginRight;
        public override int marginTop => m_MarginTop;
        public override int marginBottom => m_MarginBottom;

        /// <summary>Whether the group's own style pads it (else its children's margins become its own).</summary>
        bool Styled => style != GUIStyle.none || isWindow;

        public override void ApplyOptions(GUILayoutOption[] options)
        {
            if (options == null) return;
            base.ApplyOptions(options);
            foreach (var option in options)
            {
                switch (option.type)
                {
                    case GUILayoutOption.Type.fixedWidth:
                    case GUILayoutOption.Type.minWidth:
                    case GUILayoutOption.Type.maxWidth:
                        m_UserSpecifiedWidth = true;
                        break;
                    case GUILayoutOption.Type.fixedHeight:
                    case GUILayoutOption.Type.minHeight:
                    case GUILayoutOption.Type.maxHeight:
                        m_UserSpecifiedHeight = true;
                        break;
                    case GUILayoutOption.Type.spacing:
                        spacing = (int)option.value;
                        break;
                }
            }
        }

        protected override void ApplyStyleSettings(GUIStyle style)
        {
            base.ApplyStyleSettings(style);
            var margin = style.margin;
            m_MarginLeft = margin.left;
            m_MarginRight = margin.right;
            m_MarginTop = margin.top;
            m_MarginBottom = margin.bottom;
        }

        public void ResetCursor() => m_Cursor = 0;

        public Rect PeekNext()
        {
            if (m_Cursor < entries.Count) return entries[m_Cursor].rect;
            throw new ArgumentException($"Getting control {m_Cursor}'s position in a group with only {entries.Count} controls when doing {Event.current?.rawType}\nAborting");
        }

        public GUILayoutEntry GetNext()
        {
            if (m_Cursor < entries.Count) return entries[m_Cursor++];
            throw new ArgumentException($"Getting control {m_Cursor}'s position in a group with only {entries.Count} controls when doing {Event.current?.rawType}\nAborting");
        }

        public Rect GetLast()
        {
            if (m_Cursor == 0)
            {
                Debug.LogError("You cannot call GetLast immediately after beginning a group.");
                return kDummyRect;
            }
            if (m_Cursor <= entries.Count) return entries[m_Cursor - 1].rect;
            Debug.LogError($"Getting control {m_Cursor}'s position in a group with only {entries.Count} controls when doing {Event.current?.type}");
            return kDummyRect;
        }

        public void Add(GUILayoutEntry e) => entries.Add(e);

        /// <summary>Margins along the flow: between entries the larger of the two facing ones;
        /// entries that do not count for margins (spaces) take none.</summary>
        static float Gap(ref int last, bool first, int next, float spacing) => first ? 0f : Math.Max(Math.Max(last, next), spacing);

        public override void CalcWidth()
        {
            if (entries.Count == 0)
            {
                maxWidth = minWidth = style.padding.horizontal;
                return;
            }
            float childMin = 0f, childMax = 0f;
            int stretch = 0;
            int leftMargin = int.MaxValue, rightMargin = int.MaxValue;
            if (isVertical)
            {
                foreach (var e in entries)
                {
                    e.CalcWidth();
                    int l = e.consideredForMargin ? e.marginLeft : 0, r = e.consideredForMargin ? e.marginRight : 0;
                    leftMargin = Math.Min(leftMargin, l);
                    rightMargin = Math.Min(rightMargin, r);
                    stretch += e.stretchWidth;
                }
                foreach (var e in entries)
                {
                    int l = e.consideredForMargin ? e.marginLeft : 0, r = e.consideredForMargin ? e.marginRight : 0;
                    float extra = Styled
                        ? Math.Max(l, style.padding.left) + Math.Max(r, style.padding.right)
                        : l - leftMargin + r - rightMargin;
                    childMin = Math.Max(childMin, e.minWidth + extra);
                    childMax = Math.Max(childMax, e.maxWidth + extra);
                }
            }
            else
            {
                int last = 0;
                bool first = true;
                GUILayoutEntry firstEntry = null, lastEntry = null;
                foreach (var e in entries)
                {
                    e.CalcWidth();
                    if (e.consideredForMargin)
                    {
                        float gap = Gap(ref last, first, e.marginLeft, spacing);
                        childMin += gap;
                        childMax += gap;
                        last = e.marginRight;
                        if (firstEntry == null) firstEntry = e;
                        lastEntry = e;
                        first = false;
                    }
                    else if (!first)
                    {
                        childMin += spacing;
                        childMax += spacing;
                    }
                    childMin += e.minWidth;
                    childMax += e.maxWidth;
                    stretch += e.stretchWidth;
                }
                leftMargin = firstEntry?.marginLeft ?? 0;
                rightMargin = lastEntry?.marginRight ?? 0;
                if (Styled)
                {
                    childMin += Math.Max(leftMargin, style.padding.left) + Math.Max(rightMargin, style.padding.right);
                    childMax += Math.Max(leftMargin, style.padding.left) + Math.Max(rightMargin, style.padding.right);
                }
            }
            if (leftMargin == int.MaxValue) leftMargin = 0;
            if (rightMargin == int.MaxValue) rightMargin = 0;
            if (!Styled)
            {
                m_MarginLeft = leftMargin;
                m_MarginRight = rightMargin;
            }
            else
            {
                m_MarginLeft = style.margin.left;
                m_MarginRight = style.margin.right;
            }
            m_ChildMinWidth = childMin;
            m_ChildMaxWidth = childMax;
            m_StretchableCountX = stretch;
            if (style.fixedWidth != 0f)
            {
                minWidth = maxWidth = style.fixedWidth;
                return;
            }
            if (!m_UserSpecifiedWidth || minWidth == 0f && maxWidth == 0f)
            {
                minWidth = childMin;
                maxWidth = Math.Max(childMax, childMin);
            }
            else
            {
                if (minWidth == 0f) minWidth = childMin;
                if (maxWidth == 0f) maxWidth = Math.Max(childMax, minWidth);
            }
        }

        public override void SetHorizontal(float x, float width)
        {
            base.SetHorizontal(x, width);
            if (resetCoords) x = 0f;
            if (isVertical)
            {
                foreach (var e in entries)
                {
                    int l = e.consideredForMargin ? e.marginLeft : 0, r = e.consideredForMargin ? e.marginRight : 0;
                    float left = Styled ? Math.Max(l, style.padding.left) : l - m_MarginLeft;
                    float right = Styled ? Math.Max(r, style.padding.right) : r - m_MarginRight;
                    float available = width - left - right;
                    float w = e.stretchWidth != 0 ? available : Math.Min(Math.Max(e.maxWidth, e.minWidth), available);
                    if (e.stretchWidth != 0 && e.userMaxWidth) w = Math.Min(w, e.maxWidth);
                    w = Math.Max(w, e.minWidth);
                    e.SetHorizontal(x + left, w);
                }
                return;
            }
            float padLeft = 0f, padRight = 0f;
            if (Styled)
            {
                var firstMargin = FirstConsidered()?.marginLeft ?? 0;
                var lastMargin = LastConsidered()?.marginRight ?? 0;
                padLeft = Math.Max(firstMargin, style.padding.left);
                padRight = Math.Max(lastMargin, style.padding.right);
            }
            float inner = width - padLeft - padRight;
            float minTotal = m_ChildMinWidth - (Styled ? padLeft + padRight : 0f);
            float maxTotal = m_ChildMaxWidth - (Styled ? padLeft + padRight : 0f);
            float extra = inner - minTotal;
            float towardsMax = 0f, toStretch = 0f;
            if (extra > 0f)
            {
                towardsMax = Math.Min(extra, Math.Max(0f, maxTotal - minTotal));
                toStretch = extra - towardsMax;
                if (m_StretchableCountX == 0) toStretch = 0f;
            }
            float growRange = maxTotal - minTotal;
            float pos = x + padLeft;
            int last = 0;
            bool first = true;
            foreach (var e in entries)
            {
                if (e.consideredForMargin)
                {
                    pos += Gap(ref last, first, e.marginLeft, spacing);
                    last = e.marginRight;
                    first = false;
                }
                else if (!first)
                {
                    pos += spacing;
                }
                float w = e.minWidth;
                if (growRange > 0f) w += (e.maxWidth - e.minWidth) / growRange * towardsMax;
                if (e.stretchWidth != 0 && m_StretchableCountX > 0) w += toStretch * e.stretchWidth / m_StretchableCountX;
                if (e.userMaxWidth) w = Math.Min(w, Math.Max(e.maxWidth, e.minWidth));
                e.SetHorizontal(pos, w);
                pos += w;
            }
        }

        GUILayoutEntry FirstConsidered()
        {
            foreach (var e in entries) if (e.consideredForMargin) return e;
            return null;
        }

        GUILayoutEntry LastConsidered()
        {
            for (int i = entries.Count - 1; i >= 0; i--) if (entries[i].consideredForMargin) return entries[i];
            return null;
        }

        public override void CalcHeight()
        {
            if (entries.Count == 0)
            {
                maxHeight = minHeight = style.padding.vertical;
                return;
            }
            float childMin = 0f, childMax = 0f;
            int stretch = 0;
            int topMargin = int.MaxValue, bottomMargin = int.MaxValue;
            if (!isVertical)
            {
                foreach (var e in entries)
                {
                    e.CalcHeight();
                    int t = e.consideredForMargin ? e.marginTop : 0, b = e.consideredForMargin ? e.marginBottom : 0;
                    topMargin = Math.Min(topMargin, t);
                    bottomMargin = Math.Min(bottomMargin, b);
                    stretch += e.stretchHeight;
                }
                foreach (var e in entries)
                {
                    int t = e.consideredForMargin ? e.marginTop : 0, b = e.consideredForMargin ? e.marginBottom : 0;
                    float extra = Styled
                        ? Math.Max(t, style.padding.top) + Math.Max(b, style.padding.bottom)
                        : t - topMargin + b - bottomMargin;
                    childMin = Math.Max(childMin, e.minHeight + extra);
                    childMax = Math.Max(childMax, e.maxHeight + extra);
                }
            }
            else
            {
                int last = 0;
                bool first = true;
                GUILayoutEntry firstEntry = null, lastEntry = null;
                foreach (var e in entries)
                {
                    e.CalcHeight();
                    if (e.consideredForMargin)
                    {
                        float gap = Gap(ref last, first, e.marginTop, spacing);
                        childMin += gap;
                        childMax += gap;
                        last = e.marginBottom;
                        if (firstEntry == null) firstEntry = e;
                        lastEntry = e;
                        first = false;
                    }
                    else if (!first)
                    {
                        childMin += spacing;
                        childMax += spacing;
                    }
                    childMin += e.minHeight;
                    childMax += e.maxHeight;
                    stretch += e.stretchHeight;
                }
                topMargin = firstEntry?.marginTop ?? 0;
                bottomMargin = lastEntry?.marginBottom ?? 0;
                if (Styled)
                {
                    childMin += Math.Max(topMargin, style.padding.top) + Math.Max(bottomMargin, style.padding.bottom);
                    childMax += Math.Max(topMargin, style.padding.top) + Math.Max(bottomMargin, style.padding.bottom);
                }
            }
            if (topMargin == int.MaxValue) topMargin = 0;
            if (bottomMargin == int.MaxValue) bottomMargin = 0;
            if (!Styled)
            {
                m_MarginTop = topMargin;
                m_MarginBottom = bottomMargin;
            }
            else
            {
                m_MarginTop = style.margin.top;
                m_MarginBottom = style.margin.bottom;
            }
            m_ChildMinHeight = childMin;
            m_ChildMaxHeight = childMax;
            m_StretchableCountY = stretch;
            if (style.fixedHeight != 0f)
            {
                minHeight = maxHeight = style.fixedHeight;
                return;
            }
            if (!m_UserSpecifiedHeight || minHeight == 0f && maxHeight == 0f)
            {
                minHeight = childMin;
                maxHeight = Math.Max(childMax, childMin);
            }
            else
            {
                if (minHeight == 0f) minHeight = childMin;
                if (maxHeight == 0f) maxHeight = Math.Max(childMax, minHeight);
            }
        }

        public override void SetVertical(float y, float height)
        {
            base.SetVertical(y, height);
            if (resetCoords) y = 0f;
            if (!isVertical)
            {
                foreach (var e in entries)
                {
                    int t = e.consideredForMargin ? e.marginTop : 0, b = e.consideredForMargin ? e.marginBottom : 0;
                    float top = Styled ? Math.Max(t, style.padding.top) : t - m_MarginTop;
                    float bottom = Styled ? Math.Max(b, style.padding.bottom) : b - m_MarginBottom;
                    float available = height - top - bottom;
                    float h = e.stretchHeight != 0 ? available : Math.Min(Math.Max(e.maxHeight, e.minHeight), available);
                    if (e.stretchHeight != 0 && e.userMaxHeight) h = Math.Min(h, e.maxHeight);
                    h = Math.Max(h, e.minHeight);
                    e.SetVertical(y + top, h);
                }
                return;
            }
            float padTop = 0f, padBottom = 0f;
            if (Styled)
            {
                var firstMargin = FirstConsidered()?.marginTop ?? 0;
                var lastMargin = LastConsidered()?.marginBottom ?? 0;
                padTop = Math.Max(firstMargin, style.padding.top);
                padBottom = Math.Max(lastMargin, style.padding.bottom);
            }
            float inner = height - padTop - padBottom;
            float minTotal = m_ChildMinHeight - (Styled ? padTop + padBottom : 0f);
            float maxTotal = m_ChildMaxHeight - (Styled ? padTop + padBottom : 0f);
            float extra = inner - minTotal;
            float towardsMax = 0f, toStretch = 0f;
            if (extra > 0f)
            {
                towardsMax = Math.Min(extra, Math.Max(0f, maxTotal - minTotal));
                toStretch = extra - towardsMax;
                if (m_StretchableCountY == 0) toStretch = 0f;
            }
            float growRange = maxTotal - minTotal;
            float pos = y + padTop;
            int last = 0;
            bool first = true;
            foreach (var e in entries)
            {
                if (e.consideredForMargin)
                {
                    pos += Gap(ref last, first, e.marginTop, spacing);
                    last = e.marginBottom;
                    first = false;
                }
                else if (!first)
                {
                    pos += spacing;
                }
                float h = e.minHeight;
                if (growRange > 0f) h += (e.maxHeight - e.minHeight) / growRange * towardsMax;
                if (e.stretchHeight != 0 && m_StretchableCountY > 0) h += toStretch * e.stretchHeight / m_StretchableCountY;
                if (e.userMaxHeight) h = Math.Min(h, Math.Max(e.maxHeight, e.minHeight));
                e.SetVertical(pos, h);
                pos += h;
            }
        }

        public override string ToString() => $"{base.ToString()} {(isVertical ? "vertical" : "horizontal")} ({entries.Count} entries)";
    }

    /// <summary>
    /// A scroll view's group: it may be smaller than its content, which is laid out at its full size
    /// in the client area (less the scrollbars it needs).
    /// </summary>
    internal sealed partial class GUIScrollGroup : GUILayoutGroup
    {
        public float calcMinWidth, calcMaxWidth, calcMinHeight, calcMaxHeight;
        public float clientWidth, clientHeight;
        public bool allowHorizontalScroll = true;
        public bool allowVerticalScroll = true;
        public bool needsHorizontalScrollbar;
        public bool needsVerticalScrollbar;
        public GUIStyle horizontalScrollbar;
        public GUIStyle verticalScrollbar;

        float VerticalBarWidth => verticalScrollbar != null ? verticalScrollbar.fixedWidth + verticalScrollbar.margin.left : 0f;
        float HorizontalBarHeight => horizontalScrollbar != null ? horizontalScrollbar.fixedHeight + horizontalScrollbar.margin.top : 0f;

        public override void CalcWidth()
        {
            float min = minWidth, max = maxWidth;
            if (allowHorizontalScroll)
            {
                minWidth = 0f;
                maxWidth = 0f;
            }
            base.CalcWidth();
            calcMinWidth = minWidth;
            calcMaxWidth = maxWidth;
            if (allowHorizontalScroll)
            {
                if (minWidth > 32f) minWidth = 32f;
                if (min != 0f) minWidth = min;
                if (max != 0f)
                {
                    maxWidth = max;
                    m_StretchableCountX = 0;
                }
            }
        }

        public override void SetHorizontal(float x, float width)
        {
            float client = needsVerticalScrollbar ? width - VerticalBarWidth : width;
            if (allowHorizontalScroll && client < calcMinWidth)
            {
                needsHorizontalScrollbar = true;
                minWidth = calcMinWidth;
                maxWidth = calcMaxWidth;
                base.SetHorizontal(x, calcMinWidth);
                rect.width = width;
                clientWidth = calcMinWidth;
            }
            else
            {
                needsHorizontalScrollbar = false;
                if (allowHorizontalScroll)
                {
                    minWidth = calcMinWidth;
                    maxWidth = calcMaxWidth;
                }
                base.SetHorizontal(x, client);
                rect.width = width;
                clientWidth = client;
            }
        }

        public override void CalcHeight()
        {
            float min = minHeight, max = maxHeight;
            if (allowVerticalScroll)
            {
                minHeight = 0f;
                maxHeight = 0f;
            }
            base.CalcHeight();
            calcMinHeight = minHeight;
            calcMaxHeight = maxHeight;
            if (needsHorizontalScrollbar)
            {
                minHeight += HorizontalBarHeight;
                maxHeight += HorizontalBarHeight;
            }
            if (allowVerticalScroll)
            {
                if (minHeight > 32f) minHeight = 32f;
                if (min != 0f) minHeight = min;
                if (max != 0f)
                {
                    maxHeight = max;
                    m_StretchableCountY = 0;
                }
            }
        }

        public override void SetVertical(float y, float height)
        {
            float client = height;
            if (needsHorizontalScrollbar) client -= HorizontalBarHeight;
            if (allowVerticalScroll && client < calcMinHeight)
            {
                if (!needsVerticalScrollbar)
                {
                    // The vertical scrollbar takes width: lay the content out again beside it.
                    needsVerticalScrollbar = true;
                    SetHorizontal(rect.x, rect.width);
                    CalcHeight();
                    client = height - (needsHorizontalScrollbar ? HorizontalBarHeight : 0f);
                }
                float oldMin = minHeight, oldMax = maxHeight;
                minHeight = calcMinHeight;
                maxHeight = calcMaxHeight;
                base.SetVertical(y, calcMinHeight);
                minHeight = oldMin;
                maxHeight = oldMax;
                rect.height = height;
                clientHeight = calcMinHeight;
            }
            else
            {
                if (allowVerticalScroll)
                {
                    minHeight = calcMinHeight;
                    maxHeight = calcMaxHeight;
                }
                base.SetVertical(y, client);
                rect.height = height;
                clientHeight = client;
            }
        }
    }

    /// <summary>
    /// Automatic layout's state: per OnGUI owner (and per window) the group tree built in the
    /// Layout event, which the other events walk in the same order to get their rects.
    /// </summary>
    public partial class GUILayoutUtility
    {
        internal sealed partial class LayoutCache
        {
            public GUILayoutGroup topLevel = new GUILayoutGroup();
            /// <summary>The open groups, innermost on top.</summary>
            internal Stack<GUILayoutGroup> groups = new Stack<GUILayoutGroup>();
            /// <summary>Free groups laid out on their own rects (areas).</summary>
            internal GUILayoutGroup windows = new GUILayoutGroup();

            internal LayoutCache()
            {
                groups.Push(topLevel);
            }
        }

        static readonly Dictionary<int, LayoutCache> s_StoredLayouts = new Dictionary<int, LayoutCache>();
        static readonly Dictionary<int, LayoutCache> s_StoredWindows = new Dictionary<int, LayoutCache>();
        static readonly Stack<LayoutCache> s_SavedCaches = new Stack<LayoutCache>();
        static int s_Owner;
        internal static LayoutCache current = new LayoutCache();
        internal static readonly Rect kDummyRect = new Rect(0, 0, 1, 1);
        static GUIStyle s_SpaceStyle;

        internal static GUIStyle spaceStyle => s_SpaceStyle ??= new GUIStyle { stretchWidth = false };

        static bool IsLayout => Event.current != null && Event.current.type == EventType.Layout;

        static LayoutCache Cache(Dictionary<int, LayoutCache> store, int id)
        {
            if (!store.TryGetValue(id, out var cache)) store[id] = cache = new LayoutCache();
            return cache;
        }

        /// <summary>Starts an OnGUI call's layout: built anew in Layout, walked again otherwise.</summary>
        internal static void Begin(int instanceID)
        {
            s_Owner = instanceID;
            s_SavedCaches.Clear();
            current = Cache(s_StoredLayouts, instanceID);
            if (IsLayout)
            {
                current.topLevel = new GUILayoutGroup();
                current.groups.Clear();
                current.groups.Push(current.topLevel);
                current.windows = new GUILayoutGroup();
            }
            else
            {
                current.topLevel.ResetCursor();
                current.groups.Clear();
                current.groups.Push(current.topLevel);
                current.windows.ResetCursor();
            }
        }

        internal static void End()
        {
            s_SavedCaches.Clear();
        }

        internal static bool HasWindow(int windowID) => s_StoredWindows.ContainsKey(WindowKey(windowID));

        static int WindowKey(int windowID) => unchecked(s_Owner * 31 + windowID);

        /// <summary>Starts a window's layout inside an OnGUI call.</summary>
        internal static void BeginWindow(int windowID, GUIStyle style, GUILayoutOption[] options, Rect rect, bool isLayoutWindow)
        {
            s_SavedCaches.Push(current);
            current = Cache(s_StoredWindows, WindowKey(windowID));
            if (IsLayout)
            {
                current.topLevel = style != null ? new GUILayoutGroup(style, null) : new GUILayoutGroup();
                current.topLevel.isWindow = true;
                current.topLevel.windowID = windowID;
                current.topLevel.resetCoords = true;
                if (options != null) current.topLevel.ApplyOptions(options);
                current.groups.Clear();
                current.groups.Push(current.topLevel);
                current.windows = new GUILayoutGroup();
            }
            else
            {
                current.topLevel.ResetCursor();
                current.groups.Clear();
                current.groups.Push(current.topLevel);
                current.windows.ResetCursor();
            }
        }

        /// <summary>Ends a window's layout; after Layout its content is laid out in the window
        /// (a GUILayout window takes the size its content needs).</summary>
        internal static void EndWindow(GUI.GuiWindow window, Event e)
        {
            if (s_SavedCaches.Count == 0) return;
            if (e.type == EventType.Layout)
            {
                var top = current.topLevel;
                top.CalcWidth();
                float width = window.Layout ? Math.Max(window.Rect.width, top.minWidth) : window.Rect.width;
                if (window.Layout && top.maxWidth > 0f && HasFixedWidth(window.Options)) width = top.maxWidth;
                top.SetHorizontal(0f, width);
                top.CalcHeight();
                float height = window.Layout ? Math.Max(window.Rect.height, top.minHeight) : window.Rect.height;
                if (window.Layout && top.maxHeight > 0f && HasFixedHeight(window.Options)) height = top.maxHeight;
                top.SetVertical(0f, height);
                LayoutFreeGroup(current.windows);
                if (window.Layout) window.Rect = new Rect(window.Rect.x, window.Rect.y, width, height);
            }
            current = s_SavedCaches.Pop();
        }

        static bool HasFixedWidth(GUILayoutOption[] options)
        {
            if (options == null) return false;
            foreach (var o in options) if (o.type == GUILayoutOption.Type.fixedWidth || o.type == GUILayoutOption.Type.maxWidth) return true;
            return false;
        }

        static bool HasFixedHeight(GUILayoutOption[] options)
        {
            if (options == null) return false;
            foreach (var o in options) if (o.type == GUILayoutOption.Type.fixedHeight || o.type == GUILayoutOption.Type.maxHeight) return true;
            return false;
        }

        /// <summary>Lays the Layout event's tree out over the screen, and areas over their rects.</summary>
        internal static void Layout()
        {
            var top = current.topLevel;
            top.CalcWidth();
            top.SetHorizontal(0f, Screen.width);
            top.CalcHeight();
            top.SetVertical(0f, Screen.height);
            LayoutFreeGroup(current.windows);
        }

        internal static void LayoutFreeGroup(GUILayoutGroup toplevel)
        {
            foreach (var entry in toplevel.entries)
                if (entry is GUILayoutGroup group) LayoutSingleGroup(group);
            toplevel.ResetCursor();
        }

        /// <summary>An area keeps the size it was given: its content's size clamped to it.</summary>
        static void LayoutSingleGroup(GUILayoutGroup group)
        {
            float minW = group.minWidth, maxW = group.maxWidth;
            group.CalcWidth();
            group.SetHorizontal(group.rect.x, Mathf.Clamp(group.maxWidth, minW, maxW));
            float minH = group.minHeight, maxH = group.maxHeight;
            group.CalcHeight();
            group.SetVertical(group.rect.y, Mathf.Clamp(group.maxHeight, minH, maxH));
        }

        /// <summary>Opens a nested group: a new one in Layout, the next one otherwise.</summary>
        internal static GUILayoutGroup BeginLayoutGroup(GUIStyle style, GUILayoutOption[] options, Type layoutType)
        {
            GUILayoutGroup g;
            if (IsLayout)
            {
                g = CreateGroup(layoutType);
                g.style = style;
                if (options != null) g.ApplyOptions(options);
                current.groups.Peek().Add(g);
            }
            else
            {
                g = current.groups.Peek().GetNext() as GUILayoutGroup;
                if (g == null)
                    throw new ArgumentException("GUILayout: Mismatched LayoutGroup." + Event.current?.type);
                g.ResetCursor();
            }
            current.groups.Push(g);
            return g;
        }

        static GUILayoutGroup CreateGroup(Type layoutType) =>
            layoutType == typeof(GUIScrollGroup) ? new GUIScrollGroup() : layoutType != null && layoutType != typeof(GUILayoutGroup)
                ? (GUILayoutGroup)Activator.CreateInstance(layoutType)
                : new GUILayoutGroup();

        internal static void EndLayoutGroup()
        {
            if (current.groups.Count <= 1)
            {
                Debug.LogError("EndLayoutGroup: BeginLayoutGroup must be called first.");
                return;
            }
            current.groups.Pop();
        }

        /// <summary>Opens an area: a group laid out on its own rect.</summary>
        internal static GUILayoutGroup BeginLayoutArea(GUIStyle style, Type layoutType)
        {
            GUILayoutGroup g;
            if (IsLayout)
            {
                g = CreateGroup(layoutType);
                g.style = style;
                current.windows.Add(g);
            }
            else
            {
                g = current.windows.GetNext() as GUILayoutGroup;
                if (g == null)
                    throw new ArgumentException("GUILayout: Mismatched LayoutGroup." + Event.current?.type);
                g.ResetCursor();
            }
            current.groups.Push(g);
            return g;
        }

        public static void BeginGroup(string GroupName) { }

        public static void EndGroup(string groupName) { }

        public static Rect GetRect(GUIContent content, GUIStyle style) => DoGetRect(content, style, null);

        public static Rect GetRect(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => DoGetRect(content, style, options);

        /// <summary>An entry sized for the content; a wrapping style's height follows its width.</summary>
        static Rect DoGetRect(GUIContent content, GUIStyle style, GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI();
            if (IsLayout)
            {
                if (style.isHeightDependantOnWidth)
                {
                    current.groups.Peek().Add(new GUIWordWrapSizer(style, content, options));
                }
                else
                {
                    var size = style.CalcSize(content);
                    current.groups.Peek().Add(new GUILayoutEntry(size.x, size.x, size.y, size.y, style, options));
                }
                return kDummyRect;
            }
            return current.groups.Peek().GetNext().rect;
        }

        public static Rect GetRect(float width, float height) => DoGetRect(width, width, height, height, GUIStyle.none, null);
        public static Rect GetRect(float width, float height, GUIStyle style) => DoGetRect(width, width, height, height, style, null);
        public static Rect GetRect(float width, float height, params GUILayoutOption[] options) => DoGetRect(width, width, height, height, GUIStyle.none, options);
        public static Rect GetRect(float width, float height, GUIStyle style, params GUILayoutOption[] options) => DoGetRect(width, width, height, height, style, options);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight) => DoGetRect(minWidth, maxWidth, minHeight, maxHeight, GUIStyle.none, null);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight, GUIStyle style) => DoGetRect(minWidth, maxWidth, minHeight, maxHeight, style, null);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight, params GUILayoutOption[] options) => DoGetRect(minWidth, maxWidth, minHeight, maxHeight, GUIStyle.none, options);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight, GUIStyle style, params GUILayoutOption[] options) => DoGetRect(minWidth, maxWidth, minHeight, maxHeight, style, options);

        static Rect DoGetRect(float minWidth, float maxWidth, float minHeight, float maxHeight, GUIStyle style, GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI();
            if (IsLayout)
            {
                current.groups.Peek().Add(new GUILayoutEntry(minWidth, maxWidth, minHeight, maxHeight, style, options));
                return kDummyRect;
            }
            return current.groups.Peek().GetNext().rect;
        }

        public static Rect GetLastRect()
        {
            GUIUtility.CheckOnGUI();
            return IsLayout ? kDummyRect : current.groups.Peek().GetLast();
        }

        public static Rect GetAspectRect(float aspect) => DoGetAspectRect(aspect, null);
        public static Rect GetAspectRect(float aspect, GUIStyle style) => DoGetAspectRect(aspect, null);
        public static Rect GetAspectRect(float aspect, params GUILayoutOption[] options) => DoGetAspectRect(aspect, options);
        public static Rect GetAspectRect(float aspect, GUIStyle style, params GUILayoutOption[] options) => DoGetAspectRect(aspect, options);

        static Rect DoGetAspectRect(float aspect, GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI();
            if (IsLayout)
            {
                current.groups.Peek().Add(new GUIAspectSizer(aspect, options));
                return kDummyRect;
            }
            return current.groups.Peek().GetNext().rect;
        }

        /// <summary>The group the next GUILayout element goes in.</summary>
        internal static GUILayoutGroup topLevel => current.topLevel;
    }

    /// <summary>UnityGUI controls placed by automatic layout.</summary>
    public partial class GUILayout
    {
        public static void Label(Texture image, params GUILayoutOption[] options) => DoLabel(GUIContent.Temp(image), GUI.skin.label, options);
        public static void Label(string text, params GUILayoutOption[] options) => DoLabel(GUIContent.Temp(text), GUI.skin.label, options);
        public static void Label(GUIContent content, params GUILayoutOption[] options) => DoLabel(content, GUI.skin.label, options);
        public static void Label(Texture image, GUIStyle style, params GUILayoutOption[] options) => DoLabel(GUIContent.Temp(image), style, options);
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) => DoLabel(GUIContent.Temp(text), style, options);
        public static void Label(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => DoLabel(content, style, options);

        static void DoLabel(GUIContent content, GUIStyle style, GUILayoutOption[] options) =>
            GUI.Label(GUILayoutUtility.GetRect(content, style, options), content, style);

        public static void Box(Texture image, params GUILayoutOption[] options) => DoBox(GUIContent.Temp(image), GUI.skin.box, options);
        public static void Box(string text, params GUILayoutOption[] options) => DoBox(GUIContent.Temp(text), GUI.skin.box, options);
        public static void Box(GUIContent content, params GUILayoutOption[] options) => DoBox(content, GUI.skin.box, options);
        public static void Box(Texture image, GUIStyle style, params GUILayoutOption[] options) => DoBox(GUIContent.Temp(image), style, options);
        public static void Box(string text, GUIStyle style, params GUILayoutOption[] options) => DoBox(GUIContent.Temp(text), style, options);
        public static void Box(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => DoBox(content, style, options);

        static void DoBox(GUIContent content, GUIStyle style, GUILayoutOption[] options) =>
            GUI.Box(GUILayoutUtility.GetRect(content, style, options), content, style);

        public static bool Button(Texture image, params GUILayoutOption[] options) => DoButton(GUIContent.Temp(image), GUI.skin.button, options);
        public static bool Button(string text, params GUILayoutOption[] options) => DoButton(GUIContent.Temp(text), GUI.skin.button, options);
        public static bool Button(GUIContent content, params GUILayoutOption[] options) => DoButton(content, GUI.skin.button, options);
        public static bool Button(Texture image, GUIStyle style, params GUILayoutOption[] options) => DoButton(GUIContent.Temp(image), style, options);
        public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) => DoButton(GUIContent.Temp(text), style, options);
        public static bool Button(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => DoButton(content, style, options);

        static bool DoButton(GUIContent content, GUIStyle style, GUILayoutOption[] options) =>
            GUI.Button(GUILayoutUtility.GetRect(content, style, options), content, style);

        public static bool RepeatButton(Texture image, params GUILayoutOption[] options) => DoRepeatButton(GUIContent.Temp(image), GUI.skin.button, options);
        public static bool RepeatButton(string text, params GUILayoutOption[] options) => DoRepeatButton(GUIContent.Temp(text), GUI.skin.button, options);
        public static bool RepeatButton(GUIContent content, params GUILayoutOption[] options) => DoRepeatButton(content, GUI.skin.button, options);
        public static bool RepeatButton(Texture image, GUIStyle style, params GUILayoutOption[] options) => DoRepeatButton(GUIContent.Temp(image), style, options);
        public static bool RepeatButton(string text, GUIStyle style, params GUILayoutOption[] options) => DoRepeatButton(GUIContent.Temp(text), style, options);
        public static bool RepeatButton(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => DoRepeatButton(content, style, options);

        static bool DoRepeatButton(GUIContent content, GUIStyle style, GUILayoutOption[] options) =>
            GUI.RepeatButton(GUILayoutUtility.GetRect(content, style, options), content, style);

        public static string TextField(string text, params GUILayoutOption[] options) => DoTextField(text, -1, false, GUI.skin.textField, options);
        public static string TextField(string text, int maxLength, params GUILayoutOption[] options) => DoTextField(text, maxLength, false, GUI.skin.textField, options);
        public static string TextField(string text, GUIStyle style, params GUILayoutOption[] options) => DoTextField(text, -1, false, style, options);
        public static string TextField(string text, int maxLength, GUIStyle style, params GUILayoutOption[] options) => DoTextField(text, maxLength, false, style, options);

        public static string PasswordField(string password, char maskChar, params GUILayoutOption[] options) => PasswordField(password, maskChar, -1, GUI.skin.textField, options);
        public static string PasswordField(string password, char maskChar, int maxLength, params GUILayoutOption[] options) => PasswordField(password, maskChar, maxLength, GUI.skin.textField, options);
        public static string PasswordField(string password, char maskChar, GUIStyle style, params GUILayoutOption[] options) => PasswordField(password, maskChar, -1, style, options);

        public static string PasswordField(string password, char maskChar, int maxLength, GUIStyle style, params GUILayoutOption[] options)
        {
            var shown = GUIContent.Temp(new string(maskChar, password?.Length ?? 0));
            return GUI.PasswordField(GUILayoutUtility.GetRect(shown, style, options), password, maskChar, maxLength, style);
        }

        public static string TextArea(string text, params GUILayoutOption[] options) => DoTextField(text, -1, true, GUI.skin.textArea, options);
        public static string TextArea(string text, int maxLength, params GUILayoutOption[] options) => DoTextField(text, maxLength, true, GUI.skin.textArea, options);
        public static string TextArea(string text, GUIStyle style, params GUILayoutOption[] options) => DoTextField(text, -1, true, style, options);
        public static string TextArea(string text, int maxLength, GUIStyle style, params GUILayoutOption[] options) => DoTextField(text, maxLength, true, style, options);

        static string DoTextField(string text, int maxLength, bool multiline, GUIStyle style, GUILayoutOption[] options)
        {
            var content = GUIContent.Temp(text);
            var rect = GUILayoutUtility.GetRect(content, style, options);
            return multiline ? GUI.TextArea(rect, text, maxLength, style) : GUI.TextField(rect, text, maxLength, style);
        }

        public static bool Toggle(bool value, Texture image, params GUILayoutOption[] options) => DoToggle(value, GUIContent.Temp(image), GUI.skin.toggle, options);
        public static bool Toggle(bool value, string text, params GUILayoutOption[] options) => DoToggle(value, GUIContent.Temp(text), GUI.skin.toggle, options);
        public static bool Toggle(bool value, GUIContent content, params GUILayoutOption[] options) => DoToggle(value, content, GUI.skin.toggle, options);
        public static bool Toggle(bool value, Texture image, GUIStyle style, params GUILayoutOption[] options) => DoToggle(value, GUIContent.Temp(image), style, options);
        public static bool Toggle(bool value, string text, GUIStyle style, params GUILayoutOption[] options) => DoToggle(value, GUIContent.Temp(text), style, options);
        public static bool Toggle(bool value, GUIContent content, GUIStyle style, params GUILayoutOption[] options) => DoToggle(value, content, style, options);

        static bool DoToggle(bool value, GUIContent content, GUIStyle style, GUILayoutOption[] options) =>
            GUI.Toggle(GUILayoutUtility.GetRect(content, style, options), value, content, style);

        public static int Toolbar(int selected, string[] texts, params GUILayoutOption[] options) => Toolbar(selected, GUIContent.Temp(texts), GUI.skin.button, options);
        public static int Toolbar(int selected, Texture[] images, params GUILayoutOption[] options) => Toolbar(selected, GUIContent.Temp(images), GUI.skin.button, options);
        public static int Toolbar(int selected, GUIContent[] contents, params GUILayoutOption[] options) => Toolbar(selected, contents, GUI.skin.button, options);
        public static int Toolbar(int selected, string[] texts, GUIStyle style, params GUILayoutOption[] options) => Toolbar(selected, GUIContent.Temp(texts), style, options);
        public static int Toolbar(int selected, Texture[] images, GUIStyle style, params GUILayoutOption[] options) => Toolbar(selected, GUIContent.Temp(images), style, options);
        public static int Toolbar(int selected, string[] texts, GUIStyle style, GUI.ToolbarButtonSize buttonSize, params GUILayoutOption[] options) => Toolbar(selected, GUIContent.Temp(texts), style, buttonSize, options);
        public static int Toolbar(int selected, Texture[] images, GUIStyle style, GUI.ToolbarButtonSize buttonSize, params GUILayoutOption[] options) => Toolbar(selected, GUIContent.Temp(images), style, buttonSize, options);
        public static int Toolbar(int selected, GUIContent[] contents, GUIStyle style, params GUILayoutOption[] options) => Toolbar(selected, contents, style, GUI.ToolbarButtonSize.Fixed, options);

        public static int Toolbar(int selected, GUIContent[] contents, GUIStyle style, GUI.ToolbarButtonSize buttonSize, params GUILayoutOption[] options)
        {
            var rect = GridRect(contents, contents?.Length ?? 0, style, options, buttonSize);
            return GUI.Toolbar(rect, selected, contents, style, buttonSize);
        }

        public static int SelectionGrid(int selected, string[] texts, int xCount, params GUILayoutOption[] options) => SelectionGrid(selected, GUIContent.Temp(texts), xCount, GUI.skin.button, options);
        public static int SelectionGrid(int selected, Texture[] images, int xCount, params GUILayoutOption[] options) => SelectionGrid(selected, GUIContent.Temp(images), xCount, GUI.skin.button, options);
        public static int SelectionGrid(int selected, GUIContent[] content, int xCount, params GUILayoutOption[] options) => SelectionGrid(selected, content, xCount, GUI.skin.button, options);
        public static int SelectionGrid(int selected, string[] texts, int xCount, GUIStyle style, params GUILayoutOption[] options) => SelectionGrid(selected, GUIContent.Temp(texts), xCount, style, options);
        public static int SelectionGrid(int selected, Texture[] images, int xCount, GUIStyle style, params GUILayoutOption[] options) => SelectionGrid(selected, GUIContent.Temp(images), xCount, style, options);

        public static int SelectionGrid(int selected, GUIContent[] contents, int xCount, GUIStyle style, params GUILayoutOption[] options)
        {
            var rect = GridRect(contents, Math.Max(1, xCount), style, options, GUI.ToolbarButtonSize.Fixed);
            return GUI.SelectionGrid(rect, selected, contents, xCount, style);
        }

        /// <summary>The rect of a button grid: cells as large as the largest content.</summary>
        static Rect GridRect(GUIContent[] contents, int xCount, GUIStyle style, GUILayoutOption[] options, GUI.ToolbarButtonSize buttonSize)
        {
            int count = contents?.Length ?? 0;
            if (count == 0) return GUILayoutUtility.GetRect(0f, 0f, style, options);
            xCount = Math.Max(1, Math.Min(xCount, count));
            int rows = (count + xCount - 1) / xCount;
            float cellW = 0f, cellH = 0f, total = 0f;
            foreach (var c in contents)
            {
                var size = style.CalcSize(c);
                cellW = Math.Max(cellW, size.x);
                cellH = Math.Max(cellH, size.y);
                total += size.x;
            }
            float spacingX = Math.Max(style.margin.left, style.margin.right) * (xCount - 1);
            float spacingY = Math.Max(style.margin.top, style.margin.bottom) * (rows - 1);
            float width = buttonSize == GUI.ToolbarButtonSize.FitToContents && rows == 1 ? total + spacingX : cellW * xCount + spacingX;
            float height = cellH * rows + spacingY;
            return GUILayoutUtility.GetRect(width, width, height, height, style, options);
        }

        public static float HorizontalSlider(float value, float leftValue, float rightValue, params GUILayoutOption[] options) =>
            HorizontalSlider(value, leftValue, rightValue, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, options);

        public static float HorizontalSlider(float value, float leftValue, float rightValue, GUIStyle slider, GUIStyle thumb, params GUILayoutOption[] options) =>
            GUI.HorizontalSlider(GUILayoutUtility.GetRect(GUIContent.Temp("mmmm"), slider, options), value, leftValue, rightValue, slider, thumb);

        public static float VerticalSlider(float value, float leftValue, float rightValue, params GUILayoutOption[] options) =>
            VerticalSlider(value, leftValue, rightValue, GUI.skin.verticalSlider, GUI.skin.verticalSliderThumb, options);

        public static float VerticalSlider(float value, float leftValue, float rightValue, GUIStyle slider, GUIStyle thumb, params GUILayoutOption[] options) =>
            GUI.VerticalSlider(GUILayoutUtility.GetRect(GUIContent.Temp("\n\n\n\n\n"), slider, options), value, leftValue, rightValue, slider, thumb);

        public static float HorizontalScrollbar(float value, float size, float leftValue, float rightValue, params GUILayoutOption[] options) =>
            HorizontalScrollbar(value, size, leftValue, rightValue, GUI.skin.horizontalScrollbar, options);

        public static float HorizontalScrollbar(float value, float size, float leftValue, float rightValue, GUIStyle style, params GUILayoutOption[] options) =>
            GUI.HorizontalScrollbar(GUILayoutUtility.GetRect(GUIContent.Temp("mmmm"), style, options), value, size, leftValue, rightValue, style);

        public static float VerticalScrollbar(float value, float size, float topValue, float bottomValue, params GUILayoutOption[] options) =>
            VerticalScrollbar(value, size, topValue, bottomValue, GUI.skin.verticalScrollbar, options);

        public static float VerticalScrollbar(float value, float size, float topValue, float bottomValue, GUIStyle style, params GUILayoutOption[] options) =>
            GUI.VerticalScrollbar(GUILayoutUtility.GetRect(GUIContent.Temp("\n\n\n\n"), style, options), value, size, topValue, bottomValue, style);

        /// <summary>Fixed space along the current group; it takes no margins.</summary>
        public static void Space(float pixels)
        {
            GUIUtility.CheckOnGUI();
            if (GUILayoutUtility.current.groups.Peek().isVertical)
                GUILayoutUtility.GetRect(0f, pixels, GUILayoutUtility.spaceStyle, Height(pixels));
            else
                GUILayoutUtility.GetRect(pixels, 0f, GUILayoutUtility.spaceStyle, Width(pixels));
            if (Event.current.type == EventType.Layout)
            {
                var entries = GUILayoutUtility.current.groups.Peek().entries;
                entries[entries.Count - 1].consideredForMargin = false;
            }
        }

        /// <summary>Space that takes whatever the group has left.</summary>
        public static void FlexibleSpace()
        {
            GUIUtility.CheckOnGUI();
            var option = GUILayoutUtility.current.groups.Peek().isVertical ? ExpandHeight(true) : ExpandWidth(true);
            option.value = 10000;
            GUILayoutUtility.GetRect(0f, 0f, GUILayoutUtility.spaceStyle, option);
            if (Event.current.type == EventType.Layout)
            {
                var entries = GUILayoutUtility.current.groups.Peek().entries;
                entries[entries.Count - 1].consideredForMargin = false;
            }
        }

        public static void BeginHorizontal(params GUILayoutOption[] options) => BeginHorizontal(GUIContent.none, GUIStyle.none, options);
        public static void BeginHorizontal(GUIStyle style, params GUILayoutOption[] options) => BeginHorizontal(GUIContent.none, style, options);
        public static void BeginHorizontal(string text, GUIStyle style, params GUILayoutOption[] options) => BeginHorizontal(GUIContent.Temp(text), style, options);
        public static void BeginHorizontal(Texture image, GUIStyle style, params GUILayoutOption[] options) => BeginHorizontal(GUIContent.Temp(image), style, options);

        public static void BeginHorizontal(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            var g = GUILayoutUtility.BeginLayoutGroup(style, options, typeof(GUILayoutGroup));
            g.isVertical = false;
            if (style != GUIStyle.none || content != GUIContent.none)
                GUI.Box(g.rect, content, style);
        }

        public static void EndHorizontal() => GUILayoutUtility.EndLayoutGroup();

        public static void BeginVertical(params GUILayoutOption[] options) => BeginVertical(GUIContent.none, GUIStyle.none, options);
        public static void BeginVertical(GUIStyle style, params GUILayoutOption[] options) => BeginVertical(GUIContent.none, style, options);
        public static void BeginVertical(string text, GUIStyle style, params GUILayoutOption[] options) => BeginVertical(GUIContent.Temp(text), style, options);
        public static void BeginVertical(Texture image, GUIStyle style, params GUILayoutOption[] options) => BeginVertical(GUIContent.Temp(image), style, options);

        public static void BeginVertical(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            var g = GUILayoutUtility.BeginLayoutGroup(style, options, typeof(GUILayoutGroup));
            g.isVertical = true;
            if (style != GUIStyle.none || content != GUIContent.none)
                GUI.Box(g.rect, content, style);
        }

        public static void EndVertical() => GUILayoutUtility.EndLayoutGroup();

        public static void BeginArea(Rect screenRect) => BeginArea(screenRect, GUIContent.none, GUIStyle.none);
        public static void BeginArea(Rect screenRect, string text) => BeginArea(screenRect, GUIContent.Temp(text), GUIStyle.none);
        public static void BeginArea(Rect screenRect, Texture image) => BeginArea(screenRect, GUIContent.Temp(image), GUIStyle.none);
        public static void BeginArea(Rect screenRect, GUIContent content) => BeginArea(screenRect, content, GUIStyle.none);
        public static void BeginArea(Rect screenRect, GUIStyle style) => BeginArea(screenRect, GUIContent.none, style);
        public static void BeginArea(Rect screenRect, string text, GUIStyle style) => BeginArea(screenRect, GUIContent.Temp(text), style);
        public static void BeginArea(Rect screenRect, Texture image, GUIStyle style) => BeginArea(screenRect, GUIContent.Temp(image), style);

        /// <summary>A fixed screen rect with its own layout inside (and its own origin).</summary>
        public static void BeginArea(Rect screenRect, GUIContent content, GUIStyle style)
        {
            GUIUtility.CheckOnGUI();
            var g = GUILayoutUtility.BeginLayoutArea(style, typeof(GUILayoutGroup));
            if (Event.current.type == EventType.Layout)
            {
                g.resetCoords = true;
                g.minWidth = g.maxWidth = screenRect.width;
                g.minHeight = g.maxHeight = screenRect.height;
                g.rect = Rect.MinMaxRect(screenRect.xMin, screenRect.yMin, g.rect.xMax, g.rect.yMax);
            }
            GUI.BeginGroup(g.rect, content, style);
        }

        public static void EndArea()
        {
            GUIUtility.CheckOnGUI();
            GUILayoutUtility.current.groups.Pop();
            GUI.EndGroup();
        }

        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) =>
            BeginScrollView(scrollPosition, false, false, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, GUI.skin.scrollView, options);

        public static Vector2 BeginScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options) =>
            BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, GUI.skin.scrollView, options);

        public static Vector2 BeginScrollView(Vector2 scrollPosition, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options) =>
            BeginScrollView(scrollPosition, false, false, horizontalScrollbar, verticalScrollbar, GUI.skin.scrollView, options);

        public static Vector2 BeginScrollView(Vector2 scrollPosition, GUIStyle style)
        {
            var name = style.name;
            var v = GUI.skin.FindStyle(name + "VerticalScrollbar") ?? GUI.skin.verticalScrollbar;
            var h = GUI.skin.FindStyle(name + "HorizontalScrollbar") ?? GUI.skin.horizontalScrollbar;
            return BeginScrollView(scrollPosition, false, false, h, v, style);
        }

        public static Vector2 BeginScrollView(Vector2 scrollPosition, GUIStyle style, params GUILayoutOption[] options)
        {
            var name = style.name;
            var v = GUI.skin.FindStyle(name + "VerticalScrollbar") ?? GUI.skin.verticalScrollbar;
            var h = GUI.skin.FindStyle(name + "HorizontalScrollbar") ?? GUI.skin.horizontalScrollbar;
            return BeginScrollView(scrollPosition, false, false, h, v, style, options);
        }

        public static Vector2 BeginScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options) =>
            BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, horizontalScrollbar, verticalScrollbar, GUI.skin.scrollView, options);

        public static Vector2 BeginScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, GUIStyle background, params GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI();
            var g = (GUIScrollGroup)GUILayoutUtility.BeginLayoutGroup(background, null, typeof(GUIScrollGroup));
            if (Event.current.type == EventType.Layout)
            {
                g.resetCoords = true;
                g.isVertical = true;
                g.stretchWidth = 1;
                g.stretchHeight = 1;
                g.verticalScrollbar = verticalScrollbar;
                g.horizontalScrollbar = horizontalScrollbar;
                g.needsVerticalScrollbar = alwaysShowVertical;
                g.needsHorizontalScrollbar = alwaysShowHorizontal;
                g.ApplyOptions(options);
            }
            return GUI.BeginScrollView(g.rect, scrollPosition, new Rect(0, 0, g.clientWidth, g.clientHeight), alwaysShowHorizontal, alwaysShowVertical,
                horizontalScrollbar, verticalScrollbar, background);
        }

        public static void EndScrollView() => EndScrollView(true);

        internal static void EndScrollView(bool handleScrollWheel)
        {
            GUILayoutUtility.EndLayoutGroup();
            GUI.EndScrollView(handleScrollWheel);
        }

        public static Rect Window(int id, Rect screenRect, GUI.WindowFunction func, string text, params GUILayoutOption[] options) =>
            DoWindow(id, screenRect, func, GUIContent.Temp(text), GUI.skin.window, options);

        public static Rect Window(int id, Rect screenRect, GUI.WindowFunction func, Texture image, params GUILayoutOption[] options) =>
            DoWindow(id, screenRect, func, GUIContent.Temp(image), GUI.skin.window, options);

        public static Rect Window(int id, Rect screenRect, GUI.WindowFunction func, GUIContent content, params GUILayoutOption[] options) =>
            DoWindow(id, screenRect, func, content, GUI.skin.window, options);

        public static Rect Window(int id, Rect screenRect, GUI.WindowFunction func, string text, GUIStyle style, params GUILayoutOption[] options) =>
            DoWindow(id, screenRect, func, GUIContent.Temp(text), style, options);

        public static Rect Window(int id, Rect screenRect, GUI.WindowFunction func, Texture image, GUIStyle style, params GUILayoutOption[] options) =>
            DoWindow(id, screenRect, func, GUIContent.Temp(image), style, options);

        public static Rect Window(int id, Rect screenRect, GUI.WindowFunction func, GUIContent content, GUIStyle style, params GUILayoutOption[] options) =>
            DoWindow(id, screenRect, func, content, style, options);

        static Rect DoWindow(int id, Rect screenRect, GUI.WindowFunction func, GUIContent content, GUIStyle style, GUILayoutOption[] options) =>
            GUI.DoWindow(id, screenRect, func, content, style, GUI.skin, false, true, options);

        public static GUILayoutOption Width(float width) => new GUILayoutOption(GUILayoutOption.Type.fixedWidth, width);
        public static GUILayoutOption MinWidth(float minWidth) => new GUILayoutOption(GUILayoutOption.Type.minWidth, minWidth);
        public static GUILayoutOption MaxWidth(float maxWidth) => new GUILayoutOption(GUILayoutOption.Type.maxWidth, maxWidth);
        public static GUILayoutOption Height(float height) => new GUILayoutOption(GUILayoutOption.Type.fixedHeight, height);
        public static GUILayoutOption MinHeight(float minHeight) => new GUILayoutOption(GUILayoutOption.Type.minHeight, minHeight);
        public static GUILayoutOption MaxHeight(float maxHeight) => new GUILayoutOption(GUILayoutOption.Type.maxHeight, maxHeight);
        public static GUILayoutOption ExpandWidth(bool expand) => new GUILayoutOption(GUILayoutOption.Type.stretchWidth, expand ? 1 : 0);
        public static GUILayoutOption ExpandHeight(bool expand) => new GUILayoutOption(GUILayoutOption.Type.stretchHeight, expand ? 1 : 0);

        public partial class HorizontalScope : GUI.Scope
        {
            public HorizontalScope(params GUILayoutOption[] options) => BeginHorizontal(options);
            public HorizontalScope(GUIStyle style, params GUILayoutOption[] options) => BeginHorizontal(style, options);
            public HorizontalScope(string text, GUIStyle style, params GUILayoutOption[] options) => BeginHorizontal(text, style, options);
            public HorizontalScope(Texture image, GUIStyle style, params GUILayoutOption[] options) => BeginHorizontal(image, style, options);
            public HorizontalScope(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => BeginHorizontal(content, style, options);
            protected override void CloseScope() => EndHorizontal();
        }

        public partial class VerticalScope : GUI.Scope
        {
            public VerticalScope(params GUILayoutOption[] options) => BeginVertical(options);
            public VerticalScope(GUIStyle style, params GUILayoutOption[] options) => BeginVertical(style, options);
            public VerticalScope(string text, GUIStyle style, params GUILayoutOption[] options) => BeginVertical(text, style, options);
            public VerticalScope(Texture image, GUIStyle style, params GUILayoutOption[] options) => BeginVertical(image, style, options);
            public VerticalScope(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => BeginVertical(content, style, options);
            protected override void CloseScope() => EndVertical();
        }

        public partial class AreaScope : GUI.Scope
        {
            public AreaScope(Rect screenRect) => BeginArea(screenRect);
            public AreaScope(Rect screenRect, string text) => BeginArea(screenRect, text);
            public AreaScope(Rect screenRect, Texture image) => BeginArea(screenRect, image);
            public AreaScope(Rect screenRect, GUIContent content) => BeginArea(screenRect, content);
            public AreaScope(Rect screenRect, string text, GUIStyle style) => BeginArea(screenRect, text, style);
            public AreaScope(Rect screenRect, Texture image, GUIStyle style) => BeginArea(screenRect, image, style);
            public AreaScope(Rect screenRect, GUIContent content, GUIStyle style) => BeginArea(screenRect, content, style);
            protected override void CloseScope() => EndArea();
        }

        public partial class ScrollViewScope : GUI.Scope
        {
            public Vector2 scrollPosition { get; private set; }
            public bool handleScrollWheel { get; set; } = true;

            public ScrollViewScope(Vector2 scrollPosition, params GUILayoutOption[] options) => this.scrollPosition = BeginScrollView(scrollPosition, options);
            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options) =>
                this.scrollPosition = BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, options);
            public ScrollViewScope(Vector2 scrollPosition, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options) =>
                this.scrollPosition = BeginScrollView(scrollPosition, horizontalScrollbar, verticalScrollbar, options);
            public ScrollViewScope(Vector2 scrollPosition, GUIStyle style, params GUILayoutOption[] options) => this.scrollPosition = BeginScrollView(scrollPosition, style, options);
            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options) =>
                this.scrollPosition = BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, horizontalScrollbar, verticalScrollbar, options);
            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, GUIStyle background, params GUILayoutOption[] options) =>
                this.scrollPosition = BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, horizontalScrollbar, verticalScrollbar, background, options);

            protected override void CloseScope() => EndScrollView(handleScrollWheel);
        }
    }
}
