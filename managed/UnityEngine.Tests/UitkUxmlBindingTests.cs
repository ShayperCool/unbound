using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.UIElements;
using Unity.Properties;
using UnityEngine.UIElements;
using Xunit;

// Bindings declared in UXML (<Bindings>) and bindings to style properties. Each test names its source:
//   [doc]      Unity 6000.0 Manual "Define data binding in UXML" and "Runtime data binding" (bind to
//              `style.*` properties); Scripting API DataBinding, Binding, ConverterGroups;
//   [measured] tests/projects/uitkbindings (Unity 6000.0.71f1 Editor).
namespace UxmlBindingProbes
{
    /// <summary>A game binding declared in UXML with a [UxmlAttribute] member.</summary>
    public class PrefixBinding : CustomBinding
    {
        [UxmlAttribute]
        public string prefix { get; set; }

        protected internal override BindingResult Update(in BindingContext context)
        {
            ((Label)context.targetElement).text = prefix + context.dataSourcePath;
            return new BindingResult(BindingStatus.Success);
        }
    }
}

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkUxmlBindingTests
    {
        sealed class Source
        {
            public string message = "hi";
            public int count = 4;
            public float width = 40;
            public Length length = new Length(25, LengthUnit.Percent);
            public Color color = Color.red;
            public string text = "12px";
        }

        static Panel NewPanel() => new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

        static void Update(Panel panel) => panel.uaDataBinding.Update();

        static VisualElement Clone(string body)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "UxmlBindingTests";
            UxmlImporter.Import("Assets/UI/UxmlBindingTests.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n" + body + "\n</ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            return root;
        }

        static List<string> Logged(Action action)
        {
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type != LogType.Log) lock (logs) logs.Add(type + " " + message);
            };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return logs;
        }

        static string Ids(VisualElement e) => string.Join(",", e.GetBindingInfos().Select(i => i.bindingId.ToString()));

        // [doc] the DataBinding attributes become the binding's properties; [measured] the binding
        // is set under its `property`.
        [Fact]
        public void BindingsDeclaredInUxmlAreSetWithTheirAttributes()
        {
            var root = Clone("<ui:Label name=\"l\"><Bindings><ui:DataBinding property=\"text\" data-source-path=\"inner.value\" binding-mode=\"ToTargetOnce\" update-trigger=\"WhenDirty\" /></Bindings></ui:Label>");
            var label = (Label)root.Q("l");
            var binding = Assert.IsType<DataBinding>(label.GetBinding("text"));
            Assert.Equal("inner.value", binding.dataSourcePath.ToString());
            Assert.Equal(BindingMode.ToTargetOnce, binding.bindingMode);
            Assert.Equal(BindingUpdateTrigger.WhenDirty, binding.updateTrigger);
            Assert.Equal(0, label.childCount);
        }

        // [measured] conv-order, conv-late: groups are looked up when the document is cloned, an
        // unknown id is skipped, a later group's converter replaces an earlier one's.
        [Fact]
        public void ConverterGroupsAreAppliedInOrderWhenCloned()
        {
            var first = new ConverterGroup("uxml-binding-tests-first");
            first.AddConverter((ref int v) => "first " + v);
            ConverterGroups.RegisterConverterGroup(first);
            var second = new ConverterGroup("uxml-binding-tests-second");
            second.AddConverter((ref int v) => "second " + v);
            ConverterGroups.RegisterConverterGroup(second);
            var root = Clone("<ui:Label name=\"order\"><Bindings><ui:DataBinding property=\"text\" data-source-path=\"count\" source-to-ui-converters=\"uxml-binding-tests-missing, uxml-binding-tests-first ,uxml-binding-tests-second\" /></Bindings></ui:Label>"
                + "<ui:Label name=\"late\"><Bindings><ui:DataBinding property=\"text\" data-source-path=\"count\" source-to-ui-converters=\"uxml-binding-tests-late\" /></Bindings></ui:Label>");
            var late = new ConverterGroup("uxml-binding-tests-late");
            late.AddConverter((ref int v) => "late " + v);
            ConverterGroups.RegisterConverterGroup(late);
            var panel = NewPanel();
            root.dataSource = new Source();
            panel.visualTree.Add(root);
            Update(panel);
            Assert.Equal("second 4", ((Label)root.Q("order")).text);
            Assert.Equal("4", ((Label)root.Q("late")).text);
        }

        // [measured] NotBinding, UnknownType: a UXML object that is not a binding is dropped
        // silently, a tag naming no UXML object type is reported at import; the element's other
        // bindings are kept.
        [Fact]
        public void OtherObjectsAreDroppedAndUnknownTagsReported()
        {
            VisualElement root = null;
            var logs = Logged(() => root = Clone("<ui:Label name=\"l\">\n    <Bindings>\n        <ui:Column name=\"c\" />\n        <NoSuchBinding property=\"tooltip\" />\n        <ui:DataBinding property=\"text\" data-source-path=\"message\" />\n    </Bindings>\n</ui:Label>"));
            Assert.Equal(new[] { "Error  (5,10): Semantic - Uxml object has an invalid child element: NoSuchBinding" }, logs);
            var label = root.Q("l");
            Assert.Equal("text", Ids(label));
            Assert.Equal(0, label.childCount);
        }

        // [measured] NoProperty: a binding without `property` is listed under the empty id until
        // the panel's update reports it and drops it.
        [Fact]
        public void ABindingWithoutPropertyIsReportedByTheFirstUpdate()
        {
            var root = Clone("<ui:Label name=\"no-property\"><Bindings><ui:DataBinding data-source-path=\"message\" /></Bindings></ui:Label>");
            var label = root.Q("no-property");
            Assert.Equal("", Ids(label));
            var panel = NewPanel();
            panel.visualTree.Add(root);
            var logs = Logged(() => Update(panel));
            Assert.Equal(new[] { "Error [UI Toolkit] Trying to set a binding on `no-property (Label)` without setting the \"property\" attribute is not supported (" + panel.visualTree.name + ")." }, logs);
            Assert.Empty(label.GetBindingInfos());
            Assert.Empty(Logged(() => Update(panel)));
        }

        // [measured] describe/styled vs initial/styled: bindings not processed yet are listed newest
        // first, processed ones in the order they were set; a removed element's bindings are requests
        // again.
        [Fact]
        public void BindingRequestsAreListedNewestFirstUntilProcessed()
        {
            var panel = NewPanel();
            var e = new VisualElement();
            panel.visualTree.Add(e);
            e.SetBinding("style.width", new DataBinding());
            e.SetBinding("style.height", new DataBinding());
            e.SetBinding("tooltip", new DataBinding());
            Assert.Equal("tooltip,style.height,style.width", Ids(e));
            Update(panel);
            Assert.Equal("style.width,style.height,tooltip", Ids(e));
            e.SetBinding("style.width", new DataBinding());
            Assert.Equal("style.width,style.height,tooltip", Ids(e));
            e.SetBinding("name", new DataBinding());
            Assert.Equal("name,style.width,style.height,tooltip", Ids(e));
            Update(panel);
            Assert.Equal("style.height,tooltip,style.width,name", Ids(e));
            e.RemoveFromHierarchy();
            Assert.Equal("name,style.width,tooltip,style.height", Ids(e));
        }

        // [measured] custom: a game binding's [UxmlAttribute] members and the base attributes.
        [Fact]
        public void GameBindingsReadTheirUxmlAttributes()
        {
            var root = Clone("<ui:Label name=\"l\" data-source-path=\"message\"><Bindings><UxmlBindingProbes.PrefixBinding property=\"text\" prefix=\"P:\" update-trigger=\"EveryUpdate\" /></Bindings></ui:Label>");
            var label = (Label)root.Q("l");
            var binding = Assert.IsType<UxmlBindingProbes.PrefixBinding>(label.GetBinding("text"));
            Assert.Equal("P:", binding.prefix);
            Assert.Equal(BindingUpdateTrigger.EveryUpdate, binding.updateTrigger);
            var panel = NewPanel();
            root.dataSource = new Source();
            panel.visualTree.Add(root);
            Update(panel);
            Assert.Equal("P:message", label.text);
        }

        // [measured] box, inner-parent: an element's data-source-path and data-source-type.
        [Fact]
        public void ElementDataSourceAttributesAreRead()
        {
            var root = Clone("<ui:VisualElement name=\"e\" data-source-path=\"inner\" data-source-type=\"UnityEngine.Color\" />");
            var e = root.Q("e");
            Assert.Equal("inner", e.dataSourcePath.ToString());
            Assert.Equal(typeof(Color), e.dataSourceType);
        }

        // [doc] binding to style properties; [measured] styled, code-*: plain values, Length and
        // StyleKeyword convert to style values, USS names reach the same property, `.value` reaches
        // into the style value, a string does not convert.
        [Fact]
        public void StylePropertiesAreBindingTargets()
        {
            var panel = NewPanel();
            var source = new Source();
            var box = new VisualElement { dataSource = source };
            panel.visualTree.Add(box);
            VisualElement Bound(string property, string path)
            {
                var e = new VisualElement();
                e.SetBinding(property, new DataBinding { dataSourcePath = new PropertyPath(path), bindingMode = BindingMode.ToTarget });
                box.Add(e);
                return e;
            }
            var width = Bound("style.width", "width");
            var height = Bound("style.height", "count");
            var color = Bound("style.background-color", "color");
            var sub = Bound("style.width.value", "length");
            var text = Bound("style.width", "text");
            var resolved = Bound("resolvedStyle.width", "width");
            Update(panel);
            Assert.Equal(new StyleLength(40), width.style.width);
            Assert.Equal(new StyleLength(4), height.style.height);
            Assert.Equal(new StyleColor(Color.red), color.style.backgroundColor);
            Assert.Equal(new Length(25, LengthUnit.Percent), sub.style.width.value);
            Assert.Equal(StyleKeyword.Null, text.style.width.keyword);
            Assert.True(text.TryGetLastBindingToUIResult("style.width", out var failure));
            Assert.Equal("[UI Toolkit] Could not set value for target of type '<b>VisualElement</b>' at path '<b>style.width</b>': could not convert from type '<b>String</b>' to type '<b>StyleLength</b>'.", failure.message);
            Assert.True(resolved.TryGetLastBindingToUIResult("resolvedStyle.width", out var readOnly));
            Assert.Equal(BindingStatus.Failure, readOnly.status);
            Assert.EndsWith("the path is read-only.", readOnly.message);
            source.width = 55;
            Update(panel);
            Assert.Equal(new StyleLength(55), width.style.width);
        }

        // [measured] code-tosource: a style value written to the source converts back to its plain
        // value (StyleLength to float).
        [Fact]
        public void StyleValuesConvertBackToPlainValues()
        {
            Assert.True(ConverterGroups.TryConvert(ref Unsafe(new StyleLength(33)), out float f));
            Assert.Equal(33f, f);
            Assert.True(ConverterGroups.TryConvert(ref Unsafe(StyleKeyword.Auto), out StyleLength auto));
            Assert.Equal(StyleKeyword.Auto, auto.keyword);
            Assert.True(ConverterGroups.TryConvert(ref Unsafe(3), out StyleFloat fromInt));
            Assert.Equal(new StyleFloat(3), fromInt);
        }

        static ref T Unsafe<T>(T value)
        {
            var box = new T[] { value };
            return ref box[0];
        }
    }
}
