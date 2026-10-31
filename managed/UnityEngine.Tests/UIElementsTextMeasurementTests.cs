using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    public class UIElementsTextMeasurementTests
    {
        // Models metrics becoming available after initial layout, as the independent Native
        // dynamic-font recordings do. All cases keep the same final text color and font style.
        sealed class MetricText : TextElement
        {
            public float measuredWidth = 100;
            protected internal override Vector2 DoMeasure(float width, MeasureMode widthMode, float height, MeasureMode heightMode) =>
                new Vector2(measuredWidth, 20);
        }

        [Theory]
        [InlineData("matched-class", 96f)]
        [InlineData("inherited-color", 96f)]
        [InlineData("unmatched-class", 100f)]
        [InlineData("inline-color", 100f)]
        public void TextMeasurementFollowsStylesheetMatchingWithEqualResolvedValues(string change, float expected)
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var root = panel.visualTree;
            root.style.width = 400; root.style.height = 100; root.style.alignItems = Align.FlexStart;
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            new UssImporter("text-measurement.uss").Import(sheet, ".metric { color: white; } .changed { color: white; }");
            root.styleSheets.Add(sheet);
            var text = new MetricText(); text.AddToClassList("metric"); root.Add(text);
            void Tick() { panel.ApplyStyles(); panel.ValidateLayout(); }
            Tick();
            Assert.Equal(100, text.layout.width);
            text.measuredWidth = 96;
            switch (change)
            {
                case "matched-class": text.AddToClassList("changed"); break;
                case "inherited-color": root.style.color = Color.red; break;
                case "unmatched-class": text.AddToClassList("unmatched"); break;
                case "inline-color": text.style.color = Color.white; break;
            }
            Tick();
            Assert.Equal(Color.white, text.resolvedStyle.color);
            Assert.Equal(expected, text.layout.width);
        }
    }
}
