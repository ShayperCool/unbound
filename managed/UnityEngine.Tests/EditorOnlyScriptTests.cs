using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>Default-resources scripts that name Editor classes Unbound does not load (the Tree
    /// Creator's tree data, a sub-asset of every Tree Creator prefab: tests/projects/treebillboards
    /// Resources/Creator) are reported by their class, not as a missing script; other unknown
    /// default-resources scripts are still missing.</summary>
    public class EditorOnlyScriptTests : IDisposable
    {
        const string DefaultResources = "0000000000000000e000000000000000";
        readonly List<string> m_Logs = new List<string>();
        readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;

        public EditorOnlyScriptTests() => Debug.unityLogger.logHandler = new Capture(m_Previous, m_Logs);

        public void Dispose() => Debug.unityLogger.logHandler = m_Previous;

        sealed class Capture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;
            internal Capture(ILogHandler inner, List<string> logs) { m_Inner = inner; m_Logs = logs; }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                m_Logs.Add(type + ": " + (args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args)));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        [Fact]
        public void TreeCreatorDataIsNamedNotMissing()
        {
            Assert.Equal("TreeEditor.TreeData", EngineScripts.EditorOnlyClass(DefaultResources, 12106));
            Assert.Null(GameDomain.ResolveScript(DefaultResources, 12106));
            MissingScripts.Report(new ObjectRef(12106, DefaultResources, 0),
                new YDocument { ClassId = 114, FileId = 11400000, TypeName = "MonoBehaviour" });
            var warning = Assert.Single(m_Logs);
            Assert.StartsWith("Warning: Unbound does not load the Editor's TreeEditor.TreeData (MonoBehaviour 11400000", warning);
        }

        [Fact]
        public void OtherDefaultResourcesScriptsAreMissing()
        {
            Assert.Null(EngineScripts.EditorOnlyClass(DefaultResources, 12998));
            Assert.Null(EngineScripts.EditorOnlyClass("1234567890abcdef1234567890abcdef", 12106));
            MissingScripts.Report(new ObjectRef(12998, DefaultResources, 0),
                new YDocument { ClassId = 114, FileId = 11400002, TypeName = "MonoBehaviour" });
            var warning = Assert.Single(m_Logs);
            Assert.StartsWith($"Warning: The referenced script (guid {DefaultResources}, fileID 12998) on this Behaviour is missing!", warning);
        }
    }
}
