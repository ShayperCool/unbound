using System;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    /// <summary>A UTF-8 string by pointer and length (console entries from unmanaged code).</summary>
    internal readonly partial struct UTF8StringView
    {
        public readonly IntPtr utf8Ptr;
        public readonly int utf8Length;

        public unsafe UTF8StringView(ReadOnlySpan<byte> prefixUtf8Span)
        {
            fixed (byte* value = &prefixUtf8Span[0]) utf8Ptr = new IntPtr(value);
            utf8Length = prefixUtf8Span.Length;
        }

        public UTF8StringView(IntPtr ptr, int lengthUtf8)
        {
            utf8Ptr = ptr;
            utf8Length = lengthUtf8;
        }

        public unsafe UTF8StringView(byte* ptr, int lengthUtf8)
        {
            utf8Ptr = new IntPtr(ptr);
            utf8Length = lengthUtf8;
        }

        public override unsafe string ToString() => Encoding.UTF8.GetString((byte*)utf8Ptr.ToPointer(), utf8Length);
    }
}

namespace UnityEditor
{
    internal partial struct LogEntryStruct
    {
        public UnityEngine.UTF8StringView messagePrefix;
        public UnityEngine.UTF8StringView message;
        public UnityEngine.UTF8StringView callstack;
        public UnityEngine.UTF8StringView timestamp;
        public UnityEngine.UTF8StringView file;
        public int line;
        public int column;
        public LogMessageFlags mode;
        public int instanceID;
        public int identifier;
    }

    internal enum LogMessageFlags
    {
        kNoLogMessageFlags = 0,
        kError = 1,
        kAssert = 2,
        kLog = 4,
        kFatal = 16,
        kAssetImportError = 64,
        kAssetImportWarning = 128,
        kScriptingError = 256,
        kScriptingWarning = 512,
        kScriptingLog = 1024,
        kScriptCompileError = 2048,
        kScriptCompileWarning = 4096,
        kStickyLog = 8192,
        kMayIgnoreLineNumber = 16384,
        kReportBug = 32768,
        kDisplayPreviousErrorInStatusBar = 65536,
        kScriptingException = 131072,
        kDontExtractStacktrace = 262144,
        kScriptingAssertion = 2097152,
        kStacktraceIsPostprocessed = 4194304,
        kIsCalledFromManaged = 8388608,
        FromEditor = 8667136,
        DebugLog = 8668160,
        DebugWarning = 8667648,
        DebugError = 8667392,
        DebugException = 8798208,
        DebugAssert = 10764288,
    }

    internal static partial class LogMessageFlagsExtensions
    {
        public static bool IsInfo(this LogMessageFlags flags) =>
            (flags & (LogMessageFlags.kLog | LogMessageFlags.kScriptingLog)) != 0;

        public static bool IsWarning(this LogMessageFlags flags) =>
            (flags & (LogMessageFlags.kAssetImportWarning | LogMessageFlags.kScriptingWarning | LogMessageFlags.kScriptCompileWarning)) != 0;

        public static bool IsError(this LogMessageFlags flags) =>
            (flags & (LogMessageFlags.kError | LogMessageFlags.kAssert | LogMessageFlags.kFatal | LogMessageFlags.kAssetImportError
                | LogMessageFlags.kScriptingError | LogMessageFlags.kScriptCompileError | LogMessageFlags.kScriptingException
                | LogMessageFlags.kScriptingAssertion)) != 0;
    }

    internal sealed partial class LogEntries
    {
        /// <summary>
        /// Entries straight into the console (com.unity.logging's console sink): as in the Editor they
        /// skip Application's log callbacks and the log file; Unbound's console is its output.
        /// </summary>
        public static unsafe void AddMessagesImpl(void* messagesBuffer, int messagesBufferLength)
        {
            var entries = (LogEntryStruct*)messagesBuffer;
            for (int i = 0; i < messagesBufferLength; i++)
            {
                ref var entry = ref entries[i];
                var mode = entry.mode;
                var type = mode.IsError() ? UnityEngine.LogType.Error : mode.IsWarning() ? UnityEngine.LogType.Warning : UnityEngine.LogType.Log;
                var text = Text(entry.messagePrefix) + Text(entry.message);
                var callstack = Text(entry.callstack);
                if (callstack.Length > 0) text += "\n" + callstack;
                Unbound.Runtime.Native.Log((int)type, text);
            }
        }

        static string Text(UnityEngine.UTF8StringView view) => view.utf8Ptr == IntPtr.Zero || view.utf8Length <= 0 ? "" : view.ToString();
    }

    internal partial class ConsoleWindow : EditorWindow
    {
        internal static unsafe void AddMessages(NativeArray<LogEntryStruct> messages) =>
            LogEntries.AddMessagesImpl(messages.GetUnsafeReadOnlyPtr(), messages.Length);

        internal static unsafe void AddMessage(ref LogEntryStruct message)
        {
            fixed (LogEntryStruct* entry = &message) LogEntries.AddMessagesImpl(entry, 1);
        }
    }
}
