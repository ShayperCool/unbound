using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Object previews and object titles (tests/projects/editorpreview records the
    /// Editor's): ObjectPreview's defaults and target walking, an Editor's preview members,
    /// DrawPreview's calls, DrawPropertiesExcluding and ObjectNames' titles and class names.</summary>
    public class EditorPreviewTests
    {
        public class Sample : ScriptableObject
        {
            public int first = 1;
            public float second = 2f;
            public string third = "3";
        }

        public class TwoWords : ScriptableObject { }

        public class PlainBehaviour : MonoBehaviour { }

        [AddComponentMenu("Tests/Fancy Name")]
        public class MenuBehaviour : MonoBehaviour { }

        // A menu path is the class's own: a derived class has none.
        public class DerivedMenuBehaviour : MenuBehaviour { }

        [AddComponentMenu("Tests/Trailing/")]
        public class TrailingBehaviour : MonoBehaviour { }

        [Obsolete("old")]
        public class OldBehaviour : MonoBehaviour { }

        sealed class Recording : ObjectPreview
        {
            internal readonly List<string> Calls = new List<string>();
            public override bool MoveNextTarget() { bool more = base.MoveNextTarget(); Calls.Add("next:" + m_ReferenceTargetIndex + ":" + more); return more; }
            public override void ResetTarget() { base.ResetTarget(); Calls.Add("reset"); }
            public override void OnPreviewGUI(Rect r, GUIStyle background) => Calls.Add($"gui:{r.x},{r.y},{r.width},{r.height}:{background.name}:{m_ReferenceTargetIndex}");
        }

        public class SampleEditor : Editor
        {
            internal static readonly List<string> Calls = new List<string>();
            internal static bool ReadSerializedObject;
            public override void OnPreviewGUI(Rect r, GUIStyle background)
            {
                Calls.Add($"{r.width}:{background.name}:{target.name}:{targets.Length}");
                if (ReadSerializedObject) Assert.NotNull(serializedObject);
            }
            internal void Excluding(params string[] names) => DrawPropertiesExcluding(serializedObject, names);
        }

        static Sample[] Samples(params string[] names) => names.Select(n =>
        {
            var s = ScriptableObject.CreateInstance<Sample>();
            s.name = n;
            return s;
        }).ToArray();

        [Fact]
        public void APreviewHasNoGUIAndWalksItsTargets()
        {
            var targets = Samples("Alpha", "Beta", "Gamma");
            var preview = new ObjectPreview();
            // Before Initialize there are no targets to answer with.
            Assert.Throws<NullReferenceException>(() => preview.target);
            Assert.Throws<NullReferenceException>(() => preview.GetPreviewTitle());
            preview.Initialize(new Object[] { targets[0] });
            Assert.False(preview.HasPreviewGUI());
            Assert.Equal("Alpha", preview.GetPreviewTitle().text);
            Assert.NotSame(preview.GetPreviewTitle(), preview.GetPreviewTitle());
            Assert.Equal("", preview.GetInfoString());
            Assert.Null(preview.CreatePreview(new VisualElement()));
            Assert.Same(targets[0], preview.target);

            // Several: the count and the script's class; MoveNextTarget says whether one more
            // follows the target it moved to, and walks past the end.
            preview.Initialize(targets);
            Assert.Equal("3 Samples", preview.GetPreviewTitle().text);
            Assert.True(preview.MoveNextTarget());
            Assert.Same(targets[1], preview.target);
            Assert.False(preview.MoveNextTarget());
            Assert.Same(targets[2], preview.target);
            Assert.False(preview.MoveNextTarget());
            Assert.Throws<IndexOutOfRangeException>(() => preview.target);
            preview.ResetTarget();
            Assert.Same(targets[0], preview.target);

            // An engine type's class name in words.
            var shader = Shader.Find("Sprites/Default");
            var textures = new Object[] { new Texture2D(2, 2), new Texture2D(2, 2) };
            preview.Initialize(textures);
            Assert.Equal("2 Texture 2Ds", preview.GetPreviewTitle().text);
            preview.Initialize(new Object[0]);
            Assert.Throws<IndexOutOfRangeException>(() => preview.GetPreviewTitle());
            Assert.False(preview.MoveNextTarget());
            preview.Initialize(null);
            Assert.Throws<NullReferenceException>(() => preview.GetPreviewTitle());
            preview.Cleanup();
            Assert.Equal(typeof(Material), new CustomPreviewAttribute(typeof(Material)).m_Type);
            foreach (var t in targets) Object.DestroyImmediate(t);
            foreach (var t in textures) Object.DestroyImmediate(t);
        }

        [Fact]
        public void AnEditorAnswersWithItsPreviewAndWalksItsTargetsAround()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var targets = Samples("Alpha", "Beta", "Gamma");
            var one = Editor.CreateEditor(targets[0], typeof(SampleEditor));
            var three = Editor.CreateEditor(targets, typeof(SampleEditor));
            try
            {
                Assert.False(one.HasPreviewGUI());
                Assert.Equal("Alpha", one.GetPreviewTitle().text);
                Assert.Equal("", one.GetInfoString());
                Assert.Null(one.RenderStaticPreview("Assets/x.asset", null, 16, 16));
                Assert.Null(one.CreatePreview(new VisualElement()));
                Assert.Equal("Alpha (Sample)", one.targetTitle);
                Assert.Equal(0, one.referenceTargetIndex);
                one.OnPreviewSettings();
                one.ReloadPreviewInstances();
                Assert.Equal("3 Samples", three.GetPreviewTitle().text);
                Assert.Equal("3 Samples", three.targetTitle);
                // The preview's title keeps the class name; the editor's puts it in words.
                var worded = new Object[] { ScriptableObject.CreateInstance<TwoWords>(), ScriptableObject.CreateInstance<TwoWords>() };
                var wordedEditor = Editor.CreateEditor(worded);
                Assert.Equal("2 TwoWordss", wordedEditor.GetPreviewTitle().text);
                Assert.Equal("2 Two Wordss", wordedEditor.targetTitle);
                Object.DestroyImmediate(wordedEditor);
                foreach (var w in worded) Object.DestroyImmediate(w);

                // The index wraps: after the last target the first again.
                var walk = new List<string>();
                for (int i = 0; i < 4; i++) walk.Add(three.MoveNextTarget() + ">" + three.target.name + ">" + three.referenceTargetIndex);
                Assert.Equal(new[] { "True>Beta>1", "True>Gamma>2", "True>Alpha>0", "True>Beta>1" }, walk);
                three.ResetTarget();
                Assert.Same(targets[0], three.target);
                var thrown = Assert.Throws<InvalidOperationException>(() => one.Cleanup());
                Assert.Equal("You shouldn't call Cleanup for Editors. Dispose resources in Editor.OnDisable", thrown.Message);
            }
            finally
            {
                Object.DestroyImmediate(one);
                Object.DestroyImmediate(three);
                foreach (var t in targets) Object.DestroyImmediate(t);
            }
        }

        [Fact]
        public void DrawPreviewDrawsOneTargetOverTheAreaAndSeveralInAGrid()
        {
            using var logs = new IMGUIContainerTests.Logs();
            using var f = new IMGUIContainerTests.Fixture();
            var targets = Samples("Alpha", "Beta", "Gamma");
            var single = new Recording();
            single.Initialize(new Object[] { targets[0] });
            var many = new Recording();
            many.Initialize(targets);
            var three = Editor.CreateEditor(targets, typeof(SampleEditor));
            SampleEditor.Calls.Clear();
            var area = new Rect(4f, 6f, 300f, 200f);
            var container = f.Add(() =>
            {
                if (Event.current.type != EventType.KeyDown) return;
                single.DrawPreview(area);
                many.DrawPreview(area);
                three.DrawPreview(area);
            });
            f.Tick();
            IMGUIContainerTests.Send(container, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }), out _);
            try
            {
                Assert.Equal(new[] { "gui:4,6,300,200:PreBackground:0" }, single.Calls);
                Assert.Equal(new[]
                {
                    "reset", "gui:0,0,82,82:PreBackgroundSolid:0", "next:1:True", "gui:0,0,82,82:PreBackgroundSolid:1", "next:2:False",
                    "gui:0,0,82,82:PreBackgroundSolid:2", "next:3:False", "reset",
                }, many.Calls);
                // The editor is asked about one target at a time, and is back at the first after.
                Assert.Equal(new[] { "82:PreBackgroundSolid:Alpha:3", "82:PreBackgroundSolid:Beta:3", "82:PreBackgroundSolid:Gamma:3" }, SampleEditor.Calls);
                Assert.Same(targets[0], three.target);
                // Reading `targets` inside such a preview is an error, once per target here.
                Assert.Equal(3, logs.Entries.Count(e => e == "Error:The targets array should not be used inside OnSceneGUI or OnPreviewGUI. Use the single target property instead."));
                Assert.DoesNotContain(logs.Entries, e => e.Contains("The serializedObject should not be used"));
                // So is reading the serialized object.
                SampleEditor.ReadSerializedObject = true;
                IMGUIContainerTests.Send(container, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }), out _);
                SampleEditor.ReadSerializedObject = false;
                Assert.Equal(3, logs.Entries.Count(e => e == "Error:The serializedObject should not be used inside OnSceneGUI or OnPreviewGUI. Use the target property directly instead."));
                // The serialized object is made from `targets` on its first read: one more of that error.
                Assert.Equal(7, logs.Entries.Count(e => e.Contains("The targets array should not be used")));
                Assert.NotNull(three.serializedObject);
                Assert.Equal(3, logs.Entries.Count(e => e.Contains("The serializedObject should not be used")));
                Assert.True(Editor.m_AllowMultiObjectAccess);
                Assert.DoesNotContain(logs.Entries, e => e.StartsWith("Exception") || e.Contains("GUI Error"));
            }
            finally
            {
                Object.DestroyImmediate(three);
                foreach (var t in targets) Object.DestroyImmediate(t);
            }
        }

        // Not measured in the Editor: written from its public source.
        [Fact]
        public void ADoubleClickOnATargetOfSeveralSelectsIt()
        {
            using var f = new IMGUIContainerTests.Fixture();
            var targets = Samples("Alpha", "Beta", "Gamma");
            var many = new Recording();
            many.Initialize(targets);
            var area = new Rect(4f, 6f, 300f, 200f);
            Vector2 at = default;
            int clicks = 2;
            var container = f.Add(() =>
            {
                if (Event.current.type != EventType.MouseDown) return;
                Event.current.clickCount = clicks;
                Event.current.mousePosition = at;
                many.DrawPreview(area);
            });
            container.style.height = 300f;
            f.Tick();
            var previous = Selection.objects;
            try
            {
                void Click()
                {
                    Selection.objects = new Object[0];
                    Unbound.Runtime.GuiEvents.MousePosition = container.worldBound.center;
                    IMGUIContainerTests.Send(container, MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = container.worldBound.center, clickCount = 1 }), out _);
                }
                // The second of three squares of 82 points spans x from 112 to 195.
                at = new Vector2(150f, 100f);
                Click();
                Assert.Equal(new Object[] { targets[1] }, Selection.objects);
                // Between two squares, and a single click: nothing.
                at = new Vector2(105f, 100f);
                Click();
                Assert.Empty(Selection.objects);
                at = new Vector2(150f, 100f);
                clicks = 1;
                Click();
                Assert.Empty(Selection.objects);
            }
            finally
            {
                Selection.objects = previous;
                foreach (var t in targets) Object.DestroyImmediate(t);
            }
        }

        [Fact]
        public void APreviewThatThrowsLeavesTheTargetsReadable()
        {
            using var logs = new IMGUIContainerTests.Logs();
            using var f = new IMGUIContainerTests.Fixture();
            var targets = Samples("Alpha", "Beta");
            var throwing = new Throwing();
            throwing.Initialize(targets);
            var container = f.Add(() =>
            {
                if (Event.current.type == EventType.KeyDown) throwing.DrawPreview(new Rect(0f, 0f, 300f, 200f));
            });
            f.Tick();
            Assert.Throws<InvalidOperationException>(() => IMGUIContainerTests.Send(container, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }), out _));
            Assert.True(Editor.m_AllowMultiObjectAccess);
            // The square's group was closed.
            Assert.Equal(1, GUIClip.Depth);
            foreach (var t in targets) Object.DestroyImmediate(t);
        }

        sealed class Throwing : ObjectPreview
        {
            public override void OnPreviewGUI(Rect r, GUIStyle background) => throw new InvalidOperationException("preview");
        }

        [Fact]
        public void DrawPropertiesExcludingLeavesTheNamedPropertiesOut()
        {
            using var logs = new IMGUIContainerTests.Logs();
            using var f = new IMGUIContainerTests.Fixture();
            var target = Samples("Alpha")[0];
            var editor = (SampleEditor)Editor.CreateEditor(target, typeof(SampleEditor));
            try
            {
                float Height(params string[] names)
                {
                    var container = f.Add(() => editor.Excluding(names));
                    f.Tick();
                    f.Tick();
                    float height = container.layout.height;
                    container.RemoveFromHierarchy();
                    return height;
                }
                // The script row and three fields, 18 points each and 2 between them.
                Assert.Equal(78f, Height());
                Assert.Equal(58f, Height("second"));
                Assert.Equal(58f, Height("m_Script"));
                Assert.Equal(18f, Height("m_Script", "first", "third"));
                Assert.Equal(78f, Height("missing"));
                Assert.DoesNotContain(logs.Entries, e => e.StartsWith("Exception"));
                // No names at all: the search of them fails, and nothing is drawn.
                Assert.Equal(0f, Height(null));
                Assert.Contains(logs.Entries, e => e.StartsWith("Exception:Value cannot be null"));
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(target);
            }
        }

        [Fact]
        public void ObjectsAreTitledByNameTypeOrComponentMenu()
        {
            var go = new GameObject("Go");
            var scriptable = Samples("Alpha")[0];
            var texture = new Texture2D(2, 2) { name = "tex" };
            var mesh = new Mesh { name = "myMesh" };
            var filter = go.AddComponent<MeshFilter>();
            try
            {
                Assert.Equal("Go", ObjectNames.GetInspectorTitle(go));
                Assert.Equal("Transform", ObjectNames.GetInspectorTitle(go.transform));
                Assert.Equal("Camera", ObjectNames.GetInspectorTitle(go.AddComponent<Camera>()));
                Assert.Equal("Line Renderer", ObjectNames.GetInspectorTitle(go.AddComponent<LineRenderer>()));
                Assert.Equal("Plain Behaviour (Script)", ObjectNames.GetInspectorTitle(go.AddComponent<PlainBehaviour>()));
                Assert.Equal("Fancy Name", ObjectNames.GetInspectorTitle(go.AddComponent<MenuBehaviour>()));
                Assert.Equal("Trailing Behaviour (Script)", ObjectNames.GetInspectorTitle(go.AddComponent<TrailingBehaviour>()));
                Assert.Equal("Derived Menu Behaviour (Script)", ObjectNames.GetInspectorTitle(go.AddComponent<DerivedMenuBehaviour>()));
#pragma warning disable 618
                Assert.Equal("Old Behaviour (Script) (Deprecated)", ObjectNames.GetInspectorTitle(go.AddComponent<OldBehaviour>()));
                Assert.Equal("Some Name", ObjectNames.MangleVariableName("m_someName"));
                Assert.Equal("Transform", ObjectNames.GetPropertyEditorTitle(go.transform));
#pragma warning restore 618
                Assert.Equal("[none] (Mesh Filter)", ObjectNames.GetInspectorTitle(filter));
                Assert.Equal("Mesh Filter", ObjectNames.GetInspectorTitle(filter, true));
                filter.sharedMesh = mesh;
                Assert.Equal("My Mesh (Mesh Filter)", ObjectNames.GetInspectorTitle(filter));
                Assert.Equal("Alpha (Sample)", ObjectNames.GetInspectorTitle(scriptable));
                Assert.Equal("Tex (Texture 2D)", ObjectNames.GetInspectorTitle(texture));
                Assert.Equal("Nothing Selected", ObjectNames.GetInspectorTitle(null));

                Assert.Equal("GameObject", ObjectNames.GetClassName(go));
                Assert.Equal("MonoBehaviour", ObjectNames.GetClassName(go.GetComponent<PlainBehaviour>()));
                Assert.Equal("MonoBehaviour", ObjectNames.GetClassName(scriptable));
                Assert.Equal("Texture2D", ObjectNames.GetClassName(texture));
                Assert.Throws<ArgumentNullException>(() => ObjectNames.GetClassName(null));
                Assert.Equal("Object", ObjectNames.GetTypeName(null));
                Assert.Equal("Sample", ObjectNames.GetTypeName(scriptable));

                // Destroyed: a script object keeps only its suffix, another object is nothing.
                Object.DestroyImmediate(scriptable);
                Object.DestroyImmediate(texture);
                Assert.Equal(" (Script)", ObjectNames.GetInspectorTitle(scriptable));
                Assert.Equal("Nothing Selected", ObjectNames.GetInspectorTitle(texture));
                Assert.Throws<MissingReferenceException>(() => ObjectNames.GetClassName(texture));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
