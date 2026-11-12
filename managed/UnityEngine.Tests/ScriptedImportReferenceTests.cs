using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;
using Object = UnityEngine.Object;

// References into a ScriptedImporter's asset whose file id names none of its objects. Measured in
// the Unity 6000.0.71f1 Editor (tests/projects/uxmlattributes, arrayScripted*: an asset of two
// ProbeData objects): such a reference is empty, silently, whether no object or several objects
// of the expected type exist. Unbound's choice: a reference that may name a component or child of
// an added GameObject (which Unbound gives no file id) takes the one fitting object, or reports.

namespace UnityEngine.Tests
{
    public class ScriptedImportReferenceTests
    {
        sealed class Data : ScriptableObject { }

        sealed class Logs : ILogHandler
        {
            public readonly List<string> Lines = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
        }

        static LoadedFile File(params Object[] objects)
        {
            var file = new LoadedFile { Path = "Assets/UI/Probe.probedata", UnknownFileIds = true };
            for (int i = 0; i < objects.Length; i++)
            {
                file.Objects[100 + i] = objects[i];
                file.Identifiers[objects[i]] = "o" + i;
            }
            return file;
        }

        [Fact]
        public void AnUnknownFileIdIsEmptyWithoutAMessage()
        {
            var handler = Debug.unityLogger.logHandler;
            var logs = new Logs();
            Debug.unityLogger.logHandler = logs;
            try
            {
                Data a = ScriptableObject.CreateInstance<Data>(), b = ScriptableObject.CreateInstance<Data>();
                var two = File(a, b);
                // None of the expected type, then two of it.
                Assert.Null(AssetDatabaseRuntime.ByProducedObjects(two, typeof(Texture2D)));
                Assert.Null(AssetDatabaseRuntime.ByProducedObjects(two, typeof(Data)));
                // One of the expected type among others, and an asset of a single object: still
                // resolved by file id alone.
                Assert.Null(AssetDatabaseRuntime.ByProducedObjects(File(a, b, new Texture2D(1, 1)), typeof(Texture2D)));
                Assert.Null(AssetDatabaseRuntime.ByProducedObjects(File(a), typeof(Data)));
                Assert.Empty(logs.Lines);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
        }

        // The .meta's name tables give an added object its file id when the entry names its identifier
        // and its class (matches the Unity 6000.0.71f1 Editor with a texture and a script object,
        // tests/projects/scriptedfileids, and with a Shader Graph's shader, tests/projects/shadergraph).
        [Theory]
        [InlineData("  fileIDToRecycleName:\n    2800000: MainAsset\n", 2800000L, false)]
        [InlineData("  fileIDToRecycleName:\n    11400000: MainAsset\n", 0L, false)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      28: 1234567\n    second: MainAsset\n", 1234567L, false)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      114: 7654321\n    second: MainAsset\n", 0L, false)]
        [InlineData("  internalIDToNameTable: []\n", 0L, false)]
        [InlineData("  fileIDToRecycleName:\n    2800000: Other\n", 0L, false)]
        [InlineData("  fileIDToRecycleName:\n    2800000: MainAsset\n    11400000: MainAsset\n", 11400000L, true)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      114: 7654321\n    second: MainAsset\n", 7654321L, true)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      114: 7654321\n      28: 1234567\n    second: MainAsset\n", 1234567L, false)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      28: \n    second: MainAsset\n", 0L, false)]
        public void TheMetaNameTableGivesFileIdsByIdentifierAndClass(string table, long expected, bool scriptObject)
        {
            var settings = TestYaml.DocumentBody("ScriptedImporter:\n" + table + "  externalObjects: {}\n  userData: \n");
            Object obj = scriptObject ? ScriptableObject.CreateInstance<Data>() : new Texture2D(1, 1);
            var derived = ScriptedImports.FileId("MainAsset", obj);
            Assert.Equal(expected == 0 ? derived : expected, ScriptedImports.FileId("MainAsset", obj, ScriptedImports.NameTable(settings)));
        }

        [Fact]
        public void TheDerivedFileIdIsXxHashOfClassAndIdentifier()
        {
            // The id newer Shader Graph assets are referenced by (no name table in their .meta).
            Assert.Equal(-6465566751694194690L, (long)ScriptedImports.XxHash64(System.Text.Encoding.UTF8.GetBytes("Type:Shader->MainAsset0")));
        }

        // A reference that may name a component of an added GameObject (no derived file id): the one
        // fitting object, else a report.
        [Fact]
        public void AReferenceThatMayNameAComponentFallsBackOrReports()
        {
            var handler = Debug.unityLogger.logHandler;
            var logs = new Logs();
            Debug.unityLogger.logHandler = logs;
            try
            {
                var root = new GameObject("Root");
                var data = ScriptableObject.CreateInstance<Data>();
                var file = File(root, data);
                Assert.Same(root, AssetDatabaseRuntime.ByProducedObjects(file, typeof(GameObject)));
                Assert.Empty(logs.Lines);
                Assert.Null(AssetDatabaseRuntime.ByProducedObjects(file, typeof(MeshRenderer)));
                Assert.Single(logs.Lines);
                Assert.Contains("cannot tell which one", logs.Lines[0]);
                Object.DestroyImmediate(root);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
        }
    }
}
