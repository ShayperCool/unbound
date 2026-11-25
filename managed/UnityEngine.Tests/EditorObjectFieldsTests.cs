using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    [Serializable]
    public class ObjectFieldsNested { public int a; public int b; }

    public class ObjectFieldsHolder : ScriptableObject
    {
        public Texture2D texture;
        public Transform target;
        public KeyCode key = KeyCode.A;
        public ObjectFieldsNested nested = new ObjectFieldsNested();
        public Vector4 v4 = new Vector4(1, 2, 3, 4);
        public Vector3 v3 = new Vector3(1, 2, 3);
        public Color color = Color.red;
    }

    /// <summary>EditorGUI's object, colour, gradient, mask, layer and tag fields as
    /// tests/projects/editorobjectfields measures them on the measured release, and the
    /// not-implemented notice of other families.</summary>
    public class EditorObjectFieldsTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        static T Pass<T>(Event e, Func<T> draw, int instance = 13)
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

        static Event Down(Vector2 p, int button = 0) => new Event { type = EventType.MouseDown, mousePosition = p, button = button, clickCount = 1 };
        static Event Drop(Vector2 p) => new Event { type = EventType.DragPerform, mousePosition = p };

        static void Reset()
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
            DragAndDrop.PrepareStartDrag();
            // An object field that exits the GUI (its picker) leaves its icon size set.
            EditorGUIUtility.SetIconSize(Vector2.zero);
        }

        static readonly Rect Field = new Rect(10, 300, 300, 18);
        static readonly Vector2 Body = new Vector2(100, 309);

        [Fact]
        public void ADroppedGameObjectGivesAComponentFieldItsComponent()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var go = new GameObject("Target");
            try
            {
                DragAndDrop.objectReferences = new Object[] { go };
                Object value = null;
                value = Pass(Drop(Body), () => EditorGUI.ObjectField(Field, value, typeof(Transform), true));
                Assert.Same(go.transform, value);
            }
            finally { Object.DestroyImmediate(go); Reset(); }
        }

        [Fact]
        public void ADropOfTheWrongTypeOrOfASceneObjectWhereNoneIsAllowedKeepsTheObject()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var a = new Texture2D(2, 2) { name = "A" };
            var b = new Texture2D(2, 2) { name = "B" };
            var other = ScriptableObject.CreateInstance<ObjectFieldsHolder>();
            try
            {
                DragAndDrop.objectReferences = new Object[] { other };
                Assert.Same(a, Pass(Drop(Body), () => EditorGUI.ObjectField(Field, a, typeof(Texture2D), true)));
                DragAndDrop.objectReferences = new Object[] { b };
                // A texture made at run time is not persistent: a scene object.
                Assert.Same(a, Pass(Drop(Body), () => EditorGUI.ObjectField(Field, a, typeof(Texture2D), false)));
                Assert.Same(b, Pass(Drop(Body), () => EditorGUI.ObjectField(Field, a, typeof(Texture2D), true)));
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(other); Reset(); }
        }

        [Fact]
        public void AClickFocusesTheFieldAndDeleteClearsIt()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var a = new Texture2D(2, 2);
            try
            {
                Object value = a;
                value = Pass(Down(Body), () => EditorGUI.ObjectField(Field, value, typeof(Texture2D), true));
                Assert.NotEqual(0, GUIUtility.keyboardControl);
                bool changed = false;
                value = Pass(Event.KeyboardEvent("delete"), () =>
                {
                    GUI.changed = false;
                    var result = EditorGUI.ObjectField(Field, value, typeof(Texture2D), true);
                    changed = GUI.changed;
                    return result;
                });
                Assert.Null(value);
                Assert.True(changed);
            }
            finally { Object.DestroyImmediate(a); Reset(); }
        }

        [Fact]
        public void ThePickerButtonExitsTheGUIAndSaysThePickerIsNotShown()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                var iconSize = EditorGUIUtility.GetIconSize();
                Assert.Throws<ExitGUIException>(() => Pass(Down(new Vector2(10 + 300 - 8, 309)), () => EditorGUI.ObjectField(Field, null, typeof(Texture2D), true)));
                Assert.NotEqual(0, GUIUtility.keyboardControl);
                // The field's icon size does not outlive its exit.
                Assert.Equal(iconSize, EditorGUIUtility.GetIconSize());
            }
            finally { Debug.unityLogger.logHandler = previous; Reset(); }
            Assert.Contains(capture.Lines, line => line.Contains("object picker is not shown"));
        }

        [Fact]
        public void ColourCopyWritesItsHtmlTextAndPasteKeepsAHiddenAlpha()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var copy = new Rect(10, 366, 300, 18);
            var paste = new Rect(10, 388, 300, 18);
            var source = new Color(0.25f, 0.5f, 0.75f, 0.5f);
            Pass(Down(new Vector2(30, 375)), () => EditorGUI.ColorField(copy, "Copy", source));
            Pass(Event.KeyboardEvent("^c"), () => EditorGUI.ColorField(copy, "Copy", source));
            Assert.Equal("#4080BF80", EditorGUIUtility.systemCopyBuffer);
            var target = new Color(1, 0, 0, 0.25f);
            Pass(Down(new Vector2(30, 397)), () => EditorGUI.ColorField(paste, new GUIContent("Paste"), target, true, false, false));
            target = Pass(Event.KeyboardEvent("^v"), () => EditorGUI.ColorField(paste, new GUIContent("Paste"), target, true, false, false));
            Assert.Equal(new Color(64 / 255f, 128 / 255f, 191 / 255f, 0.25f), target);
            Reset();
        }

        [Fact]
        public void AGradientFieldWithoutAGradientThrowsDrawingIt()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Assert.Throws<NullReferenceException>(() => Pass(new Event { type = EventType.Repaint }, () => EditorGUI.GradientField(Field, (Gradient)null)));
            Assert.Null(Pass(new Event { type = EventType.Layout }, () => EditorGUI.GradientField(Field, (Gradient)null)));
        }

        static object Arm(string typeName, string field, int id, int value)
        {
            var type = typeName == "Popup"
                ? typeof(EditorGUI).GetNestedType("PopupCallbackInfo", BindingFlags.NonPublic)
                : typeof(EditorGUI).Assembly.GetType("UnityEditor.MaskFieldGUI").GetNestedType("MaskCallbackInfo", BindingFlags.NonPublic);
            var instance = Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { id }, null);
            type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, value);
            type.GetField(typeName == "Popup" ? "instance" : "m_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, instance);
            return instance;
        }

        static T Command<T>(string name, string typeName, string field, int value, Func<T> draw) =>
            Pass(new Event { type = EventType.ExecuteCommand, commandName = name }, () =>
            {
                // (Drawn once first in the pass, as the probe's fields are, so that the field's
                // control id follows the passive one taken just before it.)
                draw();
                int before = GUIUtility.GetControlID(FocusType.Passive);
                Arm(typeName, field, before + 1, value);
                return draw();
            });

        [Fact]
        public void TheMenusChoicesComeBackAsCommandsToTheirControl()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            string[] names = { "One", "Two", "Three" };
            // (A style given: the tests have no Editor resources for the popup's.)
            var style = new GUIStyle();
            Assert.Equal(3, Command("MaskMenuChanged", "Mask", "m_NewMask", 3, () => EditorGUI.MaskField(Field, 5, names, style)));
            Assert.Equal(-1, Command("MaskMenuChanged", "Mask", "m_NewMask", -1, () => EditorGUI.MaskField(Field, 0, names, style)));
            Assert.Equal("Respawn", Command("PopupMenuChanged", "Popup", "m_SelectedIndex", 1, () => EditorGUI.TagField(Field, "Untagged", style)));
            // The fifth named layer of the default layers: UI, layer 5.
            Assert.Equal(5, Command("PopupMenuChanged", "Popup", "m_SelectedIndex", 4, () => EditorGUI.LayerField(Field, 0, style)));
        }

        [Fact]
        public void PropertyFieldDrawsAVector4AsAFoldoutAndAVector3Inline()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var holder = ScriptableObject.CreateInstance<ObjectFieldsHolder>();
            try
            {
                var so = new SerializedObject(holder);
                int Ids(string name) => Pass(new Event { type = EventType.Repaint }, () =>
                {
                    int before = GUIUtility.GetControlID(FocusType.Passive);
                    EditorGUI.PropertyField(new Rect(0, 0, 300, 40), so.FindProperty(name));
                    return GUIUtility.GetControlID(FocusType.Passive) - before - 1;
                });
                // A Vector4's children show: it is a foldout, like a nested class's (a Vector3's
                // columns are inline).
                Assert.Equal(Ids("nested"), Ids("v4"));
                Assert.NotEqual(Ids("nested"), Ids("v3"));
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [Fact]
        public void AnObjectPropertyShowsItsReferenceAndTakesADroppedGameObjectsComponent()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var holder = ScriptableObject.CreateInstance<ObjectFieldsHolder>();
            var go = new GameObject("Dropped");
            var other = new Texture2D(2, 2);
            try
            {
                var so = new SerializedObject(holder);
                // A field without a type of its own shows the property's: "None (Transform)".
                Assert.Equal("None (Transform)", EditorGUI.ObjectFieldContent(null, null, so.FindProperty("target")).text);
                // (Dropped on the field, right of its label.)
                var at = new Rect(10, 300, 300, 18);
                DragAndDrop.objectReferences = new Object[] { other };
                Pass(Drop(new Vector2(250, 309)), () => { EditorGUI.PropertyField(at, so.FindProperty("target")); return 0; });
                so.ApplyModifiedProperties();
                Assert.Null(holder.target);
                DragAndDrop.objectReferences = new Object[] { go };
                Pass(Drop(new Vector2(250, 309)), () => { EditorGUI.PropertyField(at, so.FindProperty("target")); return 0; });
                so.ApplyModifiedProperties();
                Assert.Same(go.transform, holder.target);
            }
            finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(go); Object.DestroyImmediate(other); Reset(); }
        }

        [Fact]
        public void AnEngineScriptableObjectIsDropped()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var skin = ScriptableObject.CreateInstance<GUISkin>();
            try
            {
                DragAndDrop.objectReferences = new Object[] { skin };
                Assert.Same(skin, Pass(Drop(Body), () => EditorGUI.ObjectField(Field, null, typeof(GUISkin), true)));
            }
            finally { Object.DestroyImmediate(skin); Reset(); }
        }

        static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source), "..", ".."));

        /// <summary>Settings assets of tests/projects/settings: their nodes come from the type
        /// trees (a native PPtr&lt;MonoBehaviour&gt;, a native enum).</summary>
        [Fact]
        public void NativePropertiesKeepTheirOldReferenceCheckAndNoEnumNames()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var probe = System.IO.Path.Combine(Repo(), "tests", "projects", "settings");
            var project = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unbound-objectfields-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(project, "ProjectSettings"));
            foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(probe, "ProjectSettings")))
                System.IO.File.Copy(f, System.IO.Path.Combine(project, "ProjectSettings", System.IO.Path.GetFileName(f)));
            var before = ProjectInfo.ProjectPath;
            ProjectInfo.ProjectPath = project;
            Unbound.Runtime.SettingsAssets.Reset();
            var asset = ScriptableObject.CreateInstance<ObjectFieldsHolder>();
            try
            {
                var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
                // A PPtr<MonoBehaviour> takes a script asset, as before (no C# class of that name).
                Assert.True(graphics.FindProperty("m_CustomRenderPipeline").ValidateObjectReferenceValue(asset));
                var audio = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
                // A native enum has no C# type to name its values.
                Assert.Null(EditorGUI.EnumPropertyOptions(audio.FindProperty("Default Speaker Mode")));
                // A node of a C# enum type has its names, made once.
                var key = new SerializedObject(asset).FindProperty("key");
                Assert.Equal(SerializedPropertyType.Enum, key.propertyType);
                Assert.NotNull(EditorGUI.EnumPropertyOptions(key));
                Assert.Same(EditorGUI.EnumPropertyOptions(key), EditorGUI.EnumPropertyOptions(key));
            }
            finally
            {
                ProjectInfo.ProjectPath = before;
                Unbound.Runtime.SettingsAssets.Reset();
                Object.DestroyImmediate(asset);
                System.IO.Directory.Delete(project, true);
            }
        }

        [Fact]
        public void AnEnumsOptionsAreMadeOnce()
        {
            var data = UnityEditor.EnumDataUtility.GetCachedEnumData(typeof(KeyCode), true);
            Assert.Same(EditorGUI.EnumOptions(typeof(KeyCode), false, data), EditorGUI.EnumOptions(typeof(KeyCode), false, data));
        }

        [Fact]
        public void PrepareStartDragResetsTheVisualModeButNotTheActiveControl()
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            DragAndDrop.activeControlID = 5;
            DragAndDrop.objectReferences = new Object[0];
            DragAndDrop.paths = new[] { "Assets/x.mat" };
            Assert.Equal(DragAndDropVisualMode.Copy, DragAndDrop.visualMode);
            DragAndDrop.PrepareStartDrag();
            Assert.Equal(DragAndDropVisualMode.None, DragAndDrop.visualMode);
            Assert.Equal(5, DragAndDrop.activeControlID);
            Assert.Empty(DragAndDrop.paths);
            DragAndDrop.activeControlID = 0;
        }

        [Fact]
        public void OtherFamiliesReportTheFieldsNotImplemented()
        {
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                using var release = TestRelease.Pin(TestRelease.Unmeasured);
                var repaint = new Event { type = EventType.Repaint };
                Assert.Equal(Color.red, Pass(repaint, () => EditorGUI.ColorField(Field, Color.red)));
                Assert.Equal(5, Pass(repaint, () => EditorGUI.MaskField(Field, 5, new[] { "a" })));
                Assert.Equal("Alpha", Pass(repaint, () => EditorGUI.TagField(Field, "Alpha")));
            }
            finally { Debug.unityLogger.logHandler = previous; }
            Assert.Contains(capture.Lines, line => line.Contains("EditorGUI.ColorField is not implemented"));
            Assert.Contains(capture.Lines, line => line.Contains("EditorGUI.MaskField is not implemented"));
            Assert.Contains(capture.Lines, line => line.Contains("EditorGUI.TagField is not implemented"));
        }
    }
}
