using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditor.UIElements.Bindings;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A ListView bound to an array or list SerializedProperty: its controller's edits,
    /// reordering, foldout states, multi-object editing and drops.</summary>
    public class ListViewBindingTests
    {
        [Serializable]
        public class Nest
        {
            public InnerData inner;
            public int n;
        }

        public class ListSample : ScriptableObject
        {
            public List<int> numbers = new List<int> { 1, 2, 3, 4 };
            [NonReorderable] public List<int> fixedOrder = new List<int> { 5, 6 };
            public List<Nest> nests = new List<Nest> { new Nest { n = 0 }, new Nest { n = 1 }, new Nest { n = 2 } };
            public List<Object> objects = new List<Object>();
        }

        static void Tick(BaseRuntimePanel panel)
        {
            panel.Update();
            panel.Repaint(null);
        }

        sealed class Fixture : IDisposable
        {
            internal readonly PanelSettings Settings = ScriptableObject.CreateInstance<PanelSettings>();
            internal readonly List<ListSample> Targets = new List<ListSample>();
            internal readonly BaseRuntimePanel Panel;
            internal readonly SerializedObject Serialized;

            internal Fixture(int targets = 1)
            {
                for (int i = 0; i < targets; i++) Targets.Add(ScriptableObject.CreateInstance<ListSample>());
                Panel = RuntimePanel.Create(Settings);
                Serialized = new SerializedObject(Targets.Cast<Object>().ToArray());
            }

            internal ListView Bind(string path)
            {
                var property = Serialized.FindProperty(path);
                property.isExpanded = true;
                var field = new PropertyField(property);
                field.style.width = 400f;
                field.style.height = 400f;
                Panel.visualTree.Add(field);
                field.Bind(Serialized);
                Tick(Panel);
                Tick(Panel);
                return Assert.IsType<ListView>(field.hierarchy[0]);
            }

            public void Dispose()
            {
                Serialized.Dispose();
                Panel.Dispose();
                foreach (var target in Targets) Object.DestroyImmediate(target);
                Object.DestroyImmediate(Settings);
            }
        }

        [Fact]
        public void ControllerRemovesListedItemsAndClearsReportingEveryIndexButTheLast()
        {
            using var f = new Fixture();
            var list = f.Bind("numbers");
            var removed = new List<int[]>();
            list.itemsRemoved += indices => removed.Add(indices.ToArray());

            list.viewController.RemoveItems(new List<int> { 0, 2 });
            Tick(f.Panel);
            Assert.Equal(new[] { 2, 4 }, f.Targets[0].numbers);

            list.viewController.ClearItems();
            Tick(f.Panel);
            Assert.Empty(f.Targets[0].numbers);
            Assert.Equal(new[] { 0 }, removed.Last());

            // An empty list's range of removed indices is invalid before anything changes.
            Assert.Throws<ArgumentOutOfRangeException>(() => list.viewController.ClearItems());
        }

        [Fact]
        public void PropertyFieldListsAreReorderableUnlessTheirFieldIsNonReorderable()
        {
            using var f = new Fixture();
            var numbers = f.Bind("numbers");
            var fixedOrder = f.Bind("fixedOrder");
            Assert.Equal(ListViewReorderMode.Animated, numbers.reorderMode);
            Assert.True(numbers.reorderable);
            Assert.Equal(ListViewReorderMode.Simple, fixedOrder.reorderMode);
            Assert.False(fixedOrder.reorderable);
        }

        [Fact]
        public void DropsOverAnItemAreRejected()
        {
            using var f = new Fixture();
            var list = f.Bind("numbers");
            var controller = new SerializedObjectListReorderableDragAndDropController(list);
            Assert.Throws<ArgumentException>(() => controller.OnDrop(new DragAndDropArgs { dragAndDropPosition = DragAndDropPosition.OverItem }));
        }

        [Fact]
        public void DraggingItemsBetweenRowsReordersTheBoundArray()
        {
            using var f = new Fixture();
            var list = f.Bind("numbers");
            var controller = new SerializedObjectListReorderableDragAndDropController(list) { enableReordering = true };
            controller.SetupDragAndDrop(new[] { list.viewController.GetIdForIndex(0) }, skipText: true);
            controller.OnDrop(new DragAndDropArgs { dragAndDropPosition = DragAndDropPosition.BetweenItems, insertAtIndex = 3 });
            Tick(f.Panel);
            Assert.Equal(new[] { 2, 3, 1, 4 }, f.Targets[0].numbers);
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        [Fact]
        public void ObjectsDroppedOnTheHeaderAreReportedAsUnsupported()
        {
            const string member = "UnityEditor.UIElements.Bindings.BaseListViewSerializedObjectBinding (objects dragged onto a bound ListView)";
            using var f = new Fixture();
            var list = f.Bind("objects");
            var header = list.headerFoldout.toggle.visualInput;
            var previous = DragAndDrop.objectReferences;
            try
            {
                int before = Hits(member);
                DragAndDrop.objectReferences = Array.Empty<Object>();
                using (var empty = DragPerformEvent.GetPooled())
                {
                    empty.target = header;
                    header.SendEvent(empty);
                }
                Tick(f.Panel);
                Assert.Equal(before, Hits(member));

                DragAndDrop.objectReferences = new Object[] { f.Settings };
                using (var drop = DragPerformEvent.GetPooled())
                {
                    drop.target = header;
                    header.SendEvent(drop);
                }
                Tick(f.Panel);
                Assert.Equal(before + 1, Hits(member));
                Assert.Empty(f.Targets[0].objects);
            }
            finally
            {
                DragAndDrop.objectReferences = previous;
            }
        }

        [Fact]
        public void MovingAnElementMovesItsAndItsNestedFoldoutStates()
        {
            using var f = new Fixture();
            var nests = f.Serialized.FindProperty("nests");
            bool Expanded(string path) => f.Serialized.FindProperty("nests.Array.data[" + path).isExpanded;
            void Expand(string path, bool value) => f.Serialized.FindProperty("nests.Array.data[" + path).isExpanded = value;
            Expand("0]", true);
            Expand("0].inner", true);
            Expand("0].inner.v", false);
            Expand("1]", false);
            Expand("1].inner", false);
            Expand("1].inner.v", true);
            Expand("2]", false);
            Expand("2].inner", true);
            Expand("2].inner.v", true);

            var list = new SerializedObjectList(nests);
            list.Move(0, 2);
            f.Serialized.ApplyModifiedProperties();
            Assert.Equal(new[] { 1, 2, 0 }, f.Targets[0].nests.Select(n => n.n));
            Assert.Equal(new[] { false, false, true }, new[] { Expanded("0]"), Expanded("0].inner"), Expanded("0].inner.v") });
            Assert.Equal(new[] { false, true, true }, new[] { Expanded("1]"), Expanded("1].inner"), Expanded("1].inner.v") });
            Assert.Equal(new[] { true, true, false }, new[] { Expanded("2]"), Expanded("2].inner"), Expanded("2].inner.v") });
        }

        [Fact]
        public void MultiObjectListsOverTheEditLimitShowNoItemsAndDisableTheFooter()
        {
            using var f = new Fixture(targets: 2);
            f.Targets[1].numbers = new List<int> { 1, 2, 3 };
            f.Serialized.Update();
            f.Serialized.maxArraySizeForMultiEditing = 2;
            var list = f.Bind("numbers");
            Assert.Equal(0, list.itemsSource.Count);
            Assert.False(list.footer.enabledSelf);

            f.Serialized.maxArraySizeForMultiEditing = 10;
            var under = f.Bind("numbers");
            Assert.Equal(3, under.itemsSource.Count);
            Assert.True(under.footer.enabledSelf);
        }
    }
}
