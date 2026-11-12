using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;
using Object = UnityEngine.Object;

// References into a ScriptedImporter's asset whose file id names none of its objects. Measured in
// the Unity 6000.0.71f1 Editor (tests/projects/uxmlattributes, arrayScripted*: an asset of two
// ProbeData objects): such a reference is empty, silently, whether no object or several objects
// of the expected type exist. Unbound's choice, unmeasured: exactly one fitting object is taken.

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
        public void AnUnknownFileIdIsEmptyWithoutAMessageUnlessOneObjectFits()
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
                // One of the expected type among others (control: Unbound's choice).
                var texture = new Texture2D(1, 1);
                Assert.Same(texture, AssetDatabaseRuntime.ByProducedObjects(File(a, b, texture), typeof(Texture2D)));
                Assert.Empty(logs.Lines);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
        }
    }
}
