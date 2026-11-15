using System;
using UnityEngine;
using UnityEngine.UIElements;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    // tests/projects/runtimepanels: two identical Unity 6000.0.71f1 captures.
    public class RuntimePanelInterfaceTests
    {
        static UIDocument Document(string name, PanelSettings settings)
        {
            var document = new GameObject(name).AddComponent<UIDocument>();
            document.panelSettings = settings;
            return document;
        }

        [Fact]
        public void ADocumentReportsTheRuntimePanelItWasFirstEnabledOrAskedOn()
        {
            var settingsA = ScriptableObject.CreateInstance<PanelSettings>();
            var settingsB = ScriptableObject.CreateInstance<PanelSettings>();
            settingsA.name = "SettingsA";
            var first = Document("First", settingsA);
            var fourth = Document("Fourth", settingsA);
            var bare = Document("Bare", null);
            var sixth = Document("Sixth", null);
            var selectable = new GameObject("Selectable");
            try
            {
                var panelA = first.rootVisualElement.panel;
                var runtime = Assert.IsAssignableFrom<IRuntimePanel>(panelA);
                Assert.Same(panelA, first.runtimePanel);
                Assert.Same(settingsA, runtime.panelSettings);
                Assert.Equal(ContextType.Player, runtime.contextType);

                // The selectable object is the panel's own state, read through the interface.
                Assert.Null(runtime.selectableGameObject);
                runtime.selectableGameObject = selectable;
                Assert.Same(selectable, first.runtimePanel.selectableGameObject);
                runtime.selectableGameObject = null;
                Assert.Null(runtime.selectableGameObject);

                // After a settings change the document keeps reporting the first panel, while a
                // document first asked after its change reports the new one.
                first.panelSettings = settingsB;
                var panelB = first.rootVisualElement.panel;
                Assert.NotSame(panelA, panelB);
                Assert.Same(panelA, first.runtimePanel);
                fourth.panelSettings = settingsB;
                Assert.Same(panelB, fourth.runtimePanel);
                // Enabling on its settings is enough: a later change keeps that panel.
                sixth.panelSettings = settingsA;
                sixth.enabled = false;
                sixth.enabled = true;
                sixth.panelSettings = settingsB;
                Assert.Same(panelB, sixth.rootVisualElement.panel);
                Assert.Same(panelA, sixth.runtimePanel);

                // A root that is on no panel reports none.
                Assert.NotNull(bare.rootVisualElement);
                Assert.Null(bare.runtimePanel);
            }
            finally
            {
                Object.DestroyImmediate(first.gameObject);
                Object.DestroyImmediate(fourth.gameObject);
                Object.DestroyImmediate(bare.gameObject);
                Object.DestroyImmediate(sixth.gameObject);
                Object.DestroyImmediate(selectable);
                Object.DestroyImmediate(settingsA);
                Object.DestroyImmediate(settingsB);
            }
        }
    }
}
