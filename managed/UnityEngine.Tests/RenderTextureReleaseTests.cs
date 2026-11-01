using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public class RenderTextureReleaseTests
    {
        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 0)]
        public void PublicActiveReleaseWarnsAndDestructionStaysQuiet(bool destroy, int expectedWarnings)
        {
            var state = GfxStream.Current;
            var active = RenderTexture.ActiveTarget;
            var standIn = RenderTexture.ScreenStandIn;
            var warnings = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Warning) warnings.Add(message); };
            var target = new RenderTexture(4, 4, 0);
            try
            {
                RenderTexture.ScreenStandIn = null;
                Application.logMessageReceived += capture;
                RenderTexture.active = target;
                if (destroy) Object.DestroyImmediate(target);
                else target.Release();
                Assert.Null(RenderTexture.active);
                Assert.Equal(expectedWarnings, warnings.Count);
                if (expectedWarnings != 0)
                    Assert.Equal("Releasing render texture that is set to be RenderTexture.active!", warnings[0]);
            }
            finally
            {
                Application.logMessageReceived -= capture;
                Object.DestroyImmediate(target);
                RenderTexture.ScreenStandIn = standIn;
                RenderTexture.SetActiveQuietly(active);
                GfxStream.Restore(state);
            }
        }
    }
}
