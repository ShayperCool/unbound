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
    }
}
