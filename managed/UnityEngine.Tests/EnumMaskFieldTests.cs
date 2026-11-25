using System;
using System.Linq;
using UnityEditor;
using Xunit;

#pragma warning disable 618 // the tested members are obsolete

namespace UnityEngine.Tests
{
    // Expected values: tests/projects/editorenummasks (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class EnumMaskFieldTests
    {
        [Flags] public enum Flags { None = 0, Red = 1, Green = 2, Blue = 4, Big = 8 }
        public enum Plain { First, Second, Third }

        static T Pass<T>(Event e, Func<T> draw, int instance = 47)
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
        public void DrawingAMaskKeepsItsValueAndTypeAndTakesOneId()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            // rect/repaint/enumMaskField, enumMaskPopup: the value as given, even every bit or a plain enum.
            foreach (Enum value in new Enum[] { Flags.None, Flags.Red | Flags.Blue, (Flags)15, (Flags)(-1), Plain.Second })
            {
                var (drawn, ids) = Pass(new Event { type = EventType.Repaint }, () =>
                {
                    int before = GUIUtility.GetControlID(FocusType.Passive);
                    var result = EditorGUI.EnumMaskField(At, value, s_Style);
                    return (result, GUIUtility.GetControlID(FocusType.Passive) - before - 1);
                });
                Assert.Equal((value, 1), (drawn, ids));
                Assert.Equal(value.GetType(), drawn.GetType());
            }
            Assert.Equal(Flags.Red | Flags.Big, Pass(new Event { type = EventType.Repaint }, () => EditorGUI.EnumMaskPopup(At, "L", Flags.Red | Flags.Big, s_Style)));
        }

        [Fact]
        public void OutsideOnGuiAMaskFieldThrowsMonosNullReference()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previous = Event.current;
            Event.current = null;
            try
            {
                // outside/editorGUI: Mono's message, without a final period, also without a label.
                Assert.Equal("Object reference not set to an instance of an object",
                    Assert.Throws<NullReferenceException>(() => EditorGUI.EnumMaskField(At, Flags.Red, s_Style)).Message);
            }
            finally { Event.current = previous; }
        }

        [Fact]
        public void AnUnmeasuredFamilysSkinStyledMaskFieldsKeepTheirValuesAndSaySo()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            int field = Hits("UnityEditor.EditorGUI.EnumMaskField"), popup = Hits("UnityEditor.EditorGUI.EnumMaskPopup");
            int layoutField = Hits("UnityEditor.EditorGUILayout.EnumMaskField"), layoutPopup = Hits("UnityEditor.EditorGUILayout.EnumMaskPopup");
            Assert.Equal(Flags.Red, EditorGUI.EnumMaskField(At, Flags.Red));
            Assert.Equal(Flags.Red, EditorGUI.EnumMaskField(At, "L", Flags.Red));
            Assert.Equal(Flags.Red, EditorGUI.EnumMaskField(At, new GUIContent("L"), Flags.Red));
            Assert.Equal(Flags.Blue, EditorGUI.EnumMaskPopup(At, "L", Flags.Blue));
            Assert.Equal(Flags.Blue, EditorGUI.EnumMaskPopup(At, new GUIContent("L"), Flags.Blue));
            Assert.Equal(Flags.Green, EditorGUILayout.EnumMaskField(Flags.Green));
            Assert.Equal(Flags.Green, EditorGUILayout.EnumMaskField(Flags.Green, s_Style));
            Assert.Equal(Flags.Green, EditorGUILayout.EnumMaskField("L", Flags.Green, s_Style));
            Assert.Equal(Flags.Big, EditorGUILayout.EnumMaskPopup("L", Flags.Big));
            Assert.Equal(Flags.Big, EditorGUILayout.EnumMaskPopup(new GUIContent("L"), Flags.Big, s_Style));
            Assert.Equal((field + 3, popup + 2), (Hits("UnityEditor.EditorGUI.EnumMaskField"), Hits("UnityEditor.EditorGUI.EnumMaskPopup")));
            Assert.Equal((layoutField + 3, layoutPopup + 2), (Hits("UnityEditor.EditorGUILayout.EnumMaskField"), Hits("UnityEditor.EditorGUILayout.EnumMaskPopup")));
        }
    }
}
