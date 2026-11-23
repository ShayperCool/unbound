using UnityEditorInternal;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>InternalEditorUtility.GetFullUnityVersion: the release and its build revision as the
    /// Editor states them, which the performance testing package splits with a regular expression.</summary>
    [Collection("Project release")]
    public sealed class FullUnityVersionTests
    {
        [Fact]
        public void TheReleaseAndItsRevisionAsTheEditorStatesThem()
        {
            var (version, revision) = (ProjectInfo.UnityVersion, ProjectInfo.UnityRevision);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                ProjectInfo.UnityRevision = "907bc2d768b5";
                string full = InternalEditorUtility.GetFullUnityVersion();
                Assert.Equal("6000.0.71f1 (907bc2d768b5)", full);
                // The package's split (com.unity.test-framework.performance TestRunBuilder).
                var matches = System.Text.RegularExpressions.Regex.Matches(full, @"(.+\.+.+\.\w+)|((?<=\().+(?=\)))");
                Assert.Equal("6000.0.71f1", matches[0].Value);
                Assert.Equal("907bc2d768b5", matches[1].Value);
            }
            finally
            {
                (ProjectInfo.UnityVersion, ProjectInfo.UnityRevision) = (version, revision);
            }
        }

        sealed class Capture : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Errors = new System.Collections.Generic.List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error) Errors.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            }
            public void LogException(System.Exception exception, Object context) { }
        }

        [Fact]
        public void WithoutAKnownRevisionTheReleaseAloneIsAnsweredAndSaidOnce()
        {
            var (version, revision, warned) = (ProjectInfo.UnityVersion, ProjectInfo.UnityRevision, InternalEditorUtility.s_WarnedRevision);
            var logger = Debug.unityLogger;
            var (handler, enabled, filter) = (logger.logHandler, logger.logEnabled, logger.filterLogType);
            var capture = new Capture();
            try
            {
                logger.logHandler = capture;
                logger.logEnabled = true;
                logger.filterLogType = LogType.Log;
                ProjectInfo.UnityVersion = "6000.0.50f1";
                ProjectInfo.UnityRevision = "";
                InternalEditorUtility.s_WarnedRevision = false;
                Assert.Equal("6000.0.50f1", InternalEditorUtility.GetFullUnityVersion());
                Assert.Equal("6000.0.50f1", InternalEditorUtility.GetFullUnityVersion());
                var error = Assert.Single(capture.Errors);
                Assert.Contains("build revision of Unity 6000.0.50f1 is not known", error);
            }
            finally
            {
                (logger.logHandler, logger.logEnabled, logger.filterLogType) = (handler, enabled, filter);
                (ProjectInfo.UnityVersion, ProjectInfo.UnityRevision) = (version, revision);
                InternalEditorUtility.s_WarnedRevision = warned;
            }
        }
    }
}
