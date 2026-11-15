using System;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    public class UitkAtlasTests
    {
        [Fact]
        public void RowsGrowAcrossAreasAndReuseFreedGaps()
        {
            var allocator = new UaAtlasAllocator(64, 256);
            Assert.True(allocator.Allocate(5, 3, out var a));
            Assert.Equal(new RectInt(0, 0, 7, 5), a.Rect);
            Assert.True(allocator.Allocate(5, 3, out var b));
            Assert.Equal(new RectInt(7, 0, 7, 5), b.Rect);
            var locations = new[] { new Vector2Int(0, 6), new Vector2Int(64, 0), new Vector2Int(0, 64), new Vector2Int(62, 64) };
            foreach (var location in locations)
            {
                Assert.True(allocator.Allocate(60, 31, out var growth));
                Assert.Equal(location, new Vector2Int(growth.Rect.x, growth.Rect.y));
            }
            allocator.Free(a);
            Assert.True(allocator.Allocate(5, 3, out var replacement));
            Assert.Equal(a.Rect, replacement.Rect);
        }

        [Fact]
        public void EmptyRowsReturnTheirWholeHeightAndAdjacentGapsCoalesce()
        {
            var allocator = new UaAtlasAllocator(32, 32);
            Assert.True(allocator.Allocate(13, 5, out var a));
            Assert.True(allocator.Allocate(13, 5, out var b));
            Assert.True(allocator.Allocate(13, 5, out var c));
            allocator.Free(a); allocator.Free(b); allocator.Free(c);
            Assert.True(allocator.Allocate(29, 13, out var merged));
            Assert.Equal(new RectInt(0, 0, 31, 15), merged.Rect);
            Assert.False(allocator.Allocate(33, 1, out _));
            Assert.False(allocator.Allocate(1, 31, out _));
            Assert.False(allocator.Allocate(0, 1, out _));
            Assert.False(allocator.Allocate(1, int.MaxValue, out _));
        }

        [Fact]
        public void SharedTexturesKeepOneAllocationUntilTheirLastUserLeaves()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            using var atlas = new UaDynamicAtlas(panel);
            var settings = DynamicAtlasSettings.defaults;
            settings.activeFilters &= ~DynamicAtlasFilters.Readability;
            atlas.Configure(settings);
            var texture = new Texture2D(13, 5) { filterMode = FilterMode.Bilinear };
            var a = atlas.Acquire(texture, false);
            var b = atlas.Acquire(texture, false);
            Assert.Same(a, b); Assert.Equal(2, a.Users);
            atlas.Return(a);
            Assert.Equal(1, a.Users);
            var control = new Texture2D(13, 5) { filterMode = FilterMode.Bilinear };
            var c = atlas.Acquire(control, false);
            Assert.NotEqual(a.Rect, c.Rect);
            atlas.Return(b);
            var replacement = atlas.Acquire(texture, false);
            Assert.NotSame(a, replacement); Assert.Equal(a.Rect, replacement.Rect);
            atlas.Reset();
            atlas.Return(replacement); atlas.Return(c);
            var fresh = atlas.Acquire(texture, false);
            Assert.Equal(new RectInt(1, 1, 13, 5), fresh.Rect);
            atlas.Return(fresh);
            Object.DestroyImmediate(texture); Object.DestroyImmediate(control);
        }

        [Fact]
        public void FiltersRejectReadableAndTrilinearTexturesAndCanBeOverridden()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            using var atlas = new UaDynamicAtlas(panel);
            var settings = DynamicAtlasSettings.defaults;
            atlas.Configure(settings);
            var texture = new Texture2D(13, 5) { filterMode = FilterMode.Trilinear };
            Assert.Null(atlas.Acquire(texture, false));
            settings.activeFilters &= ~DynamicAtlasFilters.Readability;
            atlas.Configure(settings);
            Assert.Null(atlas.Acquire(texture, false));
            settings.customFilter = (Texture2D source, ref DynamicAtlasFilters filters) =>
            { filters &= ~DynamicAtlasFilters.FilterMode; return true; };
            atlas.Configure(settings);
            var entry = atlas.Acquire(texture, false);
            Assert.NotNull(entry); Assert.Equal(FilterMode.Bilinear, entry.Page.Token.filterMode);
            settings.customFilter = (Texture2D source, ref DynamicAtlasFilters filters) => false;
            atlas.Configure(settings);
            Assert.Null(atlas.Acquire(texture, false));
            Object.DestroyImmediate(texture);
        }
    }
}
