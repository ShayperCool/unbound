using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

static class SelectedAssetPathProbe
{
    [Serializable] struct Row { public string k; public string v; }

    sealed class Capture : ILogHandler
    {
        internal readonly List<string> Messages = new List<string>();

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) =>
            Messages.Add(logType + ":" + string.Format(format, args));

        public void LogException(Exception error, UnityEngine.Object context) =>
            Messages.Add("Exception:" + error.GetType().Name);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var output = Environment.GetEnvironmentVariable("SELECTED_ASSET_PATH_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var rows = new List<string>();
        var method = typeof(AssetDatabase).GetMethod("GetUniquePathNameAtSelectedPath", BindingFlags.Static | BindingFlags.NonPublic);
        void Add(string key, string value) => rows.Add(JsonUtility.ToJson(new Row { k = key, v = value ?? "<null>" }));
        string PathFor(string name)
        {
            try { return (string)method.Invoke(null, new object[] { name }); }
            catch (TargetInvocationException error) { return error.InnerException.GetType().Name; }
        }

        Directory.CreateDirectory("Assets/Selection/Child");
        File.WriteAllText("Assets/Name.txt", "root's existing name");
        File.WriteAllText("Assets/Selection/Name.txt", "selection folder's existing name");
        File.WriteAllText("Assets/Selection/Child/Name.txt", "child folder's existing name");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var root = AssetDatabase.LoadMainAssetAtPath("Assets");
        var folder = AssetDatabase.LoadMainAssetAtPath("Assets/Selection");
        var child = AssetDatabase.LoadMainAssetAtPath("Assets/Selection/Child");
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Selection/Child/Name.txt");
        var transient = new GameObject("Temporary selection");
        var saved = Selection.objects;
        try
        {
            foreach (var (key, selected) in new (string, UnityEngine.Object)[]
                     { ("none", null), ("root", root), ("folder", folder), ("child", child), ("asset", asset), ("scene", transient) })
            {
                Selection.activeObject = selected;
                Add(key + ".selected", AssetDatabase.GetAssetPath(Selection.activeObject));
                Add(key + ".collision", PathFor("Name.txt"));
                Add(key + ".noncollision", PathFor("Available.txt"));
            }
            Selection.objects = new UnityEngine.Object[] { child, folder };
            Add("multi.selected", AssetDatabase.GetAssetPath(Selection.activeObject));
            Add("multi.result", PathFor("Name.txt"));
            Selection.activeObject = folder;
            Add("name.null", PathFor(null));
            Add("name.empty", PathFor(""));
            Add("name.prefixed", PathFor("Assets/Name.txt"));
            Add("name.slash", PathFor("Child/Name.txt"));
            Add("name.backslash", PathFor("Child\\Name.txt"));
            Add("name.reservedAscii", PathFor("a:b*c?d|e<f>g\"h.txt"));
            Add("name.period", PathFor("Thing."));
            Add("name.whitespace", PathFor(" Name.txt "));
            Add("name.onlyWhitespace", PathFor(" "));
            Add("name.tab", PathFor("A\tB.txt"));
            Add("name.newline", PathFor("A\nB.txt"));
            Add("name.dot", PathFor("."));
            Add("name.dotDot", PathFor(".."));
            Add("name.absolute", PathFor("/private/Name.txt"));
            Add("name.prefix", PathFor("Name 1.txt"));
            Selection.activeObject = asset;
            AssetDatabase.DeleteAsset("Assets/Selection/Child/Name.txt");
            Add("deleted.active", AssetDatabase.GetAssetPath(Selection.activeObject));
            Add("deleted.result", PathFor("Name.txt"));
            const string package = "Packages/com.unbound.selected-fixture";
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(package + "/Contents/Payload.txt");
            Add("package.asset.selected", AssetDatabase.GetAssetPath(Selection.activeObject));
            Add("package.asset.result", PathFor("New.txt"));
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(package + "/Contents");
            Add("package.folder.selected", AssetDatabase.GetAssetPath(Selection.activeObject));
            Add("package.folder.result", PathFor("New.txt"));
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath("Packages/com.unity.modules.imgui");
            Add("builtinPackage.selected", AssetDatabase.GetAssetPath(Selection.activeObject));
            Add("builtinPackage.result", PathFor("New.txt"));
            Selection.activeObject = folder;
            Add("name.leadingTab", PathFor("\tA.txt"));
            Add("name.trailingTab", PathFor("A.txt\t"));
            Add("name.leadingNbsp", PathFor("\u00a0A.txt"));
            Add("name.trailingNbsp", PathFor("A.txt\u00a0"));
            Add("name.trailingDotsSpaces", PathFor("A.txt . ."));
            Add("name.dotPrefix", PathFor(".hidden"));
            Add("name.reservedWord", PathFor("CON.txt"));
            Add("name.control", PathFor("A\u0001B.txt"));
            foreach (var (key, name) in new (string, string)[]
                     { ("dotsPrefix", "...a.txt"), ("dotsSuffix", "A.txt..."), ("spacesSuffix", "A.txt   "),
                       ("spacesPrefix", "   A.txt"), ("dotAfterLeadingSpaces", "  ..A.txt"), ("hiddenBoth", ".x."),
                       ("spacesOnly", "    "), ("spacesAndDots", " . . "), ("nul", "A\0B.txt"), ("nulFirst", "\0B.txt"),
                       ("nulLast", "A.txt\0"), ("reservedLower", "con.txt"), ("reservedWithSpace", "CON .txt"),
                       ("reservedNone", "PRN"), ("reservedMultiExt", "AUX.foo.bar"), ("reservedDigit", "COM1.txt"),
                       ("reservedZero", "COM0.txt"), ("reservedTwoDigits", "COM10.txt"), ("reservedUpper", "LPT9.txt"),
                       ("reservedSuperscript", "LPT\u00b2.txt"), ("reservedConsole", "CONOUT$.txt"),
                       ("caseCollision", "name.txt"), ("numberedCollision", "Name 5.txt"), ("tilde", "A.txt~"),
                       ("hiddenTmp", "A.tmp"), ("noExtension", "Available"), ("longName", new string('X', 260) + ".txt") })
                Add("names." + key, PathFor(name));
            File.WriteAllText("Assets/Selection/Name 5.txt", "numbered name");
            File.WriteAllText("Assets/Selection/Name 1.txt", "first numbered name");
            File.WriteAllText("Assets/Selection/MetaOnly.txt.meta", "fileFormatVersion: 2\nguid: 5480961b136049fb86861cd8211eb0bd\n");
            Add("names.numberedCollisionExternal", PathFor("Name 5.txt"));
            Add("names.metaOnlyExternal", PathFor("MetaOnly.txt"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Add("names.numberedCollisionListed", PathFor("Name 5.txt"));
            foreach (var name in new[] { "First 0.txt", "First 0005.txt", "First5.txt", "First-5.txt", "First  5.txt", "First 2147483647.txt",
                                          "First 9999999999999999999.txt", "First\t5.txt", "First ７.txt", "First\n5.txt" })
            {
                File.WriteAllText("Assets/Selection/" + name, "same name");
                Add("suffix." + name, PathFor(name));
            }
            Directory.CreateDirectory("Assets/Selection/Existing Folder");
            Add("names.directoryCollision", PathFor("existing folder"));
            File.WriteAllText("Assets/Selection/Name 6.txt", "next numbered name");
            Add("names.numberedTwoCollisions", PathFor("Name 5.txt"));
            string unreserved = PathFor("Available.txt");
            Add("names.resultUnreserved", (File.Exists(unreserved) || File.Exists(unreserved + ".meta")).ToString());
            Add("names.repeatUnreserved", PathFor("Available.txt"));
            File.WriteAllText("Assets/Selection/First 2147483646.txt", "penultimate number");
            Add("names.numberedLimitCollision", PathFor("First 2147483646.txt"));
            foreach (var name in new[] { "NUL", "CLOCK$", "LPT0", "lpt8 .txt", "PRN..txt", "COM2 ", "NUL...", ".NUL", " A . ", "a/b" })
                Add("validation." + name, PathFor(name));
            Selection.activeObject = folder;
            var frame = typeof(ProjectWindowUtil).GetMethod("FrameObjectInProjectWindow", BindingFlags.Static | BindingFlags.NonPublic);
            var capture = new Capture();
            var handler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                frame.Invoke(null, new object[] { child.GetInstanceID() });
                frame.Invoke(null, new object[] { 0 });
                frame.Invoke(null, new object[] { int.MaxValue });
            }
            finally { Debug.unityLogger.logHandler = handler; }
            Add("frame.logs", string.Join("|", capture.Messages));
            Add("frame.selected", AssetDatabase.GetAssetPath(Selection.activeObject));
            File.Delete("Assets/Selection/Name.txt");
            Add("names.deletedSourceName", PathFor("Name.txt"));
            Selection.activeObject = child;
            Directory.Delete("Assets/Selection/Child", true);
            Add("folder.externallyRemoved.selected", AssetDatabase.GetAssetPath(Selection.activeObject));
            capture.Messages.Clear();
            Debug.unityLogger.logHandler = capture;
            try { Add("folder.externallyRemoved.result", PathFor("Name.txt")); }
            finally { Debug.unityLogger.logHandler = handler; }
            Add("folder.externallyRemoved.logs", string.Join("|", capture.Messages));
            Selection.activeObject = folder;
            File.WriteAllText("Assets/Selection/Second 2147483646.txt", "penultimate number");
            Add("names.numberedLimitFree", PathFor("Second 2147483646.txt"));
        }
        finally
        {
            Selection.objects = saved;
            UnityEngine.Object.DestroyImmediate(transient);
        }
        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), rows);
        EditorApplication.Exit(0);
    }
}
