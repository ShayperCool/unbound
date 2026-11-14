using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class ComponentDiagnosticLogTests
    {
        [DisallowMultipleComponent]
        sealed class Unique : MonoBehaviour { }

        sealed class Handler : ILogHandler
        {
            readonly ILogHandler m_Next;
            internal readonly List<LogType> Types = new List<LogType>();
            internal Handler(ILogHandler next) => m_Next = next;
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                Types.Add(type);
                m_Next.LogFormat(type, context, format, args);
            }
            public void LogException(Exception error, Object context) => m_Next.LogException(error, context);
        }

        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("6000.0.71f1", false)]
        [InlineData("6000.0.70f1", true)]
        [InlineData("6000.0.70f1", false)]
        public void RejectionUsesTheMeasuredRouteAndPublicLogsKeepTheirLogger(string release, bool enabled)
        {
            string previousRelease = ProjectInfo.UnityVersion;
            var logger = Debug.unityLogger;
            var previousHandler = logger.logHandler;
            bool previousEnabled = logger.logEnabled;
            var previousFilter = logger.filterLogType;
            var handler = new Handler(previousHandler);
            var main = new List<LogType>();
            var threaded = new List<LogType>();
            const string name = "ComponentDiagnosticLogTests";
            Application.LogCallback onMain = (message, trace, type) => { if (message.Contains(name)) main.Add(type); };
            Application.LogCallback onThreaded = (message, trace, type) => { if (message.Contains(name)) threaded.Add(type); };
            GameObject owner = null;
            try
            {
                ProjectInfo.UnityVersion = release;
                owner = new GameObject(name);
                var first = owner.AddComponent<Unique>();
                first.enabled = false;
                logger.logHandler = handler;
                logger.logEnabled = enabled;
                logger.filterLogType = LogType.Log;
                Application.logMessageReceived += onMain;
                Application.logMessageReceivedThreaded += onThreaded;
                Assert.Null(owner.AddComponent<Unique>());
                Assert.Same(first, owner.GetComponent<Unique>());
                Assert.Single(owner.GetComponents<Unique>());
                bool measured = release == "6000.0.71f1";
                var expected = measured ? new[] { LogType.Log }
                    : enabled ? new[] { LogType.Error } : Array.Empty<LogType>();
                Assert.Equal(expected, main);
                Assert.Equal(expected, threaded);
                Assert.Equal(!measured && enabled ? new[] { LogType.Error } : Array.Empty<LogType>(), handler.Types);
                main.Clear(); threaded.Clear(); handler.Types.Clear();
                Debug.Log(name + " public control");
                var publicExpected = enabled ? new[] { LogType.Log } : Array.Empty<LogType>();
                Assert.Equal(publicExpected, main);
                Assert.Equal(publicExpected, threaded);
                Assert.Equal(publicExpected, handler.Types);
            }
            finally
            {
                Application.logMessageReceived -= onMain;
                Application.logMessageReceivedThreaded -= onThreaded;
                logger.logHandler = previousHandler;
                logger.logEnabled = previousEnabled;
                logger.filterLogType = previousFilter;
                if (owner != null) Object.DestroyImmediate(owner);
                ProjectInfo.UnityVersion = previousRelease;
            }
        }
    }
}
