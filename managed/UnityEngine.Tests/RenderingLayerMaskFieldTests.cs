using System;
using System.Linq;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class RenderingLayerMaskFieldHolder : ScriptableObject
    {
        public RenderingLayerMask mask = 3;
    }

    // Expected values: tests/projects/editorrenderinglayers (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class RenderingLayerMaskFieldTests
    {
        static T Pass<T>(Event e, Func<T> draw, int instance = 43)
        {
            var previous = Event.current;
            Event.current = e;
            GUIUtility.BeginGUI(0, instance, 0);
            try { return draw(); }
            finally
            {
                GUIUtility.EndGUI(0);
                Event.current = previous;
            }
        }

        static int Hits(string member) =>
            Unbound.Runtime.Stubs.HitReport().Where(h => h.Key == member).Select(h => h.Value).FirstOrDefault();

        static readonly Rect At = new Rect(10, 10, 300, 18);
        static readonly GUIStyle s_Style = new GUIStyle();

        [Fact]
        public void DrawingAMaskKeepsItAndTakesOneId()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            // rect/repaint/masks: the mask comes back as given, also outside the defined layers,
            // and the field takes one control id.
            foreach (uint mask in new[] { 1u, 3u, 0u, uint.MaxValue, 1u << 20 })
            {
                var (value, ids) = Pass(new Event { type = EventType.Repaint }, () =>
                {
                    int before = GUIUtility.GetControlID(FocusType.Passive);
                    uint drawn = EditorGUI.RenderingLayerMaskField(At, new GUIContent("L"), mask, s_Style);
                    return (drawn, GUIUtility.GetControlID(FocusType.Passive) - before - 1);
                });
                Assert.Equal((mask, 1), (value, ids));
            }
        }

        [Fact]
        public void OutsideOnGuiAMaskFieldThrowsMonosNullReference()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previous = Event.current;
            Event.current = null;
            try
            {
                // outside/editorGUI: Mono's message, without a final period.
                Assert.Equal("Object reference not set to an instance of an object",
                    Assert.Throws<NullReferenceException>(() => EditorGUI.RenderingLayerMaskField(At, new GUIContent("L"), (RenderingLayerMask)3, s_Style)).Message);
            }
            finally { Event.current = previous; }
        }

        [Fact]
        public void AnUnmeasuredFamilysSkinStyledMaskFieldsKeepTheirValuesAndSaySo()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var holder = ScriptableObject.CreateInstance<RenderingLayerMaskFieldHolder>();
            try
            {
                var so = new SerializedObject(holder);
                int rect = Hits("UnityEditor.EditorGUI.RenderingLayerMaskField"), layout = Hits("UnityEditor.EditorGUILayout.RenderingLayerMaskField");
                RenderingLayerMask m = 5;
                Assert.Equal(5u, EditorGUI.RenderingLayerMaskField(At, "L", m).value);
                Assert.Equal(5u, EditorGUI.RenderingLayerMaskField(At, new GUIContent("L"), m).value);
                EditorGUI.RenderingLayerMaskField(At, "L", so.FindProperty("mask"));
                EditorGUI.RenderingLayerMaskField(At, new GUIContent("L"), so.FindProperty("mask"));
                Assert.Equal(6u, EditorGUILayout.RenderingLayerMaskField("L", 6u));
                Assert.Equal(6u, EditorGUILayout.RenderingLayerMaskField("L", 6u, s_Style));
                Assert.Equal(6u, EditorGUILayout.RenderingLayerMaskField(new GUIContent("L"), 6u));
                Assert.Equal(6u, EditorGUILayout.RenderingLayerMaskField(new GUIContent("L"), 6u, s_Style));
                Assert.Equal(5u, EditorGUILayout.RenderingLayerMaskField("L", m).value);
                Assert.Equal(5u, EditorGUILayout.RenderingLayerMaskField("L", m, s_Style).value);
                Assert.Equal(5u, EditorGUILayout.RenderingLayerMaskField(new GUIContent("L"), m).value);
                Assert.Equal(5u, EditorGUILayout.RenderingLayerMaskField(new GUIContent("L"), m, s_Style).value);
                EditorGUILayout.RenderingLayerMaskField(new GUIContent("L"), so.FindProperty("mask"));
                Assert.Equal(rect + 4, Hits("UnityEditor.EditorGUI.RenderingLayerMaskField"));
                // The default property field of the mask reports too (before it draws anything).
                int field = Hits("UnityEditor.EditorGUI.PropertyField of a RenderingLayerMask property");
                Pass(new Event { type = EventType.Repaint }, () => EditorGUI.PropertyField(At, so.FindProperty("mask")));
                Assert.Equal(field + 1, Hits("UnityEditor.EditorGUI.PropertyField of a RenderingLayerMask property"));
                Assert.Equal(layout + 9, Hits("UnityEditor.EditorGUILayout.RenderingLayerMaskField"));
                Assert.Equal(3u, holder.mask.value);
            }
            finally { Object.DestroyImmediate(holder); }
        }
    }
}
