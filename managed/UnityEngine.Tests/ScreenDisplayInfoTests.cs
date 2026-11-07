using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Screen.currentResolution, resolutions, dpi and mainWindowDisplayInfo answer from
    /// the host's `display-info` call (the display the window is on), refreshed by cached version;
    /// without a display they keep Unbound's constants and warn once. The host is a stand-in.
    /// Docs (6000.0): currentResolution is the desktop's resolution in windowed mode; resolutions
    /// differ from the Editor's standard-size choices. Density is supplied by the host.</summary>
    public class ScreenDisplayInfoTests : IDisposable
    {
        readonly List<string> m_Requests = new List<string>();
        readonly List<string> m_Warnings = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;
        Func<string, string> m_Answer;
        int m_Frame = 1;
        readonly string m_Release = ProjectInfo.UnityVersion;

        public ScreenDisplayInfoTests()
        {
            ProjectInfo.UnityVersion = "2022.3.62f1";
            Screen.ResetDisplayForTests();
            Screen.s_DisplayFrameForTests = m_Frame;
            Screen.s_DisplayCallForTests = Call;
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            // Unbound diagnostics ("Unbound: ...") bypass the game's Application log callbacks:
            // observe them at the logger's handler and forward every call to the original one.
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Warnings);
            m_Logger.logEnabled = true;
            m_Logger.filterLogType = LogType.Log;
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Warnings;

            internal CaptureHandler(ILogHandler inner, List<string> warnings)
            {
                m_Inner = inner;
                m_Warnings = warnings;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                string message = args == null || args.Length == 0 ? format
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                if (type == LogType.Warning && message.Contains("Screen.currentResolution")) m_Warnings.Add(message);
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        public void Dispose()
        {
            m_Logger.logHandler = m_PreviousHandler;
            m_Logger.logEnabled = m_PreviousLogEnabled;
            m_Logger.filterLogType = m_PreviousFilter;
            ProjectInfo.UnityVersion = m_Release;
            Screen.s_DisplayCallForTests = null;
            Screen.s_DisplayFrameForTests = null;
            Screen.ResetDisplayForTests();
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
            + ",\"dpi\":" + (96 * scale).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"refreshRate\":" + (rate ?? Rate(2997, 50)) + ",\"scaleFactor\":" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"modes\":[" + string.Join(",", modes) + "]}";

        static string Unavailable(int version, string reason) =>
            "{\"version\":" + version + ",\"available\":false,\"reason\":\"" + reason + "\"}";

        [Fact]
        public void LinuxEditorChoicesAndHostDisplayInfoHaveDifferentRefreshRatios()
        {
            if (!OperatingSystem.IsLinux()) return;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            m_Answer = _ => Known(1, width: 1280, height: 720, scale: 0);
            Assert.Equal(60u, Screen.currentResolution.refreshRateRatio.numerator);
            Assert.Equal(2997u, Screen.mainWindowDisplayInfo.refreshRate.numerator);
            Assert.Equal(50u, Screen.mainWindowDisplayInfo.refreshRate.denominator);
            Assert.Equal(0, Screen.dpi);
            Assert.Equal(15, Screen.resolutions.Length);
            Assert.Equal(800, Screen.resolutions[7].width);
            Assert.Equal(600, Screen.resolutions[7].height);
        }

        [Fact]
        public void EditorResolutionAnswerDoesNotLimitHighRefreshPacing()
        {
            if (!OperatingSystem.IsLinux()) return;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            m_Answer = _ => Known(1, width: 1920, height: 1080, rate: Rate(144, 1));
            Assert.Equal(60u, Screen.currentResolution.refreshRateRatio.numerator);
            Assert.Equal(144d, Screen.HostRefreshRate);
            Assert.Equal(0.95 / 144, Unbound.Runtime.PlayerLoopDriver.FramePeriod(-1, 1), 12);
            Assert.Equal(1.90 / 144, Unbound.Runtime.PlayerLoopDriver.FramePeriod(-1, 2), 12);
            Assert.Equal(1.0 / 120, Unbound.Runtime.PlayerLoopDriver.FramePeriod(120, 0), 12);
            Assert.Equal(0, Unbound.Runtime.PlayerLoopDriver.FramePeriod(-1, 0));
        }

        [Theory]
        [InlineData("xvfb")]
        [InlineData("host")]
        [InlineData("large")]
        [InlineData("control")]
        public void EditorChoicesMatchDisplayControls(string context)
        {
            if (!OperatingSystem.IsLinux()) return;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            string source = ReferenceDirectory();
            int width = 0, height = 0;
            var expected = new List<string>();
            foreach (string line in System.IO.File.ReadLines(System.IO.Path.Combine(source, context, "values.jsonl")))
            {
                using var document = System.Text.Json.JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.GetProperty("k").GetString() == "currentResolution")
                { width = root.GetProperty("v")[0].GetInt32(); height = root.GetProperty("v")[1].GetInt32(); }
                if (root.GetProperty("k").GetString() == "resolutions")
                    foreach (var r in root.GetProperty("v").EnumerateArray())
                        expected.Add($"{r[0]}x{r[1]}@{r[2]}/{r[3]}");
            }
            m_Answer = _ => Known(1, width: width, height: height);
            Assert.Equal(expected, Screen.resolutions.Select(r => $"{r.width}x{r.height}@{r.refreshRateRatio.numerator}/{r.refreshRateRatio.denominator}"));
        }

        static string ReferenceDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source), "../../tests/projects/inputplatform/Reference/unity-6000.0.71f1");

        [Fact]
        public void CachedDisplayIsReadOnlyWhenTheHostVersionChanges()
        {
            Screen.s_DisplayFrameForTests = null;
            Screen.UpdateDisplayVersion(1);
            m_Answer = _ => Known(1);
            _ = Screen.currentResolution;
            ulong handle = Screen.mainWindowDisplayInfo.handle;
            Screen.UpdateDisplayVersion(1);
            _ = Screen.dpi;
            Assert.Single(m_Requests);
            Screen.UpdateDisplayVersion(2);
            m_Answer = _ => Known(2, width: 1920, rate: Rate(144, 1));
            Assert.Equal(1920, Screen.currentResolution.width);
            Assert.Equal(144u, Screen.currentResolution.refreshRateRatio.numerator);
            Assert.Equal(handle, Screen.mainWindowDisplayInfo.handle);
            Assert.Equal(2, m_Requests.Count);
        }

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
