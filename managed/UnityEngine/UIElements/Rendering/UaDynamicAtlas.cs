using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    internal sealed class UaDynamicAtlas : IDisposable
    {
        internal sealed class Entry
        {
            internal Texture2D Source;
            internal Page Page;
            internal RectInt Rect;
            internal UaAtlasAllocator.Allocation Allocation;
            internal int Users;
            internal bool Persistent;
        }
        internal sealed class Page : IDisposable
        {
            internal readonly UaAtlasAllocator Allocator;
            internal readonly Texture2D Token;
            internal RenderTexture Texture;
            internal int Width, Height;
            readonly List<Entry> m_Dirty = new List<Entry>();
            readonly HashSet<Entry> m_Queued = new HashSet<Entry>();
            internal Page(int minimum, int maximum, FilterMode filter)
            {
                Allocator = new UaAtlasAllocator(minimum, maximum);
                using (ObjectRegistry.AsAssets()) Token = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                    { name = "UI Toolkit atlas binding", filterMode = filter };
            }
            internal void Dirty(Entry entry)
            {
                if (m_Queued.Add(entry)) m_Dirty.Add(entry);
            }
            internal void Commit()
            {
                if (Width == 0 || Height == 0) return;
                if (Texture == null || Texture.width != Width || Texture.height != Height)
                {
                    var previous = Texture;
                    using (ObjectRegistry.AsAssets()) Texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32)
                        { name = "UI Toolkit dynamic atlas", filterMode = Token.filterMode, wrapMode = TextureWrapMode.Clamp };
                    Texture.Create();
                    if (previous != null)
                    {
                        UaAtlasBlitter.Copy(previous, Texture, new RectInt(0, 0, previous.width, previous.height), false);
                        UnityEngine.Object.Destroy(previous);
                    }
                }
                foreach (var entry in m_Dirty)
                    if (entry.Source != null && (entry.Users > 0 || entry.Persistent)) UaAtlasBlitter.Copy(entry.Source, Texture, entry.Rect, true);
                m_Dirty.Clear(); m_Queued.Clear();
            }
            public void Dispose()
            {
                if (Texture != null) UnityEngine.Object.Destroy(Texture);
                UnityEngine.Object.Destroy(Token);
                m_Dirty.Clear(); m_Queued.Clear();
            }
        }

        internal static readonly DynamicAtlasSettings DefaultSettings = DynamicAtlasSettings.defaults;
        readonly BaseVisualElementPanel m_Panel;
        readonly Dictionary<Texture2D, Entry> m_Entries = new Dictionary<Texture2D, Entry>();
        Page m_Point, m_Bilinear;
        int m_Minimum, m_Maximum, m_SubSize;
        DynamicAtlasFilters m_Filters;
        DynamicAtlasCustomFilter m_Filter;
        bool m_Configured;
        internal int Generation { get; private set; }
        internal UaDynamicAtlas(BaseVisualElementPanel panel) => m_Panel = panel;

        internal void Configure(DynamicAtlasSettings settings)
        {
            if (settings == null) return;
            if (m_Configured && m_Minimum == settings.minAtlasSize && m_Maximum == settings.maxAtlasSize && m_SubSize == settings.maxSubTextureSize
                && m_Filters == settings.activeFilters && m_Filter == settings.customFilter) return;
            Reset();
            m_Minimum = settings.minAtlasSize; m_Maximum = settings.maxAtlasSize; m_SubSize = settings.maxSubTextureSize;
            m_Filters = settings.activeFilters; m_Filter = settings.customFilter; m_Configured = true;
        }
        internal Entry Acquire(Texture2D texture, bool persistent)
        {
            if (texture == null) return null;
            if (m_Entries.TryGetValue(texture, out var entry))
            {
                if (persistent) entry.Persistent = true;
                else entry.Users++;
                return entry;
            }
            int limit = SystemInfo.maxRenderTextureSize;
            int maximum = m_Maximum >= limit ? limit : Mathf.NextPowerOfTwo(Mathf.Max(1, m_Maximum));
            int minimum = m_Minimum >= maximum ? maximum : Mathf.NextPowerOfTwo(Mathf.Max(1, m_Minimum));
            var filters = m_Filters;
            // The custom filter runs separately for each candidate page, with independent flags.
            if (Valid(texture, FilterMode.Bilinear, filters))
            {
                m_Bilinear ??= new Page(minimum, maximum, FilterMode.Bilinear);
                entry = Add(m_Bilinear, texture, persistent);
                if (entry != null) return entry;
            }
            if (Valid(texture, FilterMode.Point, filters))
            {
                m_Point ??= new Page(minimum, maximum, FilterMode.Point);
                return Add(m_Point, texture, persistent);
            }
            return null;
        }
        Entry Add(Page page, Texture2D texture, bool persistent)
        {
            if (!page.Allocator.Allocate(texture.width, texture.height, out var allocation)) return null;
            var rect = allocation.Rect;
            page.Width = Mathf.Max(page.Width, Mathf.NextPowerOfTwo(rect.xMax));
            page.Height = Mathf.Max(page.Height, Mathf.NextPowerOfTwo(rect.yMax));
            var entry = new Entry { Source = texture, Page = page, Allocation = allocation,
                Rect = new RectInt(rect.x + UaAtlasAllocator.Border, rect.y + UaAtlasAllocator.Border, texture.width, texture.height),
                Users = persistent ? 0 : 1, Persistent = persistent };
            m_Entries.Add(texture, entry); page.Dirty(entry); return entry;
        }
        bool Valid(Texture2D texture, FilterMode filter, DynamicAtlasFilters flags)
        {
            if (m_Filter != null && !m_Filter(texture, ref flags)) return false;
            if ((flags & DynamicAtlasFilters.Readability) != 0 && texture.isReadable) return false;
            if ((flags & DynamicAtlasFilters.Size) != 0 && (texture.width > m_SubSize || texture.height > m_SubSize)) return false;
            if ((flags & DynamicAtlasFilters.Format) != 0 && !FormatSupported(texture.format)) return false;
            if ((flags & DynamicAtlasFilters.ColorSpace) != 0 && QualitySettings.activeColorSpace == ColorSpace.Linear
                && texture.activeTextureColorSpace != ColorSpace.Gamma) return false;
            return (flags & DynamicAtlasFilters.FilterMode) == 0 || texture.filterMode == filter;
        }
        static bool FormatSupported(TextureFormat format) => format switch
        {
            TextureFormat.RHalf or TextureFormat.RGHalf or TextureFormat.RGBAHalf or TextureFormat.RFloat or TextureFormat.RGFloat
                or TextureFormat.RGBAFloat or TextureFormat.YUY2 or TextureFormat.RGB9e5Float or TextureFormat.BC6H
                or TextureFormat.ASTC_HDR_4x4 or TextureFormat.ASTC_HDR_5x5 or TextureFormat.ASTC_HDR_6x6 or TextureFormat.ASTC_HDR_8x8
                or TextureFormat.ASTC_HDR_10x10 or TextureFormat.ASTC_HDR_12x12 or TextureFormat.R16_SIGNED or TextureFormat.RG16_SIGNED
                or TextureFormat.RGB24_SIGNED or TextureFormat.RGBA32_SIGNED or TextureFormat.R8_SIGNED or TextureFormat.RG32_SIGNED
                or TextureFormat.RGB48_SIGNED or TextureFormat.RGBA64_SIGNED or TextureFormat.RG32 or TextureFormat.RGB48 or TextureFormat.RGBA64
                => false,
            _ => true
        };
        internal void Return(Entry entry)
        {
            if (entry == null || entry.Persistent) return;
            if (--entry.Users != 0) return;
            // A reset retires all entries at once; old retained geometry can still release its leases.
            if (!m_Entries.TryGetValue(entry.Source, out var current) || current != entry) return;
            m_Entries.Remove(entry.Source); entry.Page.Allocator.Free(entry.Allocation);
        }
        internal void SetDirty(Texture2D texture)
        {
            if (texture != null && m_Entries.TryGetValue(texture, out var entry)) entry.Page.Dirty(entry);
        }
        internal Vector2Int Location(Texture2D texture) => texture != null && m_Entries.TryGetValue(texture, out var entry)
            ? new Vector2Int(entry.Rect.x, entry.Rect.y) : default;
        internal Texture Resolve(Texture token) => m_Point != null && ReferenceEquals(token, m_Point.Token) ? m_Point.Texture
            : m_Bilinear != null && ReferenceEquals(token, m_Bilinear.Token) ? m_Bilinear.Texture : token;
        internal void Commit() { m_Point?.Commit(); m_Bilinear?.Commit(); }
        internal void Reset()
        {
            Dispose(); Generation++;
            Repaint(m_Panel.visualTree);
        }
        static void Repaint(VisualElement element)
        {
            var geometry = element.uaGeometry;
            if (geometry != null && geometry.HasTextures) geometry.Invalidate();
            var children = element.hierarchy.children;
            for (int i = 0; i < children.Count; i++) Repaint(children[i]);
        }
        public void Dispose()
        {
            m_Entries.Clear(); m_Point?.Dispose(); m_Bilinear?.Dispose(); m_Point = null; m_Bilinear = null;
        }
    }
}
