using System;
using UnityEngine.UIElements;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    // RuntimePanelUtils (Unity 6000.0.71f1, tests/projects/uitkpanelutils). Controls outside the
    // probe: a panel's own screen-to-panel function and scale, panels that are not runtime panels,
    // and the display heights the camera conversions flip by.
    public class UitkPanelUtilsTests
    {
        [Fact]
        public void ScreenPositionsGoThroughThePanelsFunctionThenItsScale()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 2f;
            settings.SetScreenToPanelSpaceFunction(p => p + new Vector2(10f, 20f));
            var document = new GameObject("Document").AddComponent<UIDocument>();
            document.panelSettings = settings;
            try
            {
                var panel = document.rootVisualElement.panel;
                Assert.Equal(new Vector2(55f, 30f), RuntimePanelUtils.ScreenToPanel(panel, new Vector2(100f, 40f)));
            }
            finally
            {
                Object.DestroyImmediate(document.gameObject);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void PanelsThatAreNotRuntimePanelsAreRefused()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            Assert.Throws<InvalidCastException>(() => RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero));
            Assert.Throws<NullReferenceException>(() => RuntimePanelUtils.ScreenToPanel(null, Vector2.zero));
        }

        [Fact]
        public void DisplayHeightsAreTheScreensForTheGameViewAndMissingDisplays()
        {
            Assert.Equal(Screen.height, UIElementsRuntimeUtility.GetEditorDisplayHeight(0));
            Assert.Equal(Screen.height, UIElementsRuntimeUtility.GetRuntimeDisplayHeight(7 + Display.displays.Length));
            Assert.Equal(new Vector2(3f, 6f), UIElementsRuntimeUtility.FlipY(new Vector2(3f, 4f), 10f));
            Assert.Equal(new Vector2(5f, Screen.height - 2f), UIElementsRuntimeUtility.ScreenBottomLeftToPanelPosition(new Vector2(5f, 2f), 0));
        }
    }
}
