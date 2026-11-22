using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.TextCore.Text;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The rules a GUIStyle places and sizes its content by (GUIStyle.Text.cs). The test
    /// host has no font engine, so text has no size here: the image rules and the snapping are
    /// what these tests hold; text sizes, wrapping and pixels are tests/projects/imguiskin.</summary>
    [Collection("Serial")]
    public class ImguiTextContentTests
    {
        sealed class Captured : ILogHandler, IDisposable
        {
            public readonly List<string> Entries = new List<string>();
            readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
            public Captured() => Debug.unityLogger.logHandler = this;
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Entries.Add(logType + ":" + (args != null && args.Length > 0 ? string.Format(format, args) : format));
            public void LogException(Exception exception, Object context) => Entries.Add("Exception:" + exception.Message);
            public void Dispose() => Debug.unityLogger.logHandler = m_Previous;
        }

        static Texture2D Image(int width, int height)
        {
            var texture = (Texture2D)Activator.CreateInstance(typeof(Texture2D), true);
            texture.m_Width = width;
            texture.m_Height = height;
            return texture;
        }

        [Fact]
        public void TextOfAFontWithoutAFontAssetIsWarnedAboutOncePerFontAndHasNoSize()
        {
            Assert.False(Native.IsReady);
            using var log = new Captured();
            IMGUITextHandle.ResetForTests();
            const string Warning = "Warning:Can't Generate Mesh, No Font Asset has been assigned.";
            int Warnings() => log.Entries.FindAll(e => e == Warning).Count;
            var first = new Font("First Font Without A Face");
            var second = new Font("Second Font Without A Face");
            try
            {
                var style = new GUIStyle { font = first, padding = new RectOffset(3, 4, 1, 2) };
                Assert.Equal(new Vector2(7f, 3f), style.CalcSize(new GUIContent("Some text")));
                Assert.Equal(0f, style.lineHeight);
                Assert.Equal(1, Warnings());
                // The font is not tried again: nothing more is logged for it, whatever asks.
                int entries = log.Entries.Count;
                Assert.Equal(3f, style.CalcHeight(new GUIContent("Some text"), 50f));
                style.CalcMinMaxWidth(new GUIContent("Some text"), out float min, out float max);
                Assert.Equal((7f, 7f), (min, max));
                new GUIStyle { font = first }.CalcSize(new GUIContent("Other"));
                Assert.Equal(entries, log.Entries.Count);
                // Another font is warned about in its turn, and the first stays quiet between them.
                new GUIStyle { font = second }.CalcSize(new GUIContent("Other"));
                Assert.Equal(2, Warnings());
                entries = log.Entries.Count;
                style.CalcSize(new GUIContent("Some text"));
                new GUIStyle { font = second }.CalcSize(new GUIContent("Other"));
                Assert.Equal(entries, log.Entries.Count);
            }
            finally
            {
                IMGUITextHandle.ResetForTests();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Fact]
        public void AnImageLeftOfTextGivesWayToMaximumSizes()
        {
            var image = new Vector2(40f, 40f);
            var text = new Vector2(41f, 13f);
            // Nothing is over: the image stays (also when a maximum equals the content).
            Assert.Equal(image, GUIStyle.ConstrainImageLeft(image, text, Vector2.zero));
            Assert.Equal(image, GUIStyle.ConstrainImageLeft(image, text, new Vector2(81f, 40f)));
            // Too wide: the image narrows to the room the text leaves and keeps its height.
            Assert.Equal(new Vector2(17f, 40f), GUIStyle.ConstrainImageLeft(image, text, new Vector2(58f, 0f)));
            // Too tall: the image scales to the maximum height.
            Assert.Equal(new Vector2(20f, 20f), GUIStyle.ConstrainImageLeft(image, text, new Vector2(0f, 20f)));
            Assert.Equal(new Vector2(10f, 10f), GUIStyle.ConstrainImageLeft(image, text, new Vector2(90f, 10f)));
            // Over in both: the direction that is over by more decides.
            Assert.Equal(new Vector2(19f, 19f), GUIStyle.ConstrainImageLeft(image, text, new Vector2(58f, 19f)));
            Assert.Equal(new Vector2(-1f, 40f), GUIStyle.ConstrainImageLeft(image, text, new Vector2(40f, 50f)));
            Assert.Equal(new Vector2(32f, 16f), GUIStyle.ConstrainImageLeft(new Vector2(40f, 20f), text, new Vector2(50f, 16f)));
            Assert.Equal(new Vector2(20f, 20f), GUIStyle.ConstrainImageLeft(new Vector2(40f, 20f), new Vector2(10f, 13f), new Vector2(30f, 19f)));
        }

        [Fact]
        public void AnImageAboveTextGivesWayToMaximumSizes()
        {
            var image = new Vector2(40f, 40f);
            var text = new Vector2(41f, 13f);
            Assert.Equal(image, GUIStyle.ConstrainImageAbove(image, text, Vector2.zero));
            Assert.Equal(image, GUIStyle.ConstrainImageAbove(image, text, new Vector2(41f, 53f)));
            // A maximum width under the text's narrows the image and keeps its height.
            Assert.Equal(new Vector2(30f, 40f), GUIStyle.ConstrainImageAbove(image, text, new Vector2(30f, 0f)));
            // Too tall: the image takes the height the text leaves, scaled.
            Assert.Equal(new Vector2(17f, 17f), GUIStyle.ConstrainImageAbove(image, text, new Vector2(0f, 30f)));
            Assert.Equal(new Vector2(17f, 17f), GUIStyle.ConstrainImageAbove(image, text, new Vector2(50f, 30f)));
            // Too tall and wider than the maximum width by more: the width decides.
            Assert.Equal(new Vector2(20f, 40f), GUIStyle.ConstrainImageAbove(image, text, new Vector2(20f, 45f)));
        }

        [Fact]
        public void ContentIsPlacedByImagePositionAndAlignment()
        {
            var inner = new Rect(10f, 20f, 150f, 40f);
            var image = new Vector2(20f, 20f);
            var text = new Vector2(28f, 13f);

            // Text only: the text's rect starts at the content rect (the text is aligned inside it).
            var textOnly = new GUIStyle { imagePosition = ImagePosition.TextOnly, alignment = TextAnchor.LowerRight };
            var placed = textOnly.PlaceContentUnsnapped(inner, image, text);
            Assert.Equal(new Vector2(10f, 20f), placed.TextOrigin);
            Assert.Equal(default, placed.Image);
            Assert.Equal(text, placed.Total);

            // Image left, upper left: the image at the corner, centred on the taller of both;
            // the text one pixel after it.
            var left = new GUIStyle { imagePosition = ImagePosition.ImageLeft };
            placed = left.PlaceContentUnsnapped(inner, image, text);
            Assert.Equal(new Rect(10f, 20f, 20f, 20f), placed.Image);
            Assert.Equal(new Vector2(31f, 20f), placed.TextOrigin);
            Assert.Equal(new Vector2(48f, 20f), placed.Total);
            placed = left.PlaceContentUnsnapped(inner, new Vector2(20f, 9f), text);
            Assert.Equal(new Rect(10f, 22f, 20f, 9f), placed.Image);

            // Aligned: image and text together are placed, the text's rect still starts after the image.
            var leftCentered = new GUIStyle { imagePosition = ImagePosition.ImageLeft, alignment = TextAnchor.MiddleCenter, contentOffset = new Vector2(2f, 3f) };
            placed = leftCentered.PlaceContentUnsnapped(inner, image, text);
            Assert.Equal(new Rect(10f + 2f + 51f, 20f + 3f + 10f, 20f, 20f), placed.Image);
            Assert.Equal(new Vector2(10f + 2f + 21f, 20f + 3f), placed.TextOrigin);

            // Without an image the text is not moved.
            Assert.Equal(new Vector2(10f, 20f), left.PlaceContentUnsnapped(inner, Vector2.zero, text).TextOrigin);

            // Image above: the image centred over the pair's width (on a whole pixel), the text under it.
            var above = new GUIStyle { imagePosition = ImagePosition.ImageAbove, alignment = TextAnchor.MiddleCenter };
            placed = above.PlaceContentUnsnapped(inner, image, text);
            Assert.Equal(new Rect(Mathf.Floor(71f + 4f + 0.5f), 23.5f, 20f, 20f), placed.Image);
            Assert.Equal(new Vector2(10f, 43.5f), placed.TextOrigin);

            // Image only, lower right.
            var only = new GUIStyle { imagePosition = ImagePosition.ImageOnly, alignment = TextAnchor.LowerRight };
            placed = only.PlaceContentUnsnapped(inner, image, text);
            Assert.Equal(new Rect(140f, 40f, 20f, 20f), placed.Image);

            // An image larger than its room is scaled into it, to whole pixels; it is never enlarged.
            placed = left.PlaceContentUnsnapped(new Rect(0f, 0f, 60f, 18f), new Vector2(40f, 40f), new Vector2(9f, 13f));
            Assert.Equal(new Rect(0f, 0f, 18f, 18f), placed.Image);
            placed = left.PlaceContentUnsnapped(new Rect(0f, 0f, 29f, 100f), new Vector2(40f, 40f), new Vector2(9f, 13f));
            Assert.Equal(new Vector2(20f, 20f), placed.Image.size);
            placed = left.PlaceContentUnsnapped(new Rect(0f, 0f, 5f, 100f), new Vector2(40f, 40f), new Vector2(9f, 13f));
            Assert.Equal(Vector2.zero, placed.Image.size);
            placed = above.PlaceContentUnsnapped(new Rect(0f, 0f, 100f, 23f), new Vector2(40f, 40f), new Vector2(9f, 13f));
            Assert.Equal(new Vector2(10f, 10f), placed.Image.size);

            // An icon size (both components set) replaces the image's size.
            var icon = UnityEditor.EditorGUIUtility.GetIconSize();
            try
            {
                UnityEditor.EditorGUIUtility.SetIconSize(new Vector2(12f, 12f));
                Assert.Equal(new Vector2(12f, 12f), left.PlaceContentUnsnapped(inner, image, text).Image.size);
                UnityEditor.EditorGUIUtility.SetIconSize(new Vector2(12f, 0f));
                Assert.Equal(new Vector2(20f, 20f), left.PlaceContentUnsnapped(inner, image, text).Image.size);
            }
            finally { UnityEditor.EditorGUIUtility.SetIconSize(icon); }
        }

        [Fact]
        public void ContentSnapsToPixelsFromTheLeftAndFromTheBottom()
        {
            // From the left a coordinate rounds down until 0.52 past a pixel.
            Assert.Equal(240f, GUIClip.SnapFromLeft(240.4f));
            Assert.Equal(240f, GUIClip.SnapFromLeft(240.5f));
            Assert.Equal(241f, GUIClip.SnapFromLeft(240.53f));
            Assert.Equal(240f, GUIClip.SnapFromLeft(240f));
            // From the bottom the same rule moves a half pixel down the screen.
            Assert.Equal(146f, GUIClip.SnapFromBottom(145.495f, 180f));
            Assert.Equal(146f, GUIClip.SnapFromBottom(145.5f, 180f));
            Assert.Equal(145f, GUIClip.SnapFromBottom(145.4f, 180f));
            Assert.Equal(145f, GUIClip.SnapFromBottom(144.495f, 180f));
            Assert.Equal(112f, GUIClip.SnapFromBottom(112.4f, 180f));
            Assert.Equal(145f, GUIClip.SnapFromBottom(145f, 180f));
        }

        [Fact]
        public void SizesCountTheImageByItsPosition()
        {
            using var log = new Captured();
            var image = Image(20, 10);
            var content = new GUIContent(image);
            var padding = new RectOffset(3, 4, 1, 2);
            Assert.Equal(new Vector2(27f, 13f), new GUIStyle { padding = padding, imagePosition = ImagePosition.ImageOnly }.CalcSize(content));
            Assert.Equal(new Vector2(27f, 13f), new GUIStyle { padding = padding, imagePosition = ImagePosition.ImageLeft }.CalcSize(content));
            // A style that shows text only gives the image no room.
            Assert.Equal(new Vector2(7f, 3f), new GUIStyle { padding = padding, imagePosition = ImagePosition.TextOnly }.CalcSize(content));
            // Fixed sizes win, each on its axis.
            Assert.Equal(new Vector2(50f, 13f), new GUIStyle { padding = padding, imagePosition = ImagePosition.ImageOnly, fixedWidth = 50f }.CalcSize(content));
            Assert.Equal(new Vector2(50f, 60f), new GUIStyle { fixedWidth = 50f, fixedHeight = 60f }.CalcSize(content));

            // CalcHeight keeps the image's own height, CalcSize takes the icon size.
            var icon = UnityEditor.EditorGUIUtility.GetIconSize();
            try
            {
                UnityEditor.EditorGUIUtility.SetIconSize(new Vector2(6f, 6f));
                var left = new GUIStyle { padding = padding, imagePosition = ImagePosition.ImageLeft, wordWrap = true };
                Assert.Equal(new Vector2(13f, 9f), left.CalcSize(content));
                Assert.Equal(13f, left.CalcHeight(content, 100f));
            }
            finally { UnityEditor.EditorGUIUtility.SetIconSize(icon); }

            // The widths: an image keeps its width, scaled to a fixed height.
            new GUIStyle { padding = padding, imagePosition = ImagePosition.ImageLeft }.CalcMinMaxWidth(content, out float min, out float max);
            Assert.Equal((27f, 27f), (min, max));
            new GUIStyle { padding = padding, imagePosition = ImagePosition.ImageOnly, fixedHeight = 30f }.CalcMinMaxWidth(content, out min, out max);
            Assert.Equal((67f, 67f), (min, max));
            new GUIStyle { padding = padding, fixedWidth = 45f }.CalcMinMaxWidth(content, out min, out max);
            Assert.Equal((45f, 45f), (min, max));

            // Null content measures as content without text or image.
            Assert.Equal(new Vector2(7f, 3f), new GUIStyle { padding = padding }.CalcSize(null));
            new GUIStyle { padding = padding }.CalcMinMaxWidth(null, out min, out max);
            Assert.Equal((7f, 7f), (min, max));
            Assert.Equal(new GUIStyle().GetCursorPixelPosition(new Rect(5f, 6f, 50f, 20f), GUIContent.none, 0), new GUIStyle().GetCursorPixelPosition(new Rect(5f, 6f, 50f, 20f), null, 0));
            Assert.Equal(0, new GUIStyle().GetCursorStringIndex(new Rect(5f, 6f, 50f, 20f), null, new Vector2(9f, 9f)));
            Assert.Equal(new GUIStyle { padding = padding }.CalcHeight(GUIContent.none, 10f), new GUIStyle { padding = padding }.CalcHeight(null, 10f));
            Assert.Equal(34f, new GUIStyle { fixedHeight = 34f }.CalcHeight(null, 10f));
        }

        [Fact]
        public void FontAssetsOfOsFontsTakeNoFontFeaturesWhereThatWasMeasured()
        {
            var asset = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                using (TestRelease.Pin(TestRelease.Measured))
                {
                    asset.atlasPopulationMode = AtlasPopulationMode.DynamicOS;
                    Assert.Equal(OperatingSystem.IsLinux(), asset.TakesNoFontFeatures);
                    asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                    Assert.False(asset.TakesNoFontFeatures);
                }
                using (TestRelease.Pin(TestRelease.Unmeasured))
                {
                    asset.atlasPopulationMode = AtlasPopulationMode.DynamicOS;
                    Assert.False(asset.TakesNoFontFeatures);
                }
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
