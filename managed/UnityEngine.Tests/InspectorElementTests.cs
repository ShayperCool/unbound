using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>InspectorElement and the panel's phase updater (tests/projects/inspectorelement
    /// records the Editor's): the default inspector, custom editors' UI Toolkit and IMGUI GUI, the
    /// lifetime of the editor it makes, rebinding, and one panel phase at a time.</summary>
    public class InspectorElementTests
    {
        public class Inspected : ScriptableObject
        {
            public int count = 3;
            public string title = "t";
            [HideInInspector] public int secret = 4;
        }

        public class Other : ScriptableObject { public float speed = 2f; }

        public class UieEditor : Editor
        {
            internal static int disabled;
            void OnDisable() => disabled++;
            public override VisualElement CreateInspectorGUI() => new Label("custom") { name = "custom" };
        }

        public class ImguiEditor : Editor
        {
            public override void OnInspectorGUI() { }
        }

        static void Tick(BaseRuntimePanel panel)
        {
            panel.Update();
            panel.Repaint(null);
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        sealed class Fixture : System.IDisposable
        {
            internal readonly PanelSettings Settings = ScriptableObject.CreateInstance<PanelSettings>();
            internal readonly Inspected Target = ScriptableObject.CreateInstance<Inspected>();
            internal readonly BaseRuntimePanel Panel;

            internal Fixture() => Panel = RuntimePanel.Create(Settings);

            internal T Show<T>(T element) where T : VisualElement
            {
                Panel.visualTree.Add(element);
                Tick(Panel);
                return element;
            }

            public void Dispose()
            {
                Panel.Dispose();
                Object.DestroyImmediate(Target);
                Object.DestroyImmediate(Settings);
            }
        }

        static string[] Names(VisualElement e) => e.Children().Select(c => c.name).ToArray();

        [Fact]
        public void AnObjectsInspectorIsItsGenericInspectorsPropertyFields()
        {
            using var f = new Fixture();
            var inspector = new InspectorElement(f.Target);
            // Built when made, before it is on a panel.
            Assert.IsType<GenericInspector>(inspector.editor);
            Assert.Equal(3, inspector.hierarchy.childCount);
            f.Show(inspector);
            Assert.Equal(new[] { "unity-inspector-element", "unity-inspector-element--uie-default", "unity-inspector-element--uie" }, inspector.GetClasses().ToArray());
            var root = inspector.hierarchy[2];
            Assert.Equal(new[] { "PropertyField:m_Script", "PropertyField:count", "PropertyField:title" }, Names(root));
            Assert.False(root[0].enabledSelf);
            Assert.True(root[1].enabledSelf);
            Assert.True(root[1].ClassListContains(PropertyField.inspectorElementUssClassName));
            Assert.Equal(3, root[1].Q<IntegerField>().value);
            var count = root[1];
            count.RemoveFromHierarchy();
            Assert.False(count.ClassListContains(PropertyField.inspectorElementUssClassName));
        }

        [Fact]
        public void APropertyFieldOutsideAnInspectorIsNoInspectorProperty()
        {
            using var f = new Fixture();
            var container = new VisualElement();
            InspectorElement.FillDefaultInspector(container, new SerializedObject(f.Target), null);
            f.Show(container);
            Assert.Equal(new[] { "PropertyField:m_Script", "PropertyField:count", "PropertyField:title" }, Names(container));
            Assert.All(container.Children(), c => Assert.False(c.ClassListContains(PropertyField.inspectorElementUssClassName)));
            var empty = new VisualElement();
            InspectorElement.FillDefaultInspector(empty, null, null);
            Assert.Equal(0, empty.childCount);
        }

        [Fact]
        public void ACustomEditorsGUIIsTheInspectorsAndItsIMGUIIsReported()
        {
            // The IMGUI container's measured classes.
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var f = new Fixture();
            var uieEditor = Editor.CreateEditor(f.Target, typeof(UieEditor));
            var uie = f.Show(new InspectorElement(uieEditor));
            Assert.True(uie.ClassListContains(InspectorElement.uIECustomVariantUssClassName));
            Assert.True(uie.ClassListContains(InspectorElement.uIEInspectorVariantUssClassName));
            var custom = uie.Q<Label>("custom");
            Assert.True(custom.ClassListContains(InspectorElement.customInspectorUssClassName));

            int reported = Hits("UnityEditor.UIElements.InspectorElement (IMGUI of ImguiEditor)");
            var imguiEditor = Editor.CreateEditor(f.Target, typeof(ImguiEditor));
            var imgui = f.Show(new InspectorElement(imguiEditor));
            var container = Assert.IsType<IMGUIContainer>(imgui.hierarchy[2]);
            Assert.Equal(new[] { "unity-imgui-container", InspectorElement.iMGUIContainerUssClassName, InspectorElement.customInspectorUssClassName }, container.GetClasses().ToArray());
            Assert.Equal(Overflow.Visible, container.style.overflow.value);
            Assert.True(imgui.ClassListContains(InspectorElement.iMGUICustomVariantUssClassName));
            Assert.True(imgui.ClassListContains(InspectorElement.iMGUIInspectorVariantUssClassName));
            Assert.Equal(reported + 1, Hits("UnityEditor.UIElements.InspectorElement (IMGUI of ImguiEditor)"));
            Object.DestroyImmediate(uieEditor);
            Object.DestroyImmediate(imguiEditor);
        }

        [Fact]
        public void TheEditorAnInspectorMadeIsDestroyedWhenItLeavesItsPanelAndAGivenOneIsNot()
        {
            using var f = new Fixture();
            var owned = f.Show(new InspectorElement(f.Target));
            var made = owned.editor;
            owned.RemoveFromHierarchy();
            Assert.Null(owned.editor);
            Assert.True(made == null);
            // Back on a panel, it is built again.
            f.Show(owned);
            Assert.IsType<GenericInspector>(owned.editor);

            int disabled = UieEditor.disabled;
            var given = Editor.CreateEditor(f.Target, typeof(UieEditor));
            var byEditor = f.Show(new InspectorElement(given));
            Assert.Same(given, byEditor.editor);
            byEditor.RemoveFromHierarchy();
            Assert.True(given != null);
            Assert.Equal(disabled, UieEditor.disabled);
            Object.DestroyImmediate(given);
        }

        [Fact]
        public void BindingAnotherObjectRebuildsTheInspector()
        {
            using var f = new Fixture();
            var other = ScriptableObject.CreateInstance<Other>();
            var inspector = f.Show(new InspectorElement());
            Assert.Null(inspector.editor);
            Assert.Equal(new[] { "unity-inspector-element" }, inspector.GetClasses().ToArray());
            inspector.Bind(new SerializedObject(f.Target));
            Tick(f.Panel);
            Assert.Equal("PropertyField:count", inspector.hierarchy[2][1].name);
            var first = inspector.editor;
            inspector.Bind(new SerializedObject(other));
            Tick(f.Panel);
            Assert.True(first == null);
            Assert.Equal(new[] { "PropertyField:m_Script", "PropertyField:speed" }, Names(inspector.hierarchy[2]));
            Assert.Equal(3, inspector.hierarchy.childCount);
            Object.DestroyImmediate(other);
        }

        [Fact]
        public void AnotherObjectOfTheSameTypeKeepsTheGUIAndAnInspectorKeepsItsOwnObject()
        {
            using var f = new Fixture();
            var second = ScriptableObject.CreateInstance<Inspected>();
            second.count = 8;
            var inspector = f.Show(new InspectorElement(f.Target));
            var gui = inspector.hierarchy[2];
            var editor = inspector.editor;
            inspector.Bind(new SerializedObject(second));
            Tick(f.Panel);
            Assert.Same(gui, inspector.hierarchy[2]);
            Assert.NotSame(editor, inspector.editor);
            Assert.Equal(8, inspector.Q<IntegerField>().value);

            var other = ScriptableObject.CreateInstance<Other>();
            var holder = new VisualElement();
            var own = new InspectorElement(new SerializedObject(f.Target));
            holder.Add(own);
            f.Show(holder);
            holder.Bind(new SerializedObject(other));
            Tick(f.Panel);
            Assert.Equal("PropertyField:count", own.hierarchy[2][1].name);
            Assert.Equal(3, own.Q<IntegerField>().value);
            Object.DestroyImmediate(second);
            Object.DestroyImmediate(other);
        }

        public class FilterBase : MonoBehaviour
        {
            void OnAudioFilterRead(float[] data, int channels) { }
        }

        // Its filter is its base class's private one.
        public class AudioFiltered : FilterBase { public int gain = 1; }

        [Fact]
        public void AnEngineTypeWithoutItsEditorAndAnAudioFiltersMeterAreReported()
        {
            using var f = new Fixture();
            var engineObject = ScriptableObject.CreateInstance<PanelSettings>();
            string member = "UnityEditor.GenericInspector (inspector of UnityEngine.UIElements.PanelSettings)";
            int before = Hits(member);
            Assert.Null(Editor.CreateEditor(engineObject).CreateInspectorGUI());
            Assert.Equal(before + 1, Hits(member));
            var inspector = f.Show(new InspectorElement(engineObject));
            Assert.Equal(before + 2, Hits(member));
            Assert.Equal(2, inspector.hierarchy.childCount);
            Assert.Equal(new[] { "unity-inspector-element" }, inspector.GetClasses().ToArray());
            // Rebound to a script object and back.
            inspector.Bind(new SerializedObject(f.Target));
            Tick(f.Panel);
            Assert.Equal("PropertyField:count", inspector.hierarchy[2][1].name);
            inspector.Bind(new SerializedObject(engineObject));
            Tick(f.Panel);
            Assert.Equal(2, inspector.hierarchy.childCount);
            Assert.Equal(new[] { "unity-inspector-element" }, inspector.GetClasses().ToArray());
            Object.DestroyImmediate(engineObject);

            var go = new GameObject("filtered");
            var filtered = go.AddComponent<AudioFiltered>();
            int meter = Hits("UnityEditor.GenericInspector (OnAudioFilterRead level meter)");
            var gui = Editor.CreateEditor(filtered).CreateInspectorGUI();
            Assert.Equal(meter + 1, Hits("UnityEditor.GenericInspector (OnAudioFilterRead level meter)"));
            Assert.Equal("PropertyField:gain", gui[1].name);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void TheIMGUIDefaultInspectorIsTheGenericInspectorsIMGUIReportedAndRebuiltOnEachBind()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var f = new Fixture();
            string member = "UnityEditor.UIElements.InspectorElement (IMGUI of GenericInspector)";
            int before = Hits(member);
            var inspector = f.Show(new InspectorElement(new SerializedObject(f.Target), InspectorElement.DefaultInspectorFramework.IMGUI));
            Assert.Equal(new[] { "unity-inspector-element", InspectorElement.iMGUIDefaultVariantUssClassName, InspectorElement.iMGUIInspectorVariantUssClassName }, inspector.GetClasses().ToArray());
            var container = Assert.IsType<IMGUIContainer>(inspector.hierarchy[2]);
            Assert.False(container.ClassListContains(InspectorElement.customInspectorUssClassName));
            Assert.Equal(before + 1, Hits(member));
            inspector.Bind(new SerializedObject(f.Target));
            Tick(f.Panel);
            Assert.Same(container, inspector.hierarchy[2]);
            Assert.Equal(3, inspector.hierarchy.childCount);
            Assert.Equal(before + 2, Hits(member));
        }

        [Fact]
        public void AFieldBoundOffItsPanelTakesItsValueInTheBindingsPhase()
        {
            using var f = new Fixture();
            var updater = (VisualTreeUpdater)typeof(Panel).GetField("m_VisualTreeUpdater", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.Panel);
            f.Target.count = 21;
            var field = new IntegerField { bindingPath = "count" };
            field.Bind(new SerializedObject(f.Target));
            f.Panel.visualTree.Add(field);
            Assert.Equal(0, field.value);
            updater.UpdateVisualTreePhase(VisualTreeUpdatePhase.Bindings);
            Assert.Equal(21, field.value);
        }

        [Fact]
        public void ThePanelUpdatesOnePhaseAtATime()
        {
            using var f = new Fixture();
            var field = typeof(Panel).GetField("m_VisualTreeUpdater", BindingFlags.Instance | BindingFlags.NonPublic);
            var updater = (VisualTreeUpdater)field.GetValue(f.Panel);
            var box = new VisualElement();
            int geometry = 0;
            box.RegisterCallback<GeometryChangedEvent>(_ => geometry++);
            box.style.width = 123f;
            f.Panel.visualTree.Add(box);
            updater.UpdateVisualTreePhase(VisualTreeUpdatePhase.Styles);
            Assert.True(float.IsNaN(box.layout.width));
            updater.UpdateVisualTreePhase(VisualTreeUpdatePhase.Layout);
            Assert.Equal(123f, box.layout.width);
            Assert.Equal(1, geometry);
            box.style.width = 77f;
            updater.UpdateVisualTreePhase(VisualTreeUpdatePhase.Layout);
            Assert.Equal(77f, box.layout.width);
            box.style.width = 200f;
            updater.UpdateVisualTreePhase(VisualTreeUpdatePhase.TransformClip);
            Assert.Equal(77f, box.layout.width);
            updater.UpdateVisualTree();
            Assert.Equal(200f, box.layout.width);
            Assert.Equal(3, geometry);
            Assert.Throws<System.IndexOutOfRangeException>(() => updater.UpdateVisualTreePhase(VisualTreeUpdatePhase.Count));
        }
    }
}
