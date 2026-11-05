using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// GUIUtility.systemCopyBuffer and EditorGUIUtility.systemCopyBuffer reach the system clipboard
    /// through the host's string-keyed <c>clipboard-get</c>/<c>clipboard-set</c> calls. Documented
    /// (6000.0 Scripting API): GUIUtility's property "Get access to the system-wide clipboard";
    /// EditorGUIUtility's "captures any text that is selected on the machine ... Reading and writing
    /// EditorGUIUtility.systemCopyBuffer is possible". Unbound's choices (unmeasured) checked here:
    /// no text on the clipboard reads as ""; a failing clipboard falls back to the in-process copy
    /// with one warning until it answers again; both properties share that copy.
    /// A stand-in host answers the calls, so no test touches the machine's clipboard. Warnings are
    /// captured at the logger's handler (Unbound's own notices skip Application.logMessageReceived).
    /// The hook, the shared copy and the log handler are restored in <see cref="Dispose"/>.
    /// </summary>
    public sealed class SystemClipboardTests : IDisposable
    {
        readonly List<(string Method, string Request)> m_Calls = new List<(string, string)>();
        readonly List<string> m_Reports = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;

        /// <summary>The stand-in clipboard: its text, or null when it holds no text.</summary>
        string m_Clipboard;
        /// <summary>When set, reads and/or writes fail as the native call does.</summary>
        string m_FailReads, m_FailWrites;
        /// <summary>When set, the host answers this instead of a well-formed get answer.</summary>
        string m_RawGetAnswer;

        public SystemClipboardTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Reports);
            m_Logger.logEnabled = true;
            m_Logger.filterLogType = LogType.Log;
            GUIUtility.ResetClipboardForTests();
            GUIUtility.s_ClipboardCallForTests = Host;
        }

        public void Dispose()
        {
            GUIUtility.s_ClipboardCallForTests = null;
            GUIUtility.ResetClipboardForTests();
            m_Logger.logHandler = m_PreviousHandler;
            m_Logger.logEnabled = m_PreviousLogEnabled;
            m_Logger.filterLogType = m_PreviousFilter;
        }

        string Host(string method, string request)
        {
            m_Calls.Add((method, request));
            switch (method)
            {
                case "clipboard-get":
                    if (m_FailReads != null) throw new InvalidOperationException("native `clipboard-get` failed: " + m_FailReads);
                    if (m_RawGetAnswer != null) return m_RawGetAnswer;
                    return JsonSerializer.Serialize(new Dictionary<string, string> { ["text"] = m_Clipboard });
                case "clipboard-set":
                    if (m_FailWrites != null) throw new InvalidOperationException("native `clipboard-set` failed: " + m_FailWrites);
                    m_Clipboard = TextOf(request);
                    return "{}";
                default:
                    throw new InvalidOperationException($"unknown native method `{method}`");
            }
        }

        static string TextOf(string request)
        {
            using var json = JsonDocument.Parse(request);
            return json.RootElement.GetProperty("text").GetString();
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

        [Fact]
        public void SettingWritesTheSystemClipboardAndGettingReadsIt()
        {
            GUIUtility.systemCopyBuffer = "copied in the game";
            Assert.Single(m_Calls);
            Assert.Equal("clipboard-set", m_Calls[0].Method);
            Assert.Equal("copied in the game", TextOf(m_Calls[0].Request));

            // Another application copies: the game reads the machine's clipboard, not its own copy.
            m_Clipboard = "copied elsewhere";
            Assert.Equal("copied elsewhere", GUIUtility.systemCopyBuffer);
            Assert.Equal("clipboard-get", m_Calls[1].Method);
            Assert.Empty(m_Reports);
        }

        [Fact]
        public void TheEditorAndRuntimePropertiesAreOneClipboard()
        {
            EditorGUIUtility.systemCopyBuffer = "from the editor API";
            Assert.Equal("from the editor API", m_Clipboard);
            Assert.Equal("from the editor API", GUIUtility.systemCopyBuffer);
            GUIUtility.systemCopyBuffer = "from the runtime API";
            Assert.Equal("from the runtime API", EditorGUIUtility.systemCopyBuffer);

            // The in-process copy is shared as well.
            m_FailReads = m_FailWrites = "no display";
            EditorGUIUtility.systemCopyBuffer = "kept in process";
            Assert.Equal("kept in process", GUIUtility.systemCopyBuffer);
            GUIUtility.systemCopyBuffer = "kept again";
            Assert.Equal("kept again", EditorGUIUtility.systemCopyBuffer);
            Assert.Single(m_Reports);
        }

        [Fact]
        public void TextAndLineEndsPassUnchanged()
        {
            const string written = "a\r\nb\nc\rd\té\U0001F600 ";
            GUIUtility.systemCopyBuffer = written;
            Assert.Equal(written, TextOf(m_Calls[0].Request));
            const string read = "x\r\ny\n\n";
            m_Clipboard = read;
            Assert.Equal(read, GUIUtility.systemCopyBuffer);
            Assert.Equal(read, EditorGUIUtility.systemCopyBuffer);
        }

        [Fact]
        public void NoTextOnTheClipboardReadsEmptyWithoutAWarning()
        {
            GUIUtility.systemCopyBuffer = "old text";
            // Another application copied something that is not text.
            m_Clipboard = null;
            Assert.Equal("", GUIUtility.systemCopyBuffer);
            Assert.Empty(m_Reports);

            // That read is the in-process copy now: a later failure does not bring back the old text.
            m_FailReads = "connection lost";
            Assert.Equal("", GUIUtility.systemCopyBuffer);
            Assert.Single(m_Reports);
        }

        [Fact]
        public void AFailingClipboardFallsBackWithOneWarningUntilItAnswersAgain()
        {
            m_Clipboard = "first";
            Assert.Equal("first", GUIUtility.systemCopyBuffer);

            m_FailReads = "connection lost";
            Assert.Equal("first", GUIUtility.systemCopyBuffer);
            Assert.Equal("first", GUIUtility.systemCopyBuffer);
            var report = Assert.Single(m_Reports);
            Assert.StartsWith("Warning: Unbound: GUIUtility.systemCopyBuffer could not read the system clipboard", report);
            Assert.Contains("connection lost", report);

            m_FailReads = null;
            m_Clipboard = "second";
            Assert.Equal("second", GUIUtility.systemCopyBuffer);
            Assert.Single(m_Reports);

            // A new failure after an answer warns again, once.
            m_FailReads = "connection lost";
            Assert.Equal("second", GUIUtility.systemCopyBuffer);
            Assert.Equal("second", GUIUtility.systemCopyBuffer);
            Assert.Equal(2, m_Reports.Count);

            // The warnings name the cause, never the clipboard's text.
            foreach (var r in m_Reports)
            {
                Assert.DoesNotContain("first", r);
                Assert.DoesNotContain("second", r);
            }
        }

        [Fact]
        public void AFailedWriteKeepsTheTextInProcess()
        {
            m_FailReads = m_FailWrites = "no display";
            GUIUtility.systemCopyBuffer = "local";
            Assert.Null(m_Clipboard);
            Assert.Equal("local", GUIUtility.systemCopyBuffer);
            var report = Assert.Single(m_Reports);
            Assert.StartsWith("Warning: Unbound: GUIUtility.systemCopyBuffer could not write the system clipboard", report);
            Assert.DoesNotContain("local", report);

            // A write that succeeds again ends the failure: the next one warns again.
            m_FailWrites = null;
            GUIUtility.systemCopyBuffer = "shared";
            Assert.Equal("shared", m_Clipboard);
            m_FailWrites = "no display";
            GUIUtility.systemCopyBuffer = "local again";
            Assert.Equal(2, m_Reports.Count);
        }

        [Fact]
        public void AnUnreadableAnswerIsAFailureNotText()
        {
            GUIUtility.systemCopyBuffer = "set";
            m_RawGetAnswer = "{\"text\":5}";
            Assert.Equal("set", GUIUtility.systemCopyBuffer);
            Assert.Single(m_Reports);
            m_RawGetAnswer = "not json";
            Assert.Equal("set", GUIUtility.systemCopyBuffer);
            Assert.Single(m_Reports);
        }

        [Fact]
        public void AHostWithoutTheCallsFallsBack()
        {
            GUIUtility.s_ClipboardCallForTests = (method, request) =>
                throw new InvalidOperationException($"native `{method}` failed: unknown native method `{method}`");
            GUIUtility.systemCopyBuffer = "in process";
            Assert.Equal("in process", GUIUtility.systemCopyBuffer);
            Assert.Equal("in process", EditorGUIUtility.systemCopyBuffer);
            var report = Assert.Single(m_Reports);
            Assert.Contains("unknown native method", report);
        }

        [Fact]
        public void NullIsStoredAsEmptyText()
        {
            GUIUtility.systemCopyBuffer = null;
            Assert.Equal("", TextOf(m_Calls[0].Request));
            EditorGUIUtility.systemCopyBuffer = null;
            Assert.Equal("", TextOf(m_Calls[1].Request));
            Assert.Equal("", EditorGUIUtility.systemCopyBuffer);
        }
    }
}
