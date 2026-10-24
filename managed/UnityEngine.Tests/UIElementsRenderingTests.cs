using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    public class UIElementsRenderingTests
    {
        [Fact]
        public void Radii_that_overlap_scale_down_together()
        {
            var r = new UaPainter.Radii { topLeft = new Vector2(30, 30), topRight = new Vector2(30, 30), bottomRight = new Vector2(10, 10), bottomLeft = new Vector2(10, 10) };
            var fit = r.Fit(new Rect(0, 0, 40, 100));
            Assert.Equal(20f, fit.topLeft.x, 4);
            Assert.Equal(20f, fit.topRight.x, 4);
            Assert.Equal(new Vector2(20f / 3f, 20f / 3f).x, fit.bottomLeft.x, 4);
            var same = r.Fit(new Rect(0, 0, 100, 100));
            Assert.Equal(30f, same.topLeft.x);
        }

        [Fact]
        public void Shader_info_entries_land_where_UnityUIE_reads_them()
        {
            // uie_decode_shader_info_texel_pos: page * (32, 8) + (id % 32, id / 32 * stride).
            Assert.Equal((2 * 32 + 1, 0), UaShaderInfo.Texel(UaShaderInfo.OpacityPage(1), UaShaderInfo.InPage(1), 1));
            Assert.Equal((3 * 32 + 1, 4), UaShaderInfo.Texel(UaShaderInfo.TextPage(33), UaShaderInfo.InPage(33), 4));
            Assert.Equal(4, UaShaderInfo.OpacityPage(256));
            Assert.Equal(0, UaShaderInfo.InPage(256));
        }

        [Fact]
        public void Background_sizes_fit_the_image_as_css_does()
        {
            var image = new Rect(0, 0, 16, 16);
            var total = new Rect(0, 0, 48, 36);
            var contain = UaPainter.ImageSize(image, total, new BackgroundSize(BackgroundSizeType.Contain));
            Assert.Equal(36f, contain.width);
            Assert.Equal(36f, contain.height);
            var cover = UaPainter.ImageSize(image, total, new BackgroundSize(BackgroundSizeType.Cover));
            Assert.Equal(48f, cover.width);
            Assert.Equal(48f, cover.height);
            var stretch = UaPainter.ImageSize(image, total, new BackgroundSize(Length.Percent(100), Length.Percent(100)));
            Assert.Equal(48f, stretch.width);
            Assert.Equal(36f, stretch.height);
        }
    }
}
