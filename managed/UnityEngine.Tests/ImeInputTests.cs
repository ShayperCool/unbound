using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The legacy Input Manager's IME members against the host's IME state, through the
    /// <see cref="Input.ImeHost"/> seam (no window). Unity 6000.0 Scripting API: compositionString is
    /// "the current IME composition string being typed by the user"; imeIsSelected tells whether
    /// "the users keyboard is currently configured for IME input"; imeCompositionMode Auto enables
    /// IME input "only when a text field is selected (default)", On enables it, Off disables it;
    /// compositionCursorPos is "the current text input position used by IMEs to open windows".
    /// The tests check that the answers follow the host's state, polled once per frame; that the
    /// mode and position become the window's request; and that without a window the answers
    /// stay "" and false with one warning. The frame counter, the log handler and the IME state
    /// are restored in <see cref="Dispose"/>.
    /// </summary>
    public sealed class ImeInputTests : IDisposable
    {
        readonly int m_Frame = Time.s_FrameCount;
        readonly List<string> m_Reports = new List<string>();
        readonly List<(string Method, string Request)> m_Calls = new List<(string, string)>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;
        string m_State = Available("", false);

        public ImeInputTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Reports);
            m_Logger.logEnabled = true;
            m_Logger.filterLogType = LogType.Log;
            Input.ResetIme();
            Input.ImeHost = Host;
        }

        public void Dispose()
        {
            Input.ResetIme();
            Time.s_FrameCount = m_Frame;
            m_Logger.logHandler = m_PreviousHandler;
            m_Logger.logEnabled = m_PreviousLogEnabled;
            m_Logger.filterLogType = m_PreviousFilter;
        }

        static string Available(string composition, bool selected) =>
            "{\"available\":true,\"composition\":" + System.Text.Json.JsonSerializer.Serialize(composition)
            + ",\"cursor\":null,\"selected\":" + (selected ? "true" : "false") + "}";

        const string Unavailable = "{\"available\":false,\"composition\":\"\",\"cursor\":null,\"selected\":false}";

        string Host(string method, string request)
        {
            m_Calls.Add((method, request));
            return method == "ime-state" ? m_State : "{}";
        }

        int Calls(string method) => m_Calls.FindAll(c => c.Method == method).Count;

        List<string> Requests() => m_Calls.FindAll(c => c.Method == "ime-request").ConvertAll(c => c.Request);

        List<string> ImeWarnings() => m_Reports.FindAll(r => r.Contains("IME"));

        static void NextFrame() => Time.s_FrameCount++;

        [Fact]
        public void CompositionStringAndImeIsSelectedFollowTheHostState()
        {
            NextFrame();
            m_State = Available("かな", true);
            Assert.Equal("かな", Input.compositionString);
            Assert.True(Input.imeIsSelected);

            NextFrame();
            m_State = Available("", false);
            Assert.Equal("", Input.compositionString);
            Assert.False(Input.imeIsSelected);
            Assert.Empty(ImeWarnings());
        }

        [Fact]
        public void TheHostIsPolledOncePerFrame()
        {
            NextFrame();
            m_State = Available("ㅎ", true);
            _ = Input.compositionString;
            _ = Input.imeIsSelected;
            _ = Input.CompositionString;
            _ = Input.compositionString;
            Assert.Equal(1, Calls("ime-state"));

            // A change within the frame shows on the next frame.
            m_State = Available("하", true);
            Assert.Equal("ㅎ", Input.compositionString);
            NextFrame();
            Assert.Equal("하", Input.compositionString);
            Assert.Equal(2, Calls("ime-state"));
        }

        [Fact]
        public void TheEnginesOwnCompositionFollowsTheHostWithoutWarning()
        {
            NextFrame();
            m_State = Available("zhong", true);
            Assert.Equal("zhong", Input.CompositionString);
            m_State = Unavailable;
            NextFrame();
            Assert.Equal("", Input.CompositionString);
            Assert.Empty(ImeWarnings());
        }

        [Fact]
        public void ImeCompositionModeBecomesTheWindowsRequest()
        {
            NextFrame();
            Assert.Equal(IMECompositionMode.Auto, Input.imeCompositionMode);
            Input.imeCompositionMode = IMECompositionMode.On;
            Input.imeCompositionMode = IMECompositionMode.On;
            Input.imeCompositionMode = IMECompositionMode.Off;
            Input.imeCompositionMode = IMECompositionMode.On;
            // Auto: no text field selection is seen, so the input method goes off (Unbound's
            // choice, unmeasured).
            Input.imeCompositionMode = IMECompositionMode.Auto;
            Assert.Equal(IMECompositionMode.Auto, Input.imeCompositionMode);
            Assert.Equal(new List<string>
            {
                "{\"enabled\":true,\"position\":[0,0]}",
                "{\"enabled\":false,\"position\":[0,0]}",
                "{\"enabled\":true,\"position\":[0,0]}",
                "{\"enabled\":false,\"position\":[0,0]}",
            }, Requests());
            Assert.Empty(ImeWarnings());
        }

        [Fact]
        public void CompositionCursorPosBecomesTheImePosition()
        {
            NextFrame();
            Input.imeCompositionMode = IMECompositionMode.On;
            Input.compositionCursorPos = new Vector2(12.5f, 40f);
            Input.compositionCursorPos = new Vector2(12.5f, 40f);
            Assert.Equal(new Vector2(12.5f, 40f), Input.compositionCursorPos);
            // No flip: the value goes as given (the coordinate space is undocumented).
            Input.compositionCursorPos = new Vector2(-3f, 2048f);
            Assert.Equal(new List<string>
            {
                "{\"enabled\":true,\"position\":[0,0]}",
                "{\"enabled\":true,\"position\":[12.5,40]}",
                "{\"enabled\":true,\"position\":[-3,2048]}",
            }, Requests());
        }

        [Fact]
        public void WithoutAWindowTheAnswersStayEmptyWithOneWarning()
        {
            m_State = Unavailable;
            for (int frame = 0; frame < 3; frame++)
            {
                NextFrame();
                Assert.Equal("", Input.compositionString);
                Assert.False(Input.imeIsSelected);
                Input.imeCompositionMode = frame % 2 == 0 ? IMECompositionMode.On : IMECompositionMode.Off;
                Input.compositionCursorPos = new Vector2(frame, frame);
            }
            var warnings = ImeWarnings();
            Assert.Single(warnings);
            Assert.Contains("IME input is not available", warnings[0]);
        }

        [Fact]
        public void AFailingHostFallsBackWithOneWarningAndIsNotAskedAgain()
        {
            Input.ImeHost = (method, request) =>
            {
                m_Calls.Add((method, request));
                throw new InvalidOperationException("the native side is not connected");
            };
            NextFrame();
            Assert.Equal("", Input.compositionString);
            Assert.False(Input.imeIsSelected);
            NextFrame();
            Assert.Equal("", Input.compositionString);
            Input.imeCompositionMode = IMECompositionMode.On;
            Assert.Single(m_Calls);
            var warnings = ImeWarnings();
            Assert.Single(warnings);
            Assert.Contains("not connected", warnings[0]);
        }

        /// <summary>Records every report other than a plain log and forwards each call unchanged.</summary>
        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Reports;

            internal CaptureHandler(ILogHandler inner, List<string> reports)
            {
                m_Inner = inner;
                m_Reports = reports;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type != LogType.Log)
                {
                    string message = args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                    m_Reports.Add($"{type}: {message}");
                }
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context)
            {
                if (exception != null) m_Reports.Add($"Exception: {exception}");
                m_Inner.LogException(exception, context);
            }
        }
    }
}
