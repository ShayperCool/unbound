using System.Collections.Generic;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>GroupBox and radio grouping (Unity 6000.0.71f1, tests/projects/radiogroups).
    /// Controls outside the probe: a group box naming its own manager type, and a manager forgotten
    /// when its group box leaves the panel.</summary>
    public sealed class GroupBoxTests : System.IDisposable
    {
        readonly Panel m_Panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

        public void Dispose() => m_Panel.Dispose();

        sealed class FirstOnlyManager : IGroupManager
        {
            internal static readonly List<string> Calls = new List<string>();
            public void Init(IGroupBox groupBox) => Calls.Add("init");
            public IGroupBoxOption GetSelectedOption() => null;
            public void OnOptionSelectionChanged(IGroupBoxOption selectedOption) => Calls.Add("selected " + ((RadioButton)selectedOption).name);
            public void RegisterOption(IGroupBoxOption option) => Calls.Add("register " + ((RadioButton)option).name);
            public void UnregisterOption(IGroupBoxOption option) => Calls.Add("unregister " + ((RadioButton)option).name);
        }

        sealed class CustomBox : VisualElement, IGroupBox<FirstOnlyManager>
        {
            public void OnOptionAdded(IGroupBoxOption option) { }
            public void OnOptionRemoved(IGroupBoxOption option) { }
        }

        [Fact]
        public void TheTitleLabelIsMadeAndRemovedWithTheText()
        {
            var box = new GroupBox("Title");
            Assert.Equal("Title", box.text);
            Assert.IsType<Label>(box[0]);
            Assert.Contains(GroupBox.labelUssClassName, box[0].GetClasses());
            box.text = "";
            Assert.Null(box.text);
            Assert.Equal(0, box.childCount);
            Assert.Null(new GroupBox().text);
        }

        [Fact]
        public void AGroupBoxCanNameItsManager()
        {
            FirstOnlyManager.Calls.Clear();
            var box = new CustomBox();
            var a = new RadioButton { name = "a" };
            box.Add(a);
            m_Panel.visualTree.Add(box);
            a.value = true;
            Assert.Equal(new[] { "init", "register a", "selected a" }, FirstOnlyManager.Calls);
            Assert.IsType<FirstOnlyManager>(((IGroupBox)box).GetGroupManager());
            box.RemoveFromHierarchy();
            Assert.Null(((IGroupBox)box).GetGroupManager());
            Assert.Contains("unregister a", FirstOnlyManager.Calls);
        }

        [Fact]
        public void ChoicesCanBeReadWhileTheValueChanges()
        {
            var group = new RadioButtonGroup("G", new List<string> { "a", "b", "c" });
            m_Panel.visualTree.Add(group);
            int i = 0;
            foreach (var choice in group.choices)
            {
                group.value = i;
                foreach (var _ in group.choices) { }
                i++;
            }
            Assert.Equal(3, i);
            Assert.Equal(2, group.value);
        }

        [Fact]
        public void AGroupLeavingItsPanelForgetsItsValueUntilItsCheckedButtonReturns()
        {
            var group = new RadioButtonGroup("G", new List<string> { "a", "b" });
            m_Panel.visualTree.Add(group);
            group.value = 1;
            group.RemoveFromHierarchy();
            Assert.Equal(-1, group.value);
            m_Panel.visualTree.Add(group);
            Assert.Equal(1, group.value);
        }

        [Fact]
        public void ButtonsJoinTheirPanelsGroupOnlyWhileAttached()
        {
            var a = new RadioButton();
            var b = new RadioButton();
            var holder = new VisualElement();
            holder.Add(a);
            holder.Add(b);
            a.value = true;
            b.value = true;
            Assert.True(a.value);
            m_Panel.visualTree.Add(holder);
            a.value = false;
            a.value = true;
            Assert.False(b.value);
            Assert.Same(a, ((IGroupBox)m_Panel).GetSelectedOption());
        }
    }
}
