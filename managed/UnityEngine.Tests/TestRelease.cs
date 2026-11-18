using System;
using System.Runtime.CompilerServices;
using Unbound.Runtime;

namespace UnityEngine.Tests
{
    /// <summary>The Unity release the tests run under, as the engine runs a project under its
    /// release (ProjectInfo.UnityVersion, which the Compat answers follow): the release Unbound's
    /// references are measured on, so the release-specific paths are what the tests exercise. A
    /// test of behaviour that does not depend on the release, or of another release's, sets
    /// ProjectInfo.UnityVersion itself (and puts it back). Releases are grouped by family
    /// (MAJOR.MINOR, Compat.Family): every 6000.0 release gets the answers measured on 6000.0.71f1,
    /// so a release without measured answers is one of another family. UNBOUND_TEST_UNITY_RELEASE
    /// selects another release for a whole run (for example 6000.3.0f1, a release without measured
    /// answers, or 6000.0.0f1, which must answer as the measured release). The compatibility profile's features (ProjectInfo.Features), which the host fills
    /// from the profile (every one for 6000.0.71f1), stay empty; a test that needs one adds it.</summary>
    static class TestRelease
    {
        internal const string Measured = "6000.0.71f1";

        /// <summary>A release without measured answers (outside every measured family), for tests
        /// of the path other families keep.</summary>
        internal const string Unmeasured = "6000.3.0f1";

        /// <summary>Runs under <paramref name="release"/> until disposed, then under the previous one.</summary>
        internal static IDisposable Pin(string release) => new Scope(release);

        sealed class Scope : IDisposable
        {
            readonly string m_Previous = ProjectInfo.UnityVersion;
            internal Scope(string release) => ProjectInfo.UnityVersion = release;
            public void Dispose() => ProjectInfo.UnityVersion = m_Previous;
        }

        [ModuleInitializer]
        internal static void Select()
        {
            var release = Environment.GetEnvironmentVariable("UNBOUND_TEST_UNITY_RELEASE");
            ProjectInfo.UnityVersion = string.IsNullOrEmpty(release) ? Measured : release;
        }
    }
}
