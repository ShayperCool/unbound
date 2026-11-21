using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>PropertyField and the drawers of a property's attributes: the ones it builds
    /// ([ColorUsage], [GradientUsage]), their change events, and the ones it reports.</summary>
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
        public void DrawersAndDecoratorsItDoesNotBuildAreReported()
        {
            const string range = "UnityEditor.UIElements.PropertyField (drawer of RangeAttribute)";
            const string header = "UnityEditor.UIElements.PropertyField (decorator of HeaderAttribute)";
            using var f = new Fixture();
            int rangeBefore = Hits(range), headerBefore = Hits(header);
            int totalBefore = Stubs.HitReport().Where(h => h.Key.StartsWith("UnityEditor.UIElements.PropertyField (")).Sum(h => h.Value);
            Assert.NotNull(f.Field("ratio").Q<FloatField>());
            Assert.Equal(rangeBefore + 1, Hits(range));
            Assert.NotNull(f.Field("counted").Q<IntegerField>());
            Assert.Equal(headerBefore + 1, Hits(header));
            // A tooltip has no drawer; the usage attributes have theirs.
            f.Field("described");
            f.Field("tint");
            int totalAfter = Stubs.HitReport().Where(h => h.Key.StartsWith("UnityEditor.UIElements.PropertyField (")).Sum(h => h.Value);
            Assert.Equal(totalBefore + 2, totalAfter);
        }
    }
}
