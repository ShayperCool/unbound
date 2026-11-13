using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public class RenderTextureReleaseTests
    {
        sealed class Capture : ILogHandler
        {
            public readonly List<(LogType type, string message)> Entries = new List<(LogType, string)>();
            public void LogFormat(LogType type, Object context, string format, params object[] args)
                => Entries.Add((type, string.Format(format, args)));
            public void LogException(Exception exception, Object context)
                => Entries.Add((LogType.Exception, exception.Message));
        }

        [Theory]
        [InlineData(false, true, 1)]
        [InlineData(true, true, 1)]
        [InlineData(false, false, 0)]
        [InlineData(true, false, 0)]
        public void ActiveReleaseAndDestructionWarnWhileInactiveTargetsStayQuiet(bool destroy, bool activeTarget, int expectedWarnings)
        {
            var release = ProjectInfo.UnityVersion;
            var state = GfxStream.Current;
            var active = RenderTexture.ActiveTarget;
            var standIn = RenderTexture.ScreenStandIn;
            var handler = Debug.unityLogger.logHandler;
            var capture = new Capture();
            var target = new RenderTexture(4, 4, 0);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                RenderTexture.ScreenStandIn = null;
                Debug.unityLogger.logHandler = capture;
                target.Create();
                RenderTexture.active = activeTarget ? target : null;
                if (destroy) Object.DestroyImmediate(target);
                else target.Release();
                Assert.Null(RenderTexture.active);
                Assert.Equal(expectedWarnings, capture.Entries.Count);
                if (expectedWarnings != 0)
                    Assert.Equal((LogType.Warning, "Releasing render texture that is set to be RenderTexture.active!"), capture.Entries[0]);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
                Object.DestroyImmediate(target);
                RenderTexture.ScreenStandIn = standIn;
                RenderTexture.SetActiveQuietly(active);
                GfxStream.Restore(state);
                ProjectInfo.UnityVersion = release;
            }
        }
    }
}
