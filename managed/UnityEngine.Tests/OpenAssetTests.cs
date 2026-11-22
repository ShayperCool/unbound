using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The test assembly's [OnOpenAsset] methods: each writes down its call and answers,
    /// or throws, as the test tells it.</summary>
    static class OpenAssetTestCallbacks
    {
        internal static readonly List<string> Calls = new List<string>();
        internal static readonly HashSet<string> Answer = new HashSet<string>();
        internal static string Throw;

        static bool Call(string name, string arguments)
        {
            Calls.Add(name + "(" + arguments + ")");
            if (Throw == name) throw new InvalidOperationException("thrown by " + name);
            return Answer.Contains(name);
        }

        [OnOpenAsset(100)]
        static bool Late(int id, int line, int column) => Call("Late", line + "," + column);

        [OnOpenAsset(-5)]
        static bool Early(int id) => Call("Early", "");

        [OnOpenAsset]
        static bool Default(int id, int line) => Call("Default", line.ToString());

        [OnOpenAsset(0)]
        static bool Zero(int id, int line, int column) => Call("Zero", line + "," + column);

        [OnOpenAsset(3)]
        static bool Three(int id) => Call("Three", "");

        [OnOpenAsset(OnOpenAssetAttributeMode.Validate)]
        static bool Validate(int id) => Call("Validate", "");

        [OnOpenAsset(-7, OnOpenAssetAttributeMode.Validate)]
        static bool ValidateEarly(int id, int line) => Call("ValidateEarly", line.ToString());

        [OnOpenAsset(-9)]
        static void WrongReturn(int id) => Call("WrongReturn", "");

        [OnOpenAsset(-9)]
        static bool WrongParameter(string id) => Call("WrongParameter", "");

        [OnOpenAsset(-9, OnOpenAssetAttributeMode.Validate)]
        static void ValidateWrongReturn(int id) => Call("ValidateWrongReturn", "");

        internal sealed class Holder
        {
            [OnOpenAsset(-9)]
            bool Instance(int id) => Call("Instance", "");
        }
    }

    /// <summary>AssetDatabase.OpenAsset, CanOpenAssetInEditor and [OnOpenAsset] as the Unity
    /// 6000.0.71f1 Editor runs them in Play mode (tests/projects/openasset).</summary>
    [Collection("EditorState")]
    public sealed class OpenAssetTests : IDisposable
    {
        const string Table = """
            [
              {"guid":"00000000000000001000000000000000","path":"Assets","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"0a000000000000000000000000000001","path":"Assets/Folder","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"0a000000000000000000000000000002","path":"Assets/Plain.txt","kind":"text","importer":"TextScriptImporter","labels":[]},
              {"guid":"0a000000000000000000000000000003","path":"Assets/Clip.anim","kind":"yaml","importer":"NativeFormatImporter","labels":[]},
              {"guid":"0a000000000000000000000000000004","path":"Assets/Thing.prefab","kind":"prefab","importer":"PrefabImporter","labels":[]},
              {"guid":"0a000000000000000000000000000005","path":"Assets/Atlas.spriteatlasv2","kind":"yaml","importer":"SpriteAtlasImporter","labels":[]},
              {"guid":"0a000000000000000000000000000006","path":"Assets/Code.asmdef","kind":"text","importer":"AssemblyDefinitionImporter","labels":[]},
              {"guid":"0a000000000000000000000000000007","path":"Assets/Code.asmref","kind":"text","importer":"AssemblyDefinitionReferenceImporter","labels":[]},
              {"guid":"0a000000000000000000000000000008","path":"Assets/Main.unity","kind":"scene","importer":"DefaultImporter","labels":[]}
            ]
            """;

        readonly List<Assembly> m_Assemblies;
        readonly Assembly[] m_OriginalAssemblies;
        readonly FieldInfo m_Generation;
        readonly int m_OriginalGeneration;
        readonly List<string> m_Guids = new List<string>();
        readonly List<Object> m_Objects = new List<Object>();
        readonly List<string> m_Logs = new List<string>();
        readonly Application.LogCallback m_OnLog;
        readonly string m_Directory, m_OpenerLog, m_PreviousOpener;

        readonly GameObject m_Selected, m_Scene;
        readonly TextAsset m_Text;
        readonly AnimationClip m_Clip, m_ClipPart;
        readonly DefaultAsset m_Folder;

        public OpenAssetTests()
        {
            m_Assemblies = (List<Assembly>)typeof(GameDomain).GetField("s_Assemblies", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            m_Generation = typeof(GameDomain).GetField("s_Generation", BindingFlags.Static | BindingFlags.NonPublic);
            m_OriginalAssemblies = m_Assemblies.ToArray();
            m_OriginalGeneration = (int)m_Generation.GetValue(null);
            m_Assemblies.Clear();
            m_Assemblies.Add(typeof(OpenAssetTests).Assembly);
            m_Generation.SetValue(null, m_OriginalGeneration + 1);

            AssetDatabase.List(Table);
            m_Selected = Keep(new GameObject("Selected"));
            m_Scene = Keep(new GameObject("Scene"));
            m_Folder = Adopt(1, "Assets/Folder", DefaultAsset.Create("Folder", HideFlags.NotEditable), DefaultAsset.FileId);
            m_Text = Adopt(2, "Assets/Plain.txt", new TextAsset("text") { name = "Plain" }, 4900000);
            m_Clip = Adopt(3, "Assets/Clip.anim", new AnimationClip { name = "Clip" }, 7400000);
            m_ClipPart = Keep(new AnimationClip { name = "Part" });
            AssetDatabaseRuntime.AddObject(AssetDatabaseRuntime.Adopt(m_Guids[2], "Assets/Clip.anim", m_Clip, 7400000), m_ClipPart, 1234);

            m_Directory = Path.Combine(Path.GetTempPath(), "unbound-openasset-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Directory);
            m_OpenerLog = Path.Combine(m_Directory, "opened.txt");
            var opener = Path.Combine(m_Directory, "opener.sh");
            File.WriteAllText(opener, "#!/bin/sh\nprintf '%s\\n' \"$1\" >> '" + m_OpenerLog + "'\n");
            File.SetUnixFileMode(opener, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            m_PreviousOpener = Environment.GetEnvironmentVariable("UNBOUND_OPEN_URL");
            Environment.SetEnvironmentVariable("UNBOUND_OPEN_URL", opener);

            OpenAssetTestCallbacks.Calls.Clear();
            OpenAssetTestCallbacks.Answer.Clear();
            OpenAssetTestCallbacks.Throw = null;
            Selection.activeObject = m_Selected;
            m_OnLog = (condition, _, type) => m_Logs.Add(type + ":" + condition);
            Application.logMessageReceived += m_OnLog;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= m_OnLog;
            Environment.SetEnvironmentVariable("UNBOUND_OPEN_URL", m_PreviousOpener);
            Directory.Delete(m_Directory, true);
            Selection.activeObject = null;
            foreach (var guid in m_Guids) AssetDatabaseRuntime.Invalidate(guid);
            foreach (var o in m_Objects) Object.DestroyImmediate(o, true);
            AssetDatabase.Invalidate();
            m_Assemblies.Clear();
            m_Assemblies.AddRange(m_OriginalAssemblies);
            m_Generation.SetValue(null, m_OriginalGeneration);
        }

        T Keep<T>(T o) where T : Object
        {
            m_Objects.Add(o);
            return o;
        }

        T Adopt<T>(int number, string path, T main, long fileId) where T : Object
        {
            var guid = "0a0000000000000000000000000000" + number.ToString("x2");
            m_Guids.Add(guid);
            AssetDatabaseRuntime.Adopt(guid, path, main, fileId);
            return Keep(main);
        }

        sealed class Recorder : ILogHandler
        {
            internal readonly List<string> Logs = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Logs.Add(logType + ":" + string.Format(format, args));
            public void LogException(Exception exception, Object context) => Logs.Add("Exception:" + exception.Message);
        }

        static string Calls() => string.Join(" ", OpenAssetTestCallbacks.Calls);

        const string ExecuteShapeReports =
            "Error:Error: UnityEngine.Tests.OpenAssetTestCallbacks:WrongReturn (int) does not match any of [OnOpenAssetAttribute] expected signatures.\n"
            + "Valid signatures are: static Boolean WrongReturn(Int32) , static Boolean WrongReturn(Int32, Int32) , static Boolean WrongReturn(Int32, Int32, Int32)|"
            + "Error:Error: UnityEngine.Tests.OpenAssetTestCallbacks:WrongParameter (string) does not match any of [OnOpenAssetAttribute] expected signatures.\n"
            + "Valid signatures are: static Boolean WrongParameter(Int32) , static Boolean WrongParameter(Int32, Int32) , static Boolean WrongParameter(Int32, Int32, Int32)|"
            + "Error:Error: UnityEngine.Tests.OpenAssetTestCallbacks/Holder:Instance (int) does not match any of [OnOpenAssetAttribute] expected signatures.\n"
            + "Valid signatures are: static Boolean Instance(Int32) , static Boolean Instance(Int32, Int32) , static Boolean Instance(Int32, Int32, Int32)";

        const string ValidateShapeReport =
            "Error:Error: UnityEngine.Tests.OpenAssetTestCallbacks:ValidateWrongReturn (int) does not match any of [OnOpenAssetAttribute] expected signatures.\n"
            + "Valid signatures are: static Boolean ValidateWrongReturn(Int32) , static Boolean ValidateWrongReturn(Int32, Int32) , static Boolean ValidateWrongReturn(Int32, Int32, Int32)";

        [Fact]
        public void AnObjectThatIsNoAssetIsSelectedWithoutAskingAnyMethod()
        {
            OpenAssetTestCallbacks.Answer.Add("Early");
            Assert.True(AssetDatabase.OpenAsset(m_Scene.GetInstanceID(), 7, 3));
            Assert.Same(m_Scene, Selection.activeObject);
            Assert.Empty(OpenAssetTestCallbacks.Calls);
            Assert.Empty(m_Logs);
            Assert.False(AssetDatabase.CanOpenAssetInEditor(m_Scene.GetInstanceID()));
            Assert.Empty(OpenAssetTestCallbacks.Calls);
        }

        [Fact]
        public void NoObjectOpensNothing()
        {
            var destroyed = new GameObject("Destroyed");
            Object.DestroyImmediate(destroyed);
            Assert.False(AssetDatabase.OpenAsset(0));
            Assert.False(AssetDatabase.OpenAsset(2000000001));
            Assert.False(AssetDatabase.OpenAsset((Object)null));
            Assert.False(AssetDatabase.OpenAsset((Object)null, 7, 3));
            Assert.False(AssetDatabase.OpenAsset(destroyed));
            Assert.False(AssetDatabase.CanOpenAssetInEditor(0));
            Assert.False(AssetDatabase.CanOpenAssetInEditor(2000000001));
            Assert.Same(m_Selected, Selection.activeObject);
            Assert.Empty(OpenAssetTestCallbacks.Calls);
            Assert.Empty(m_Logs);
        }

        [Fact]
        public void ExecuteMethodsAreAskedByOrderUntilOneAnswersAndValidatorsAreNot()
        {
            OpenAssetTestCallbacks.Answer.UnionWith(new[] { "Validate", "ValidateEarly", "Default", "Three" });
            Assert.True(AssetDatabase.OpenAsset(m_Text, 7, 3));
            Assert.Equal("Early() Zero(7,3) Default(7)", Calls());
            // A method that opened the asset leaves the selection alone.
            Assert.Same(m_Selected, Selection.activeObject);
            // The methods of another shape are reported, and never called.
            Assert.Equal(ExecuteShapeReports, string.Join("|", m_Logs));
        }

        [Fact]
        public void TheLineAndColumnDefaultToMinusOne()
        {
            OpenAssetTestCallbacks.Answer.Add("Late");
            Assert.True(AssetDatabase.OpenAsset(m_Text.GetInstanceID()));
            Assert.Equal("Early() Zero(-1,-1) Default(-1) Three() Late(-1,-1)", Calls());
            OpenAssetTestCallbacks.Calls.Clear();
            Assert.True(AssetDatabase.OpenAsset(m_Text, 7));
            Assert.Equal("Early() Zero(7,-1) Default(7) Three() Late(7,-1)", Calls());
        }

        [Fact]
        public void MethodsOfAnotherShapeAreReportedOnEveryCall()
        {
            OpenAssetTestCallbacks.Answer.Add("Early");
            AssetDatabase.OpenAsset(m_Text);
            AssetDatabase.OpenAsset(m_Text);
            Assert.Equal(ExecuteShapeReports + "|" + ExecuteShapeReports, string.Join("|", m_Logs));
        }

        [Fact]
        public void AMethodThatThrowsIsLoggedAndTheNextIsAsked()
        {
            OpenAssetTestCallbacks.Throw = "Early";
            OpenAssetTestCallbacks.Answer.UnionWith(new[] { "Early", "Zero" });
            Assert.True(AssetDatabase.OpenAsset(m_Text));
            Assert.Equal("Early() Zero(-1,-1)", Calls());
            Assert.Equal("Exception:Exception thrown while invoking [OnOpenAssetAttribute] method 'UnityEngine.Tests.OpenAssetTestCallbacks:Early (int)' : InvalidOperationException: thrown by Early",
                m_Logs.Last());
        }

        [Fact]
        public void AnArrayOpensEveryObjectAndAnswersForAll()
        {
            OpenAssetTestCallbacks.Answer.Add("Early");
            Assert.True(AssetDatabase.OpenAsset(new Object[] { m_Text, m_Clip }));
            Assert.Equal("Early() Early()", Calls());
            OpenAssetTestCallbacks.Calls.Clear();
            // A null entry fails the whole answer; the entries after it are still opened.
            Assert.False(AssetDatabase.OpenAsset(new Object[] { m_Text, null, m_Clip, m_Scene }));
            Assert.Equal("Early() Early()", Calls());
            Assert.Same(m_Scene, Selection.activeObject);
            Assert.True(AssetDatabase.OpenAsset(new Object[0]));
            Assert.Throws<NullReferenceException>(() => AssetDatabase.OpenAsset((Object[])null));
        }

        [Fact]
        public void AnUnopenedNativeAssetOrFolderIsSelectedAndCountsAsOpened()
        {
            Assert.True(AssetDatabase.OpenAsset(m_Clip));
            Assert.Equal("Early() Zero(-1,-1) Default(-1) Three() Late(-1,-1)", Calls());
            Assert.Same(m_Clip, Selection.activeObject);
            Assert.True(AssetDatabase.OpenAsset(m_ClipPart));
            Assert.Same(m_ClipPart, Selection.activeObject);
            Assert.True(AssetDatabase.OpenAsset(m_Folder));
            Assert.Same(m_Folder, Selection.activeObject);
            Assert.False(File.Exists(m_OpenerLog));
        }

        [Fact]
        public void AnUnopenedFileOfAnotherFormatGoesToTheDesktopOpener()
        {
            Assert.True(AssetDatabase.OpenAsset(m_Text));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(m_OpenerLog) && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(20);
            Assert.Equal(AssetDatabase.FileOf("Assets/Plain.txt"), File.ReadAllText(m_OpenerLog).TrimEnd('\n'));
            Assert.Same(m_Selected, Selection.activeObject);
            // An opener that cannot be started: the asset is not opened, and Unbound says why.
            Environment.SetEnvironmentVariable("UNBOUND_OPEN_URL", Path.Combine(m_Directory, "missing-opener"));
            Assert.False(AssetDatabase.OpenAsset(m_Text));
        }

        [Fact]
        public void AnUnopenedSceneOrPrefabIsRefusedLoudly()
        {
            var scene = Adopt(8, "Assets/Main.unity", new SceneAsset(), 102900000);
            var prefab = Adopt(4, "Assets/Thing.prefab", new GameObject("Thing"), 100100000);
            // Unbound's own diagnostics reach the log handler, not the game's log callbacks.
            var handler = new Recorder();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = handler;
            try
            {
                Assert.False(AssetDatabase.OpenAsset(scene));
                Assert.False(AssetDatabase.OpenAsset(prefab));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
            Assert.Equal(new[]
            {
                "Warning:Unbound: AssetDatabase.OpenAsset does not load the scene 'Assets/Main.unity' yet; use EditorSceneManager.OpenScene.",
                "Warning:Unbound: AssetDatabase.OpenAsset does not open the prefab 'Assets/Thing.prefab': Unbound has no Prefab Mode.",
            }, handler.Logs.Where(l => l.StartsWith("Warning:", StringComparison.Ordinal)));
            Assert.Same(m_Selected, Selection.activeObject);
            Assert.False(File.Exists(m_OpenerLog));
        }

        [Fact]
        public void TheEditorOpensFoldersScenesAtlasesPrefabsAndAssemblyDefinitionsItself()
        {
            var scene = Adopt(8, "Assets/Main.unity", new SceneAsset(), 102900000);
            var prefab = Adopt(4, "Assets/Thing.prefab", new GameObject("Thing"), 100100000);
            var atlas = Adopt(5, "Assets/Atlas.spriteatlasv2", new SpriteAtlas(), 4343727234628468602);
            var definition = Adopt(6, "Assets/Code.asmdef", new TextAsset("{}") { name = "Code" }, 5897886265953266890);
            var reference = Adopt(7, "Assets/Code.asmref", new TextAsset("{}") { name = "Code" }, 5897886265953266890);
            foreach (var target in new Object[] { m_Folder, scene, prefab, atlas, definition, reference })
                Assert.True(AssetDatabase.CanOpenAssetInEditor(target.GetInstanceID()), target.GetType().Name + " " + AssetDatabase.GetAssetPath(target));
            // No validator is asked about them, so none is reported either.
            Assert.Empty(OpenAssetTestCallbacks.Calls);
            Assert.Empty(m_Logs);
        }

        [Fact]
        public void ValidatorsAreAskedByOrderAndThenTheAssetsFormatDecides()
        {
            // A text file is not of the Editor's own format: only a validator makes it openable.
            Assert.False(AssetDatabase.CanOpenAssetInEditor(m_Text.GetInstanceID()));
            Assert.Equal("ValidateEarly(-1) Validate()", Calls());
            Assert.Equal(ValidateShapeReport, string.Join("|", m_Logs));
            OpenAssetTestCallbacks.Calls.Clear();
            OpenAssetTestCallbacks.Answer.UnionWith(new[] { "ValidateEarly", "Early", "Zero" });
            Assert.True(AssetDatabase.CanOpenAssetInEditor(m_Text.GetInstanceID()));
            Assert.Equal("ValidateEarly(-1)", Calls());
            // The Execute methods are not asked: their answers do not count.
            OpenAssetTestCallbacks.Calls.Clear();
            OpenAssetTestCallbacks.Answer.Remove("ValidateEarly");
            Assert.False(AssetDatabase.CanOpenAssetInEditor(m_Text.GetInstanceID()));
            Assert.Equal("ValidateEarly(-1) Validate()", Calls());
            // A native asset, and any object of its file, is openable though every validator says no.
            OpenAssetTestCallbacks.Calls.Clear();
            Assert.True(AssetDatabase.CanOpenAssetInEditor(m_Clip.GetInstanceID()));
            Assert.True(AssetDatabase.CanOpenAssetInEditor(m_ClipPart.GetInstanceID()));
            Assert.Equal("ValidateEarly(-1) Validate() ValidateEarly(-1) Validate()", Calls());
            Assert.Same(m_Selected, Selection.activeObject);
        }

        [Fact]
        public void AValidatorThatThrowsIsLoggedAndCountsAsNo()
        {
            OpenAssetTestCallbacks.Throw = "ValidateEarly";
            OpenAssetTestCallbacks.Answer.Add("ValidateEarly");
            Assert.False(AssetDatabase.CanOpenAssetInEditor(m_Text.GetInstanceID()));
            Assert.Equal("ValidateEarly(-1) Validate()", Calls());
            Assert.Equal("Exception:Exception thrown while invoking [OnOpenAssetAttribute] method 'UnityEngine.Tests.OpenAssetTestCallbacks:ValidateEarly (int,int)' : InvalidOperationException: thrown by ValidateEarly",
                m_Logs.Last());
        }

        [Fact]
        public void TheAttributeKeepsItsOrderAndMode()
        {
            Assert.Equal(1, new OnOpenAssetAttribute().callbackOrder);
            Assert.Equal(OnOpenAssetAttributeMode.Execute, new OnOpenAssetAttribute().attributeMode);
            Assert.Equal(1, new OnOpenAssetAttribute(OnOpenAssetAttributeMode.Validate).callbackOrder);
            Assert.Equal(OnOpenAssetAttributeMode.Validate, new OnOpenAssetAttribute(OnOpenAssetAttributeMode.Validate).attributeMode);
            Assert.Equal(-4, new OnOpenAssetAttribute(-4).callbackOrder);
            Assert.Equal(OnOpenAssetAttributeMode.Validate, new OnOpenAssetAttribute(9, OnOpenAssetAttributeMode.Validate).attributeMode);
        }
    }
}
