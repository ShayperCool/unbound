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

        /// <summary>The other version members, as the 6000.0.71f1 Editor answers them
        /// (tests/projects/editorinternals version.*); another 6000.0 release gets the family's
        /// build facts with its own version.</summary>
        [Fact]
        public void TheVersionMembersAnswerAsTheEditorOfTheRelease()
        {
            var (version, revision) = (ProjectInfo.UnityVersion, ProjectInfo.UnityRevision);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                ProjectInfo.UnityRevision = "907bc2d768b5";
                Assert.Equal("6000.0.71", InternalEditorUtility.GetUnityVersionDigits());
                Assert.Equal("6000.0.71f1", InternalEditorUtility.GetUnityDisplayVersion());
                Assert.Equal("6000.0.71f1.8720.19091", InternalEditorUtility.GetUnityDisplayVersionVerbose());
                Assert.Equal("6000.0/respin/6000.0.71f1-8308b6469260", InternalEditorUtility.GetUnityBuildBranch());
                Assert.Equal("907bc2d768b5", InternalEditorUtility.GetUnityBuildHash());
                Assert.Equal(9468866, InternalEditorUtility.GetUnityRevision());
                Assert.Equal(1773651634, InternalEditorUtility.GetUnityVersionDate());
                Assert.Equal("Unity 6.0", InternalEditorUtility.GetUnityProductName());
                Assert.Equal("(c) 2005-2026 Unity Technologies. All rights reserved.", InternalEditorUtility.GetUnityCopyright());
                ProjectInfo.UnityVersion = "6000.0.62f1";
                ProjectInfo.UnityRevision = "abcdef012345";
                Assert.Equal("6000.0.62", InternalEditorUtility.GetUnityVersionDigits());
                Assert.Equal("6000.0.62f1.8720.19091", InternalEditorUtility.GetUnityDisplayVersionVerbose());
                Assert.Equal("abcdef012345", InternalEditorUtility.GetUnityBuildHash());
                Assert.Equal(9468866, InternalEditorUtility.GetUnityRevision());
            }
            finally
            {
                (ProjectInfo.UnityVersion, ProjectInfo.UnityRevision) = (version, revision);
            }
        }

        [Fact]
        public void AFamilyWithoutBuildFactsAnswersEmptyAndSaysSoOncePerMember()
        {
            var (version, revision) = (ProjectInfo.UnityVersion, ProjectInfo.UnityRevision);
            var logger = Debug.unityLogger;
            var (handler, enabled, filter) = (logger.logHandler, logger.logEnabled, logger.filterLogType);
            var capture = new Capture();
            try
            {
                logger.logHandler = capture;
                logger.logEnabled = true;
                logger.filterLogType = LogType.Log;
                ProjectInfo.UnityVersion = "6000.3.0f1";
                ProjectInfo.UnityRevision = "0123456789ab";
                InternalEditorUtility.s_UnmeasuredBuildFacts.Clear();
                Assert.Equal("6000.3.0", InternalEditorUtility.GetUnityVersionDigits());
                Assert.Equal("6000.3.0f1", InternalEditorUtility.GetUnityDisplayVersionVerbose());
                Assert.Equal("", InternalEditorUtility.GetUnityBuildBranch());
                Assert.Equal("", InternalEditorUtility.GetUnityBuildBranch());
                Assert.Equal(2, capture.Errors.Count);
                Assert.Contains("GetUnityDisplayVersionVerbose is not measured for Unity 6000.3.0f1", capture.Errors[0]);
                Assert.Contains("GetUnityBuildBranch is not measured", capture.Errors[1]);
                Assert.Equal(0, InternalEditorUtility.GetUnityVersionDate());
                Assert.Equal(0, InternalEditorUtility.GetUnityVersionDate());
                Assert.Equal(3, capture.Errors.Count);
            }
            finally
            {
                (logger.logHandler, logger.logEnabled, logger.filterLogType) = (handler, enabled, filter);
                (ProjectInfo.UnityVersion, ProjectInfo.UnityRevision) = (version, revision);
                InternalEditorUtility.s_UnmeasuredBuildFacts.Clear();
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
