using System;
using Unbound.Runtime;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DefaultUiStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultUiStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        sealed class Pointer : PointerEventBase<Pointer>
        {
            public Pointer() { }
            internal void SetTilt(Vector2 value) { tilt = value; }
            internal void SetAngles(float altitude, float azimuth) { altitudeAngle = altitude; azimuthAngle = azimuth; }
            internal void SetKind(string kind) { pointerType = kind; }
        }

        [Fact]
        public void PointerAnglesConvertOnceUntilTheirOwnSetterOrPoolReset()
        {
            var e = new Pointer();
            e.SetTilt(new Vector2(0.2f, -0.3f));
            Assert.Equal(1.21192384f, e.altitudeAngle); Assert.Equal(5.29248571f, e.azimuthAngle);
            e.SetTilt(new Vector2(-0.4f, 0.1f));
            Assert.Equal(1.21192384f, e.altitudeAngle); Assert.Equal(5.29248571f, e.azimuthAngle);
            e.SetAngles(0.7f, 1.2f);
            Assert.Equal(1.15914047f, e.altitudeAngle); Assert.Equal(2.90858889f, e.azimuthAngle);
            e.Dispose();
            var touch = Pointer.GetPooled(); touch.SetKind(UnityEngine.UIElements.PointerType.touch);
            Assert.True(float.IsNaN(touch.altitudeAngle)); Assert.True(float.IsNaN(touch.azimuthAngle));
            touch.Dispose();
            var reset = Pointer.GetPooled();
            Assert.Equal(Mathf.PI / 2f, reset.altitudeAngle); Assert.Equal(0f, reset.azimuthAngle); Assert.Equal(Vector2.zero, reset.tilt);
            reset.Dispose();
        }

        [Fact]
        public void PriorProfileRetainsIndependentPointerValues()
        {
            ProjectInfo.UnityVersion = "6000.0.70f1";
            var e = new Pointer(); e.SetAngles(0.7f, 1.2f); e.SetTilt(new Vector2(0.2f, -0.3f));
            Assert.Equal(0.7f, e.altitudeAngle); Assert.Equal(1.2f, e.azimuthAngle); Assert.Equal(new Vector2(0.2f, -0.3f), e.tilt);
            e.Dispose();
        }

        [Fact]
        public void AttributeDescriptionsKeepTheirStoredValuesAndMeasuredPresentation()
        {
            var color = new UxmlColorAttributeDescription();
            Assert.Equal(UxmlAttributeDescription.Use.Optional, color.use); Assert.Equal(Color.black, color.defaultValue);
            color.use = UxmlAttributeDescription.Use.Prohibited; Assert.Equal(UxmlAttributeDescription.Use.Prohibited, color.use);
            color.defaultValue = new Color(-0.25f, 0.5f, 1.5f, 0.125f);
            Assert.Equal("RGBA(-0.250, 0.500, 1.500, 0.125)", color.defaultValueAsString);
        }

        [Fact]
        public void TextExperimentalInterfaceOwnsTheSeparateRenderedText()
        {
            var label = new Label("logical");
            Assert.Same(label, label.experimental);
            label.experimental.SetRenderedText("painted");
            Assert.Equal("logical", label.text); Assert.Equal("painted", label.renderedTextString);
            label.text = "next"; Assert.Equal("next", label.renderedTextString);
        }

        [Fact]
        public void GuiContainerAndSliderKeepExplicitPropertyWrites()
        {
            var calls = 0; Action callback = () => calls++;
            using var container = new IMGUIContainer(callback);
            Assert.True(container.focusable); Assert.Equal(ContextType.Editor, container.contextType); Assert.Same(callback, container.onGUIHandler);
            Assert.Equal(0, calls);
            container.contextType = ContextType.Player; container.focusable = false; container.onGUIHandler = null;
            Assert.Equal(ContextType.Player, container.contextType); Assert.False(container.focusable); Assert.Null(container.onGUIHandler);
            var slider = new MinMaxSlider(); Assert.Equal(PickingMode.Ignore, slider.pickingMode);
            slider.pickingMode = PickingMode.Position; Assert.Equal(PickingMode.Position, slider.pickingMode);
        }

        [Fact]
        public void RadioChoicesAndAuthoredChildrenHaveSeparateContainers()
        {
            var group = new RadioButtonGroup("group", new System.Collections.Generic.List<string> { "a", "b" });
            var manual = new RadioButton(); group.Add(manual);
            Assert.NotSame(group, group.contentContainer); Assert.False(group.contentContainer.focusable);
            Assert.Equal("contentContainer", group.contentContainer.name); Assert.Equal(1, group.contentContainer.childCount);
            Assert.Equal(3, RadioButton.OptionsOf(group).Count);
            Assert.Same(group, manual.parent);
            group.choices = new[] { "c" };
            Assert.Same(group.contentContainer, manual.hierarchy.parent); Assert.Equal(1, group.contentContainer.childCount);
            Assert.Equal(2, RadioButton.OptionsOf(group).Count);
        }

        [Fact]
        public void AtlasFactoriesAndPanelSettingsOwnIndependentMutableData()
        {
            using var engine = MainThreadCheck.Engine();
            var empty = new DynamicAtlasSettings(); Assert.Equal(0, empty.minAtlasSize); Assert.Equal(DynamicAtlasFilters.None, empty.activeFilters);
            var first = DynamicAtlasSettings.defaults; var second = DynamicAtlasSettings.defaults;
            Assert.NotSame(first, second); Assert.Equal(64, first.minAtlasSize); Assert.Equal(4096, first.maxAtlasSize); Assert.Equal(64, first.maxSubTextureSize);
            first.minAtlasSize = -2; first.maxAtlasSize = 17; first.maxSubTextureSize = 3; first.activeFilters = (DynamicAtlasFilters)129;
            Assert.Equal(64, second.minAtlasSize); Assert.Equal(-2, first.minAtlasSize); Assert.Equal(17, first.maxAtlasSize);
            var json = JsonUtility.ToJson(first);
            Assert.Equal("{\"m_MinAtlasSize\":-2,\"m_MaxAtlasSize\":17,\"m_MaxSubTextureSize\":3,\"m_ActiveFilters\":129}", json);
            var restored = JsonUtility.FromJson<DynamicAtlasSettings>(json);
            Assert.Equal(-2, restored.minAtlasSize); Assert.Equal((DynamicAtlasFilters)129, restored.activeFilters);
            var a = ScriptableObject.CreateInstance<PanelSettings>(); var b = ScriptableObject.CreateInstance<PanelSettings>();
            try
            {
                Assert.NotSame(a.dynamicAtlasSettings, b.dynamicAtlasSettings);
                a.dynamicAtlasSettings = first; Assert.Same(first, a.dynamicAtlasSettings); Assert.Equal(64, b.dynamicAtlasSettings.minAtlasSize);
                a.dynamicAtlasSettings = null; Assert.Null(a.dynamicAtlasSettings);
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        sealed class ContentHost : VisualElement
        {
            readonly VisualElement container;
            public ContentHost() { container = new VisualElement(); hierarchy.Add(container); }
            public override VisualElement contentContainer => container;
        }

        [Fact]
        public void LogicalParentsFollowPublicAddsWhileHierarchyKeepsPhysicalParents()
        {
            var host = new ContentHost(); var child = new VisualElement(); host.Add(child);
            Assert.Same(host, child.parent); Assert.Same(host.contentContainer, child.hierarchy.parent);
            var inserted = new VisualElement(); host.Insert(0, inserted);
            Assert.Same(host, inserted.parent); Assert.Same(host.contentContainer, inserted.hierarchy.parent);
            var other = new ContentHost(); other.Add(child);
            Assert.Same(other, child.parent); Assert.Same(other.contentContainer, child.hierarchy.parent);
            Assert.Equal(1, host.childCount);
            child.RemoveFromHierarchy(); Assert.Null(child.parent); Assert.Null(child.hierarchy.parent);
            host.hierarchy.Add(child); Assert.Same(host, child.parent); Assert.Same(host, child.hierarchy.parent);
        }

        [Fact]
        public void PublicTouchFactoryRetainsTheMeasuredConversionRounding()
        {
            var touch = new Touch { altitudeAngle = .7f, azimuthAngle = 1.2f, phase = TouchPhase.Moved };
            using var e = PointerMoveEvent.GetPooled(touch);
            Assert.Equal(1.19387412f, e.altitudeAngle); Assert.Equal(.699999928f, e.azimuthAngle);
            Assert.Equal(new Vector2(.289028436f, .245410264f), e.tilt);
        }

        [Serializable] sealed class TextData { public string text; public string[] items; }

        [Fact]
        public void JsonStringTransferStopsAtEmbeddedNullWithoutChangingEscapedText()
        {
            var data = new TextData { text = "before\0after", items = new[] { "\0", "literal\\0" } };
            const string expected = "{\"text\":\"before\",\"items\":[\"\",\"literal\\\\0\"]}";
            Assert.Equal(expected, JsonUtility.ToJson(data)); Assert.Equal(expected, UnityEditor.EditorJsonUtility.ToJson(data));
            const string source = "{\"text\":\"before\\u0000after\"}";
            Assert.Equal("before", JsonUtility.FromJson<TextData>(source).text);
            JsonUtility.FromJsonOverwrite(source, data); Assert.Equal("before", data.text);
            data.text = "previous"; UnityEditor.EditorJsonUtility.FromJsonOverwrite(source, data); Assert.Equal("before", data.text);
            ProjectInfo.UnityVersion = "6000.0.70f1";
            Assert.Equal("before\0after", JsonUtility.FromJson<TextData>(source).text);
            Assert.Contains("\\u0000", JsonUtility.ToJson(new TextData { text = "before\0after" }));
        }
    }
}
