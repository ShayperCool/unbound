using System;
using Unbound.Runtime;

namespace UnityEngine.Tests
{
    /// <summary>Runs a navigation test body as if the project used <c>release</c>, so a Compat
    /// mechanism and its other-release control share one helper.</summary>
    static class NavigationTestRelease
    {
        public static void At(string release, Action body)
        {
            string previous = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = release;
            try { body(); }
            finally { ProjectInfo.UnityVersion = previous; }
        }
    }
}
