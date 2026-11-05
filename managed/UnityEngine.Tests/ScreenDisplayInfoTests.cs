using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Screen.currentResolution, resolutions, dpi and mainWindowDisplayInfo answer from
    /// the host's `display-info` call (the display the window is on), asked at most once a frame;
    /// without a display they keep Unbound's constants and warn once. The host is a stand-in.
    /// Docs (6000.0): currentResolution is the desktop's resolution in windowed mode; resolutions
    /// are sorted first by width, then ascending. The dpi formula (96 per unit of scale) is
    /// Unbound's choice (unmeasured).</summary>
    public class ScreenDisplayInfoTests : IDisposable
    {
        readonly List<string> m_Requests = new List<string>();
        readonly List<string> m_Warnings = new List<string>();
        Func<string, string> m_Answer;
        int m_Frame = 1;

        public ScreenDisplayInfoTests()
        {
            Screen.ResetDisplayForTests();
            Screen.s_DisplayFrameForTests = m_Frame;
            Screen.s_DisplayCallForTests = Call;
            Application.logMessageReceived += Capture;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= Capture;
            Screen.s_DisplayCallForTests = null;
            Screen.s_DisplayFrameForTests = null;
            Screen.ResetDisplayForTests();
        }

        void Capture(string message, string stack, LogType type)
        {
            if (type == LogType.Warning && message.Contains("Screen.currentResolution")) m_Warnings.Add(message);
        }

        string Call(string method, string request)
        {
            Assert.Equal("display-info", method);
            m_Requests.Add(request);
            return m_Answer(request);
        }

        void NextFrame() => Screen.s_DisplayFrameForTests = ++m_Frame;

        static string Rate(uint numerator, uint denominator) => "{\"numerator\":" + numerator + ",\"denominator\":" + denominator + "}";

        static string Mode(int width, int height, string rate) => "{\"width\":" + width + ",\"height\":" + height + ",\"refreshRate\":" + rate + "}";

        static string Known(int version, int width = 2560, int height = 1440, string rate = null, double scale = 1.5, string name = "\"DP-1\"", params string[] modes) =>
            "{\"version\":" + version + ",\"available\":true,\"id\":7,\"name\":" + name + ",\"width\":" + width + ",\"height\":" + height
            + ",\"refreshRate\":" + (rate ?? Rate(2997, 50)) + ",\"scaleFactor\":" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"modes\":[" + string.Join(",", modes) + "]}";

        static string Unavailable(int version, string reason) =>
            "{\"version\":" + version + ",\"available\":false,\"reason\":\"" + reason + "\"}";

        /// <summary>Red: the 747e3f23 getter (the window's size at 60 Hz) gives 1080x1920 @ 60/1.</summary>
        [Fact]
        public void CurrentResolutionIsTheDesktopsWithItsRefreshRatio()
        {
            m_Answer = _ => Known(1);
            var resolution = Screen.currentResolution;
            Assert.Equal(2560, resolution.width);
            Assert.Equal(1440, resolution.height);
            Assert.Equal(2997u, resolution.refreshRateRatio.numerator);
            Assert.Equal(50u, resolution.refreshRateRatio.denominator);
            Assert.Empty(m_Warnings);
        }

        /// <summary>Red: the 747e3f23 getter returns the current resolution only (one entry).</summary>
        [Fact]
        public void ResolutionsAreTheModesSortedByWidthThenHeightThenRate()
        {
            m_Answer = _ => Known(1, modes: new[]
            {
                Mode(1920, 1080, Rate(60, 1)),
                Mode(1280, 720, Rate(60, 1)),
                Mode(2560, 1440, Rate(2997, 50)),
                Mode(1920, 1080, Rate(144, 1)),
                Mode(1920, 1080, Rate(60, 1)), // another colour depth
                Mode(1920, 1200, Rate(60, 1)),
            });
            var got = Screen.resolutions.Select(r => r.width + "x" + r.height + "@" + r.refreshRateRatio.numerator + "/" + r.refreshRateRatio.denominator);
            Assert.Equal(new[] { "1280x720@60/1", "1920x1080@60/1", "1920x1080@144/1", "1920x1200@60/1", "2560x1440@2997/50" }, got);
        }

        /// <summary>Red: a getter that hands out the cached array lets a caller change it.</summary>
        [Fact]
        public void ResolutionsAreACopy()
        {
            m_Answer = _ => Known(1, modes: new[] { Mode(1280, 720, Rate(60, 1)) });
            Screen.resolutions[0] = default;
            Assert.Equal(1280, Screen.resolutions[0].width);
        }

        /// <summary>Red: the 747e3f23 property is the constant 96.</summary>
        [Fact]
        public void DpiIs96PerUnitOfScale()
        {
            m_Answer = _ => Known(1, scale: 1.5);
            Assert.Equal(144f, Screen.dpi);
        }

        /// <summary>Red: the 747e3f23 getter names the window "Unbound" with its size at 60 Hz.</summary>
        [Fact]
        public void MainWindowDisplayInfoIsTheWindowsDisplay()
        {
            m_Answer = _ => Known(1);
            var info = Screen.mainWindowDisplayInfo;
            Assert.Equal("DP-1", info.name);
            Assert.Equal(2560, info.width);
            Assert.Equal(1440, info.height);
            Assert.Equal(2997u, info.refreshRate.numerator);
            Assert.Equal(50u, info.refreshRate.denominator);
            Assert.Equal(new RectInt(0, 0, 2560, 1440), info.workArea);
            var layout = new List<DisplayInfo>();
            Screen.GetDisplayLayout(layout);
            Assert.Equal("DP-1", Assert.Single(layout).name);
        }

        /// <summary>Red: without the per-frame cache every read asks the host (4 calls, not 1);
        /// without the version, the second frame asks for the whole answer again.</summary>
        [Fact]
        public void TheHostIsAskedOnceAFrameAndForChangesOnly()
        {
            m_Answer = request => request.Contains("\"since\":1") ? "{\"version\":1,\"unchanged\":true}" : Known(1);
            _ = Screen.currentResolution;
            _ = Screen.dpi;
            _ = Screen.resolutions;
            _ = Screen.mainWindowDisplayInfo;
            Assert.Equal(new[] { "{}" }, m_Requests);
            NextFrame();
            Assert.Equal(2560, Screen.currentResolution.width);
            Assert.Equal(new[] { "{}", "{\"since\":1}" }, m_Requests);
        }

        /// <summary>Red: an answer kept past its frame keeps 2560 after the display changed.</summary>
        [Fact]
        public void ANewVersionReplacesTheDisplay()
        {
            m_Answer = _ => Known(1, width: 2560);
            Assert.Equal(2560, Screen.currentResolution.width);
            m_Answer = _ => Known(2, width: 1920, height: 1080, rate: Rate(60, 1), scale: 1);
            NextFrame();
            Assert.Equal(1920, Screen.currentResolution.width);
            Assert.Equal(60u, Screen.currentResolution.refreshRateRatio.numerator);
            Assert.Equal(96f, Screen.dpi);
        }

        /// <summary>Red: the 747e3f23 members never warn (0 warnings); without the once-per-reason
        /// set, a host that repeats its answer warns every frame (3).</summary>
        [Fact]
        public void WithoutADisplayTheConstantsStayWithOneWarning()
        {
            m_Answer = _ => Unavailable(1, "remote");
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(Screen.width, Screen.currentResolution.width);
                Assert.Equal(Screen.height, Screen.currentResolution.height);
                Assert.Equal(60u, Screen.currentResolution.refreshRateRatio.numerator);
                Assert.Equal(1u, Screen.currentResolution.refreshRateRatio.denominator);
                Assert.Equal(96f, Screen.dpi);
                Assert.Single(Screen.resolutions);
                Assert.Equal("Unbound", Screen.mainWindowDisplayInfo.name);
                NextFrame();
            }
            Assert.Contains("remote", Assert.Single(m_Warnings));
        }

        /// <summary>Red: without remembering that the host does not answer, it is asked every
        /// frame (3 calls); the 747e3f23 members never warn.</summary>
        [Fact]
        public void AHostThatDoesNotAnswerIsAskedOnceAndWarnsOnce()
        {
            m_Answer = _ => throw new InvalidOperationException("native `display-info` failed: unknown method");
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(96f, Screen.dpi);
                NextFrame();
            }
            Assert.Single(m_Requests);
            Assert.Contains("does not answer", Assert.Single(m_Warnings));
        }

        /// <summary>Red: without the substitution the rate reads 0/0 (value 0); without the
        /// once-per-reason set it warns every frame.</summary>
        [Fact]
        public void AnUnknownRefreshRateIs60HzWithOneWarning()
        {
            m_Answer = _ => Known(1, rate: "null");
            for (int i = 0; i < 2; i++)
            {
                Assert.Equal(60u, Screen.currentResolution.refreshRateRatio.numerator);
                Assert.Equal(1u, Screen.currentResolution.refreshRateRatio.denominator);
                Assert.Equal(2560, Screen.currentResolution.width);
                NextFrame();
            }
            Assert.Contains("no refresh rate", Assert.Single(m_Warnings));
        }

        /// <summary>Red: dropping the no-host guard makes the native call throw "not connected"
        /// and warn. (Native is not connected in this test host.)</summary>
        [Fact]
        public void WithoutAHostTheConstantsStayQuietly()
        {
            Screen.s_DisplayCallForTests = null;
            Assert.False(Unbound.Runtime.Native.IsReady);
            Assert.Equal(96f, Screen.dpi);
            Assert.Equal(Screen.width, Screen.currentResolution.width);
            Assert.Empty(m_Warnings);
        }
    }
}
