using System;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Project release")]
    public class UitkAtlasTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        public UitkAtlasTests() => ProjectInfo.UnityVersion = "6000.0.71f1";
        public void Dispose() => ProjectInfo.UnityVersion = m_Release;
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
        [Theory]
        [InlineData(TextureFormat.Alpha8, UaPainter.VertText)]
        [InlineData(TextureFormat.RGBA32, UaPainter.VertTexture)]
        public void BitmapTextKeepsItsOwnTextureWithAllAtlasFiltersDisabled(TextureFormat format, byte type)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.dynamicAtlasSettings.activeFilters = DynamicAtlasFilters.None;
            var texture = new Texture2D(8, 8, format, false);
            var vertices = new Unity.Collections.NativeArray<Vertex>(4, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < 4; i++) vertices[i] = new Vertex
                { position = new Vector3(i, i, 0), tint = Color.white, uv = new Vector2(.25f, .75f) };
            try
            {
                using var panel = new Panel(settings, ContextType.Player, EventDispatcher.CreateDefault(), null);
                panel.visualTree.style.width = 200; panel.visualTree.style.height = 200;
                var element = new VisualElement(); element.style.width = 20; element.style.height = 20;
                element.generateVisualContent += ctx => ctx.UaDrawText(new Unity.Collections.NativeSlice<Vertex>(vertices), texture, 0);
                panel.visualTree.Add(element); panel.ApplyStyles(); panel.ValidateLayout();
                var painter = new UaPainter(); painter.Paint(panel, 200);
                Assert.All(element.uaGeometry.Content.Segments, segment => Assert.Null(segment.AtlasEntry));
                Assert.Contains(painter.Batches, batch => batch.TextureCount > 0 && ReferenceEquals(batch.Textures[0], texture));
                Assert.All(painter.Uvs[3], flag => Assert.Equal(type, (byte)Mathf.RoundToInt(flag.x * 255)));
                Assert.All(painter.Uvs[0], uv => Assert.Equal(new Vector2(.25f, .75f), new Vector2(uv.x, uv.y)));
                texture.SetPixel(0, 0, Color.red); texture.Apply(false);
                painter.Paint(panel, 200);
                Assert.All(element.uaGeometry.Content.Segments, segment => Assert.Null(segment.AtlasEntry));
            }
            finally { vertices.Dispose(); Object.DestroyImmediate(texture); Object.DestroyImmediate(settings); }
        }

        [Fact]
        public void AtlasCopiesIgnoreAndRestoreCallerScissorAndReuseTheirMeshStorage()
        {
            if (Shader.Find("Hidden/BlitCopy") == null)
                Shader.Register(Shader.Create("Hidden/BlitCopy", 2000));
            var source = new Texture2D(5, 3, TextureFormat.RGBA32, false);
            var target = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32);
            var saved = Unbound.Runtime.GfxStream.Current;
            var scissor = new Rect(100, 100, 10, 10);
            var rect = new RectInt(1, 1, 5, 3);
            try
            {
                Unbound.Runtime.GfxStream.BeginFrame(128, 128, ColorSpace.Gamma);
                Unbound.Runtime.GfxStream.Scissor(scissor);
                for (int i = 0; i < 8; i++) { UaAtlasBlitter.Copy(source, target, rect, true); Unbound.Runtime.GfxStream.Flush(); }
                AllocationMeasurement.EmptyAllocationContext();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 4; i++) { UaAtlasBlitter.Copy(source, target, rect, true); Unbound.Runtime.GfxStream.Flush(); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0, allocated);
                Assert.Equal(scissor, Unbound.Runtime.GfxStream.Current.Scissor);
                int start = Unbound.Runtime.GfxStream.Recorded.Length;
                UaAtlasBlitter.Copy(source, target, rect, true);
                var packet = Unbound.Runtime.GfxStream.Recorded.Slice(start);
                int offset = 0;
                while (packet[offset] != 9)
                {
                    int size = packet[offset] switch { 3 => 25, 5 => 17, 6 => 133, _ => throw new InvalidOperationException("Unexpected atlas copy command") };
                    offset += size;
                }
                // Draw's fixed fields: mesh, submesh, material, pass, model, color, main texture.
                byte flags = packet[offset + 1 + 8 + 4 + 8 + 4 + 64 + 16 + 8];
                Assert.Equal(0, flags & 1);
                Assert.Equal(scissor, Unbound.Runtime.GfxStream.Current.Scissor);
            }
            finally
            {
                Unbound.Runtime.GfxStream.Restore(saved); Unbound.Runtime.GfxStream.Flush();
                Object.DestroyImmediate(source); Object.DestroyImmediate(target);
            }
        }
    }
}
