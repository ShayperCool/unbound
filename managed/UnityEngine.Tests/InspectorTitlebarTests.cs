using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Presets;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    // Expected values: tests/projects/editortitlebar (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class InspectorTitlebarTests : IDisposable
    {
        // The measured release (a test of another family pins its own), and, as the host has no
        // skin images, an enabled toggle style of its own.
        readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);

        public InspectorTitlebarTests() => EditorStyles.SetToggleForTests(new GUIStyle { name = "toggle", fixedWidth = 14, fixedHeight = 14 });

        public void Dispose()
        {
            EditorStyles.SetToggleForTests(null);
            m_Release.Dispose();
        }

        public sealed class Plain : MonoBehaviour { }

        public sealed class WithUpdate : MonoBehaviour { void Update() { } }

        public sealed class WithAwake : MonoBehaviour { void Awake() { } }

        public class WithOnDisable : MonoBehaviour { void OnDisable() { } }

        public sealed class Derived : WithOnDisable { }

        public sealed class Asset : ScriptableObject { }

        static T Pass<T>(Event e, Func<T> draw, int instance = 31)
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

        static Event Mouse(EventType type, Vector2 position) => new Event { type = type, mousePosition = position, button = 0, clickCount = 1 };

        static void Reset()
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
        }

        static int s_Instance = 1000;

        /// <summary>The control ids one call takes in the first Layout pass of a GUI instance of its own.</summary>
        static int Ids(Action call) => Pass(new Event { type = EventType.Layout }, () =>
        {
            int before = GUIUtility.GetControlID(FocusType.Passive);
            call();
            return GUIUtility.GetControlID(FocusType.Passive) - before - 1;
        }, ++s_Instance);

        static readonly Rect Bar = new Rect(10, 300, 300, 22);

        /// <summary>The enabled toggle's middle: right of the 16-point icon and 4 points, its row the icon's.</summary>
        static Vector2 Toggle(Rect bar)
        {
            var style = EditorStyles.inspectorTitlebar;
            return new Vector2(bar.x + style.padding.left + 16 + 4 + 8, bar.y + (style.fixedHeight - 16) / 2 + style.padding.top + 8);
        }

        [Fact]
        public void ABehavioursToggleNeedsAnEventFunctionThatEnablingAffects()
        {
            var go = new GameObject("Titlebar");
            try
            {
                // objectEnabled, objectEnabled/functions.
                Assert.Equal(-1, EditorUtility.GetObjectEnabled(go.AddComponent<Plain>()));
                Assert.Equal(-1, EditorUtility.GetObjectEnabled(go.AddComponent<WithAwake>()));
                Assert.Equal(1, EditorUtility.GetObjectEnabled(go.AddComponent<WithUpdate>()));
                Assert.Equal(1, EditorUtility.GetObjectEnabled(go.AddComponent<Derived>()));
                Assert.Equal(-1, EditorUtility.GetObjectEnabled(go.transform));
                Assert.Equal(1, EditorUtility.GetObjectEnabled(go));
                var disabled = go.AddComponent<WithUpdate>();
                disabled.enabled = false;
                Assert.Equal(0, EditorUtility.GetObjectEnabled(disabled));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void PresetsAndHelpCoverEveryObjectButAGameObjectsPreset()
        {
            var go = new GameObject("Titlebar");
            var asset = ScriptableObject.CreateInstance<Asset>();
            try
            {
                // presetValid, hasHelp.
                Assert.True(new PresetType(go.AddComponent<Plain>()).IsValid());
                Assert.True(new PresetType(go.transform).IsValid());
                Assert.True(new PresetType(asset).IsValid());
                Assert.False(new PresetType(go).IsValid());
                Assert.False(new PresetType((Object)null).IsValid());
                Assert.Equal(new PresetType(go.transform), new PresetType(new GameObject("Other").transform));
                Assert.NotEqual(new PresetType(go.transform), new PresetType(asset));
                Assert.True(Help.HasHelpForObject(go));
                Assert.True(Help.HasHelpForObject(asset));
                Assert.False(Help.HasHelpForObject(null));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(asset);
            }
        }

        [Fact]
        public void TheTitlebarTakesItsFoldoutToggleAndHeaderItemIds()
        {
            var go = new GameObject("Titlebar");
            var asset = ScriptableObject.CreateInstance<Asset>();
            try
            {
                var behaviour = go.AddComponent<Plain>();
                // rect/*/titlebar: the foldout, the toggle (a behaviour, a GameObject), the preset
                // button (not a GameObject) and the help button.
                Assert.Equal(4, Ids(() => EditorGUI.InspectorTitlebar(Bar, false, behaviour, true)));
                Assert.Equal(3, Ids(() => EditorGUI.InspectorTitlebar(Bar, false, go.transform, true)));
                Assert.Equal(3, Ids(() => EditorGUI.InspectorTitlebar(Bar, false, asset, true)));
                Assert.Equal(3, Ids(() => EditorGUI.InspectorTitlebar(Bar, false, go, true)));
                Assert.Equal(4, Ids(() => EditorGUI.InspectorTitlebar(Bar, new Object[] { behaviour })));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(asset);
            }
        }

        [Fact]
        public void TheTitleFoldsOnAClickAndTheArrowKeys()
        {
            var go = new GameObject("Titlebar");
            Reset();
            try
            {
                var behaviour = go.AddComponent<Plain>();
                var title = new Vector2(Bar.x + 150, Bar.center.y);
                bool fold = false;
                // script/title.*: down takes the hot and keyboard control, up on the bar toggles.
                fold = Pass(Mouse(EventType.MouseDown, title), () => EditorGUI.InspectorTitlebar(Bar, fold, behaviour, true));
                Assert.False(fold);
                Assert.NotEqual(0, GUIUtility.hotControl);
                fold = Pass(Mouse(EventType.MouseUp, title), () => EditorGUI.InspectorTitlebar(Bar, fold, behaviour, true));
                Assert.True(fold);
                Assert.Equal(0, GUIUtility.hotControl);
                fold = Pass(Event.KeyboardEvent("left"), () => EditorGUI.InspectorTitlebar(Bar, fold, behaviour, true));
                Assert.False(fold);
                fold = Pass(Event.KeyboardEvent("right"), () => EditorGUI.InspectorTitlebar(Bar, fold, behaviour, true));
                Assert.True(fold);
                // A release off the bar does not toggle.
                Pass(Mouse(EventType.MouseDown, title), () => EditorGUI.InspectorTitlebar(Bar, fold, behaviour, true));
                fold = Pass(Mouse(EventType.MouseUp, title + new Vector2(0, 60)), () => EditorGUI.InspectorTitlebar(Bar, fold, behaviour, true));
                Assert.True(fold);
            }
            finally
            {
                Reset();
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void TheToggleSetsEveryObjectToTheFirstBehavioursOppositeState()
        {
            var go = new GameObject("Titlebar");
            Reset();
            try
            {
                var first = go.AddComponent<Plain>();
                var second = go.AddComponent<Plain>();
                second.enabled = false;
                var both = new Object[] { first, second };
                // script/toggle.*: one behaviour is disabled.
                Pass(Mouse(EventType.MouseDown, Toggle(Bar)), () => EditorGUI.InspectorTitlebar(Bar, false, first, true));
                bool changed = Pass(Mouse(EventType.MouseUp, Toggle(Bar)), () =>
                {
                    GUI.changed = false;
                    EditorGUI.InspectorTitlebar(Bar, false, first, true);
                    return GUI.changed;
                });
                Assert.True(changed);
                Assert.False(first.enabled);
                // script/mixed.*: the first one's state (off) is shown for both; a click enables both.
                Pass(Mouse(EventType.MouseDown, Toggle(Bar)), () => EditorGUI.InspectorTitlebar(Bar, false, both, true));
                Pass(Mouse(EventType.MouseUp, Toggle(Bar)), () => EditorGUI.InspectorTitlebar(Bar, false, both, true));
                Assert.True(first.enabled);
                Assert.True(second.enabled);
            }
            finally
            {
                Reset();
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void OutsideOnGuiTheTitlebarThrowsAsTheEditors()
        {
            var go = new GameObject("Titlebar");
            var previous = Event.current;
            Event.current = null;
            try
            {
                var behaviour = go.AddComponent<Plain>();
                // outside/editorGUI.
                var e = Assert.Throws<NullReferenceException>(() => EditorGUI.InspectorTitlebar(new Rect(0, 0, 300, 22), false, behaviour, true));
                Assert.Equal("Object reference not set to an instance of an object", e.Message);
            }
            finally
            {
                Event.current = previous;
                Object.DestroyImmediate(go);
            }
        }

        static int Hits(string member) => Unbound.Runtime.Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        [Fact]
        public void AnUnmeasuredFamilyKeepsItsAnswersAndSaysSo()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var go = new GameObject("Titlebar");
            try
            {
                var behaviour = go.AddComponent<Plain>();
                int titlebar = Hits("UnityEditor.EditorGUI.InspectorTitlebar"), help = Hits("UnityEditor.Help.HasHelpForObject");
                Assert.True(Pass(new Event { type = EventType.Repaint }, () => EditorGUI.InspectorTitlebar(Bar, true, behaviour, true)));
                Assert.Equal(titlebar + 1, Hits("UnityEditor.EditorGUI.InspectorTitlebar"));
                Assert.False(Help.HasHelpForObject(go));
                Assert.Equal(help + 1, Hits("UnityEditor.Help.HasHelpForObject"));
                Assert.False(new PresetType(go.transform).IsValid());
                Assert.Null(AssetPreview.GetMiniThumbnail(go));
                // The enabled state as before: every behaviour's own, none for a GameObject.
                Assert.Equal(1, EditorUtility.GetObjectEnabled(behaviour));
                Assert.Equal(-1, EditorUtility.GetObjectEnabled(go));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void HeaderItemsStepLeftFromTheSettingsButton()
        {
            var go = new GameObject("Titlebar");
            try
            {
                var r = new Rect(200, 0, 16, 16);
                // Two items (preset, help): the last one's rect, one item and spacing left of the first.
                var last = Pass(new Event { type = EventType.Layout }, () => EditorGUIUtility.DrawEditorHeaderItems(r, new Object[] { go.transform }, 4));
                Assert.Equal(180, last.x);
                // A GameObject has no preset button: the help button stays where the first goes.
                last = Pass(new Event { type = EventType.Layout }, () => EditorGUIUtility.DrawEditorHeaderItems(r, new Object[] { go }, 4));
                Assert.Equal(200, last.x);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
