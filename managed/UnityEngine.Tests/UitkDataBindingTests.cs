using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Properties;
using UnityEngine.UIElements;
using Xunit;

// Runtime data binding. [doc] Unity 6000.0 Scripting API "DataBinding" (bindingMode, updateTrigger,
// dataSource, dataSourcePath, converters), "VisualElement.SetBinding", "dataSource",
// "dataSourcePath", "CustomBinding", "INotifyBindablePropertyChanged", Manual "Runtime data binding"
// (hierarchical data source and path, binding modes and update triggers). The collections probe
// (tests/projects/uitkcollections, binding section) records the Editor's behaviour.
namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkDataBindingTests
    {
        sealed class Source
        {
            public string message = "hi";
            public int count = 3;
            public Inner inner = new Inner();
        }

        sealed class Inner
        {
            public string value = "inner";
        }

        sealed class Notifying : INotifyBindablePropertyChanged
        {
            public string message = "start";
            public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

            public void Notify(string name) => propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(name));
        }

        sealed class CountingBinding : CustomBinding
        {
            public readonly List<string> calls = new List<string>();

            protected internal override BindingResult Update(in BindingContext context)
            {
                calls.Add(context.bindingId + "@" + context.dataSourcePath);
                ((Label)context.targetElement).text = "custom " + calls.Count;
                return new BindingResult(BindingStatus.Success);
            }
        }

        static Panel NewPanel() => new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

        static void Update(Panel panel) => panel.uaDataBinding.Update();

        [Fact]
        public void ToTargetCopiesTheSourceValueEveryUpdateFromAPlainSource()
        {
            var panel = NewPanel();
            var label = new Label();
            var source = new Source();
            label.dataSource = source;
            label.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("message"), bindingMode = BindingMode.ToTarget });
            panel.visualTree.Add(label);
            Update(panel);
            Assert.Equal("hi", label.text);
            source.message = "changed";
            Update(panel);
            Assert.Equal("changed", label.text);
        }

        [Fact]
        public void TheDataSourceAndPathComeFromTheHierarchy()
        {
            var panel = NewPanel();
            var parent = new VisualElement { dataSource = new Source(), dataSourcePath = new PropertyPath("inner") };
            var label = new Label();
            label.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("value") });
            parent.Add(label);
            panel.visualTree.Add(parent);
            Update(panel);
            Assert.Equal("inner", label.text);
            var context = label.GetDataSourceContext("text");
            Assert.Equal("inner.value", context.dataSourcePath.ToString());
        }

        [Fact]
        public void NotifyingSourcesUpdateOnlyAfterAChangeOnThePath()
        {
            var panel = NewPanel();
            var source = new Notifying();
            var label = new Label { dataSource = source };
            label.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("message") });
            panel.visualTree.Add(label);
            Update(panel);
            Assert.Equal("start", label.text);
            source.message = "silent";
            Update(panel);
            Assert.Equal("start", label.text);
            source.Notify("other");
            Update(panel);
            Assert.Equal("start", label.text);
            source.Notify("message");
            Update(panel);
            Assert.Equal("silent", label.text);
        }

        [Fact]
        public void ToTargetOnceUpdatesOnceUntilMarkedDirty()
        {
            var panel = NewPanel();
            var source = new Source();
            var label = new Label { dataSource = source };
            var binding = new DataBinding { dataSourcePath = new PropertyPath("message"), bindingMode = BindingMode.ToTargetOnce };
            label.SetBinding("text", binding);
            panel.visualTree.Add(label);
            Update(panel);
            source.message = "later";
            Update(panel);
            Assert.Equal("hi", label.text);
            binding.MarkDirty();
            Update(panel);
            Assert.Equal("later", label.text);
        }

        [Fact]
        public void TwoWayWritesUiChangesToTheSource()
        {
            var panel = NewPanel();
            var source = new Source();
            var field = new TextField { dataSource = source };
            field.SetBinding("value", new DataBinding { dataSourcePath = new PropertyPath("message") });
            panel.visualTree.Add(field);
            Update(panel);
            Assert.Equal("hi", field.value);
            field.value = "typed";
            Update(panel);
            Assert.Equal("typed", source.message);
            // ToTarget does not write back.
            field.SetBinding("value", new DataBinding { dataSourcePath = new PropertyPath("message"), bindingMode = BindingMode.ToTarget });
            Update(panel);
            field.value = "ignored";
            Update(panel);
            Assert.Equal("typed", source.message);
            Assert.Equal("typed", field.value);
        }

        [Fact]
        public void ConvertersChangeTheValueOnTheWay()
        {
            var panel = NewPanel();
            var label = new Label { dataSource = new Source() };
            var binding = new DataBinding { dataSourcePath = new PropertyPath("count"), bindingMode = BindingMode.ToTarget };
            binding.sourceToUiConverters.AddConverter((ref int v) => "n=" + v);
            label.SetBinding("text", binding);
            panel.visualTree.Add(label);
            Update(panel);
            Assert.Equal("n=3", label.text);
        }

        [Fact]
        public void CustomBindingsUpdateEveryUpdateWithTheirContext()
        {
            var panel = NewPanel();
            var parent = new VisualElement { dataSource = new Source(), dataSourcePath = new PropertyPath("inner") };
            var label = new Label();
            var binding = new CountingBinding();
            label.SetBinding("text", binding);
            parent.Add(label);
            panel.visualTree.Add(parent);
            Update(panel);
            Update(panel);
            Assert.Equal(new[] { "text@inner", "text@inner" }, binding.calls);
            Assert.Equal("custom 2", label.text);
        }

        [Fact]
        public void BindingsWaitForASourceAndForThePanel()
        {
            var panel = NewPanel();
            var label = new Label();
            label.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("message") });
            Assert.True(label.HasBinding("text"));
            Assert.False(label.TryGetLastBindingToUIResult("text", out _));
            panel.visualTree.Add(label);
            Binding.SetPanelLogLevel(panel, BindingLogLevel.None);
            Update(panel);
            Assert.True(label.TryGetLastBindingToUIResult("text", out var result));
            Assert.Equal(BindingStatus.Pending, result.status);
            label.dataSource = new Source();
            Update(panel);
            Assert.Equal("hi", label.text);
            label.ClearBinding("text");
            Assert.False(label.HasBinding("text"));
            ((Source)label.dataSource).message = "after";
            Update(panel);
            Assert.Equal("hi", label.text);
            Assert.Empty(label.GetBindingInfos());
        }
    }
}
