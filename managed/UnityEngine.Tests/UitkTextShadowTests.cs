using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The outline and shadow of a text element's distance field text, as the panel shader
    /// takes them (tests/projects/uitktextshadow measures the drawn result against the Editor).</summary>
    public class UitkTextShadowTests
    {
        static (Panel panel, Label label, FontAsset font) Setup()
        {
            var font = ScriptableObject.CreateInstance<FontAsset>();
            font.atlasPadding = 9;
            font.faceInfo = new FaceInfo { pointSize = 90 };
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var panel = new Panel(settings, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var label = new Label("Ag");
            label.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            label.style.fontSize = 30;
            panel.visualTree.Add(label);
            return (panel, label, font);
        }

        [Fact]
        public void ShadowAndOutlineAreFractionsOfTheAtlasPaddingAtTheFontsPointSize()
        {
            var (panel, label, font) = Setup();
            try
            {
                // One pixel at 30 points is 90 / 30 = 3 units of the font's point size, a third of its padding of 9.
                label.style.textShadow = new TextShadow { offset = new Vector2(1.5f, -6f), blurRadius = 0.75f, color = Color.red };
                label.style.unityTextOutlineWidth = 0.6f;
                label.style.unityTextOutlineColor = Color.blue;
                label.style.color = Color.green;
                panel.ApplyStyles();
                var s = TextElement.TextCoreSettings(label);
                Assert.Equal(0.5f, s.underlayOffset.x, 5);
                Assert.Equal(-1f, s.underlayOffset.y, 5); // -2 paddings: within one
                Assert.Equal(0.25f, s.underlaySoftness, 5);
                Assert.Equal(Color.red, s.underlayColor);
                Assert.Equal(0.2f, s.outlineWidth, 5);
                Assert.Equal(Color.blue, s.outlineColor);
                Assert.Equal(Color.green, s.faceColor);

                // A wide blur and offset stop at one padding; an outline of no width has no colour.
                label.style.textShadow = new TextShadow { offset = new Vector2(9f, 0f), blurRadius = 9f, color = Color.black };
                label.style.unityTextOutlineWidth = 0f;
                panel.ApplyStyles();
                s = TextElement.TextCoreSettings(label);
                Assert.Equal(new Vector2(1f, 0f), s.underlayOffset);
                Assert.Equal(1f, s.underlaySoftness);
                Assert.Equal(0f, s.outlineWidth);
                Assert.Equal(new Color(0f, 0f, 1f, 0f), s.outlineColor);
            }
            finally
            {
                panel.Dispose();
                Object.DestroyImmediate(font);
            }
        }

        [Fact]
        public void PlainTextHasTheDefaultSettings()
        {
            var (panel, label, font) = Setup();
            try
            {
                panel.ApplyStyles();
                Assert.True(TextElement.TextCoreSettings(label).IsDefault);
            }
            finally
            {
                panel.Dispose();
                Object.DestroyImmediate(font);
            }
        }
    }
}
