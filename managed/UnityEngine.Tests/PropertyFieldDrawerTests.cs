using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>PropertyField and a property's drawers (tests/projects/drawerdispatch records the
    /// Editor's): built-in and usage drawers, their change events, drawers that cannot be created,
    /// and what is reported instead of drawn.</summary>
    public class PropertyFieldDrawerTests
    {
        public class DrawerSample : ScriptableObject
        {
            [ColorUsage(false, true)] public Color tint = Color.red;
            [ColorUsage(false, true)] public Color[] tints = { Color.red, Color.blue };
            [GradientUsage(true, ColorSpace.Linear)] public List<Gradient> ramps = new List<Gradient> { new Gradient() };
            [Range(0f, 1f)] public float ratio = 0.5f;
            [Header("Section")] public int counted = 3;
            [Tooltip("plain")] public int described = 4;
        }

        [System.Serializable] public class ThrowingDrawn { public int a = 1; }
        [System.Serializable] public class GenericDrawn { public int b = 2; }

        public class CustomDrawnSample : ScriptableObject
        {
            public ThrowingDrawn throwing = new ThrowingDrawn();
            public GenericDrawn generic = new GenericDrawn();
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
            internal readonly DrawerSample Target = ScriptableObject.CreateInstance<DrawerSample>();
            internal readonly BaseRuntimePanel Panel;
            internal readonly SerializedObject Serialized;

            internal Fixture()
            {
                Panel = RuntimePanel.Create(Settings);
                Serialized = new SerializedObject(Target);
            }

            internal PropertyField Field(string path)
            {
                var field = new PropertyField(Serialized.FindProperty(path));
                Panel.visualTree.Add(field);
                field.Bind(Serialized);
                Tick(Panel);
                Tick(Panel);
                return field;
            }

            public void Dispose()
            {
                Serialized.Dispose();
                Panel.Dispose();
                Object.DestroyImmediate(Target);
                Object.DestroyImmediate(Settings);
            }
        }

        [Fact]
        public void ACollectionsUsageAttributeConfiguresEachElement()
        {
            using var f = new Fixture();
            var color = f.Field("tints.Array.data[1]").Q<ColorField>();
            Assert.False(color.showAlpha);
            Assert.True(color.hdr);
            var gradient = f.Field("ramps.Array.data[0]").Q<GradientField>();
            Assert.True(gradient.hdr);
            Assert.Equal(ColorSpace.Linear, gradient.colorSpace);
        }

        [Fact]
        public void AUsageDrawersChangesReachThePropertyFieldOnceAFrameLater()
        {
            using var f = new Fixture();
            var field = f.Field("tint");
            int events = 0;
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => events++);
            var color = field.Q<ColorField>();
            color.value = Color.green;
            color.value = Color.yellow;
            Assert.Equal(0, events);
            for (int i = 0; i < 4; i++)
            {
                Tick(f.Panel);
                System.Threading.Thread.Sleep(20);
            }
            Assert.Equal(1, events);
            Assert.Equal(Color.yellow, f.Target.tint);
        }

        [Fact]
        public void ATooltipPropertysLabelKeepsItsTooltipWhenElided()
        {
            using var f = new Fixture();
            Assert.False(f.Field("described").Q<IntegerField>().labelElement.displayTooltipWhenElided);
            Assert.True(f.Field("counted").Q<IntegerField>().labelElement.displayTooltipWhenElided);
        }

        // The test assembly is not a game assembly, so its drawers go into the dispatch table
        // directly, for the duration of a test.
        sealed class DrawerTable : System.IDisposable
        {
            static readonly System.Reflection.FieldInfo s_Table = typeof(Editor).Assembly.GetType("UnityEditor.DrawerDispatch")
                .GetField("s_Table", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            static readonly System.Reflection.FieldInfo s_Generation = typeof(Editor).Assembly.GetType("UnityEditor.DrawerDispatch")
                .GetField("s_TableGeneration", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            internal DrawerTable(params (System.Type drawn, System.Type drawer)[] drawers)
            {
                s_Table.SetValue(null, null);
                s_Generation.SetValue(null, -1);
                // Built from the loaded drawers, then the test's are added.
                var built = typeof(Editor).Assembly.GetType("UnityEditor.DrawerDispatch")
                    .GetProperty("Table", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                var entryType = typeof(Editor).Assembly.GetType("UnityEditor.DrawerDispatch+DrawerEntry");
                var dictionary = (System.Collections.IDictionary)built;
                foreach (var (drawn, drawer) in drawers)
                {
                    var entries = System.Array.CreateInstance(entryType, 1);
                    entries.SetValue(System.Activator.CreateInstance(entryType, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                        null, new object[] { drawer, null, false }, null), 0);
                    dictionary[drawn] = entries;
                }
            }

            public void Dispose()
            {
                s_Table.SetValue(null, null);
                s_Generation.SetValue(null, -1);
            }
        }

        [Fact]
        public void ADrawerThatCannotBeCreatedFailsTheBindAsInTheEditor()
        {
            // The Editor creates a drawer with Activator.CreateInstance while the PropertyField
            // handles its bind event, which nothing catches: a throwing constructor, or an open
            // generic drawer class, fails the Bind call.
            using var table = new DrawerTable((typeof(ThrowingDrawn), typeof(ThrowingDrawer)), (typeof(GenericDrawn), typeof(GenericDrawer<>)));
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<CustomDrawnSample>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var throwing = new PropertyField(so.FindProperty("throwing"));
                panel.visualTree.Add(throwing);
                var error = Assert.ThrowsAny<System.Exception>(() => throwing.Bind(so));
                Assert.Equal("drawer constructor", error.GetBaseException().Message);
                var generic = new PropertyField(so.FindProperty("generic"));
                panel.visualTree.Add(generic);
                Assert.ThrowsAny<System.ArgumentException>(() => generic.Bind(so));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void ADrawerWithObjectFieldsIsReportedForItsDefaultReferences()
        {
            // The Editor fills such fields from the drawer script's default references.
            const string member = "UnityEditor.PropertyDrawer (default references of ReferencingDrawer)";
            using var table = new DrawerTable((typeof(ThrowingDrawn), typeof(ReferencingDrawer)));
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<CustomDrawnSample>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                int before = Hits(member);
                var field = new PropertyField(so.FindProperty("throwing"));
                panel.visualTree.Add(field);
                field.Bind(so);
                Tick(panel);
                Assert.Equal(before + 1, Hits(member));
                Assert.Equal("referencing", field.Q<Label>().text);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        public class ImguiSample : ScriptableObject
        {
            public ThrowingDrawn imguiOnly = new ThrowingDrawn();
            public UnityEngine.Events.UnityEvent onClick = new UnityEngine.Events.UnityEvent();
        }

        [Fact]
        public void BuiltInDrawersAreBuiltAndWhatIsNotDrawnIsReported()
        {
            using var f = new Fixture();
            Assert.NotNull(f.Field("ratio").Q<Slider>());
            var counted = f.Field("counted");
            Assert.Equal("Section", counted.Q<Label>(null, "unity-header-drawer__label").text);

            // A drawer with only IMGUI gets the Editor's IMGUI container, which Unbound does not
            // draw: that is reported. A UnityEvent, whose Editor drawer Unbound lacks, too.
            using var table = new DrawerTable((typeof(ThrowingDrawn), typeof(ImguiOnlyDrawer)));
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<ImguiSample>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                const string imgui = "UnityEditor.UIElements.PropertyField (IMGUI of ImguiOnlyDrawer)";
                const string unityEvent = "UnityEditor.UIElements.PropertyField (drawer of UnityEvent)";
                int imguiBefore = Hits(imgui), eventBefore = Hits(unityEvent);
                var imguiField = new PropertyField(so.FindProperty("imguiOnly"));
                var eventField = new PropertyField(so.FindProperty("onClick"));
                panel.visualTree.Add(imguiField);
                panel.visualTree.Add(eventField);
                imguiField.Bind(so);
                eventField.Bind(so);
                Tick(panel);
                Tick(panel);
                Assert.IsType<IMGUIContainer>(imguiField.hierarchy[0]);
                Assert.True(imguiField.ClassListContains("unity-property-field__imgui-container-property"));
                Assert.Equal(imguiBefore + 1, Hits(imgui));
                Assert.Equal(eventBefore + 1, Hits(unityEvent));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }
    }

    public class ReferencingDrawer : PropertyDrawer
    {
        public Texture2D icon;
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => new Label("referencing");
    }

    public class ImguiOnlyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) { }
    }

    [CustomPropertyDrawer(typeof(PropertyFieldDrawerTests.ThrowingDrawn))]
    public class ThrowingDrawer : PropertyDrawer
    {
        public ThrowingDrawer() => throw new System.InvalidOperationException("drawer constructor");
    }

    [CustomPropertyDrawer(typeof(PropertyFieldDrawerTests.GenericDrawn))]
    public class GenericDrawer<T> : PropertyDrawer
    {
    }
}
