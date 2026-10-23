using System;
using System.Diagnostics;
using System.Globalization;
using Unbound.Runtime;

namespace UnityEngine
{



    public partial interface ILogHandler
    {
        void LogFormat(LogType logType, Object context, string format, params object[] args);
        void LogException(Exception exception, Object context);
    }

    public partial interface ILogger : ILogHandler
    {
        ILogHandler logHandler { get; set; }
        bool logEnabled { get; set; }
        LogType filterLogType { get; set; }
        bool IsLogTypeAllowed(LogType logType);
        void Log(LogType logType, object message);
        void Log(LogType logType, object message, Object context);
        void Log(LogType logType, string tag, object message);
        void Log(LogType logType, string tag, object message, Object context);
        void Log(object message);
        void Log(string tag, object message);
        void Log(string tag, object message, Object context);
        void LogWarning(string tag, object message);
        void LogWarning(string tag, object message, Object context);
        void LogError(string tag, object message);
        void LogError(string tag, object message, Object context);
        void LogFormat(LogType logType, string format, params object[] args);
        void LogException(Exception exception);
    }

    public partial class Logger : ILogger
    {
        const string kNoTagFormat = "{0}";
        const string kTagFormat = "{0}: {1}";

        public Logger(ILogHandler logHandler)
        {
            this.logHandler = logHandler;
            logEnabled = true;
            filterLogType = LogType.Log;
        }

        public ILogHandler logHandler { get; set; }
        public bool logEnabled { get; set; }
        public LogType filterLogType { get; set; }

        public bool IsLogTypeAllowed(LogType logType)
        {
            if (!logEnabled) return false;
            if (logType == LogType.Exception) return true;
            if (filterLogType != LogType.Exception) return logType <= filterLogType;
            return false;
        }

        static string GetString(object message)
        {
            if (message == null) return "Null";
            return message is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : message.ToString();
        }

        public void Log(LogType logType, object message)
        {
            if (IsLogTypeAllowed(logType)) logHandler.LogFormat(logType, null, kNoTagFormat, GetString(message));
        }

        public void Log(LogType logType, object message, Object context)
        {
            if (IsLogTypeAllowed(logType)) logHandler.LogFormat(logType, context, kNoTagFormat, GetString(message));
        }

        public void Log(LogType logType, string tag, object message)
        {
            if (IsLogTypeAllowed(logType)) logHandler.LogFormat(logType, null, kTagFormat, tag, GetString(message));
        }

        public void Log(LogType logType, string tag, object message, Object context)
        {
            if (IsLogTypeAllowed(logType)) logHandler.LogFormat(logType, context, kTagFormat, tag, GetString(message));
        }

        public void Log(object message) => Log(LogType.Log, message);
        public void Log(string tag, object message) => Log(LogType.Log, tag, message);
        public void Log(string tag, object message, Object context) => Log(LogType.Log, tag, message, context);
        public void LogWarning(string tag, object message) => Log(LogType.Warning, tag, message);
        public void LogWarning(string tag, object message, Object context) => Log(LogType.Warning, tag, message, context);
        public void LogError(string tag, object message) => Log(LogType.Error, tag, message);
        public void LogError(string tag, object message, Object context) => Log(LogType.Error, tag, message, context);

        public void LogFormat(LogType logType, string format, params object[] args)
        {
            if (IsLogTypeAllowed(logType)) logHandler.LogFormat(logType, null, format, args);
        }

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            if (IsLogTypeAllowed(logType)) logHandler.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception)
        {
            if (logEnabled) logHandler.LogException(exception, null);
        }

        public void LogException(Exception exception, Object context)
        {
            if (logEnabled) logHandler.LogException(exception, context);
        }
    }

    /// <summary>The default log handler: the native console and Application.logMessageReceived.</summary>
    internal sealed class DebugLogHandler : ILogHandler
    {
        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            string message = args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
            var trace = logType == LogType.Log && Application.GetStackTraceLogType(logType) == StackTraceLogType.None
                ? ""
                : new StackTrace(3, true).ToString();
            Log.Write(logType, message, trace, context);
        }

        public void LogException(Exception exception, Object context)
        {
            if (exception == null) return;
            Log.Write(LogType.Exception, $"{exception.GetType().Name}: {exception.Message}", exception.StackTrace ?? "", context, exception);
        }
    }

    public static partial class Debug
    {
        internal static readonly ILogger s_DefaultLogger = new Logger(new DebugLogHandler());
        static ILogger s_Logger = s_DefaultLogger;

        public static ILogger unityLogger => s_Logger;

        public static bool developerConsoleEnabled { get; set; }
        public static bool developerConsoleVisible { get; set; }
        public static bool isDebugBuild => true;
        public static ILogger logger => s_Logger;

        [Conditional("UNITY_ASSERTIONS")]
        public static void Assert(bool condition)
        {
            if (!condition) unityLogger.Log(LogType.Assert, "Assertion failed");
        }

        [Conditional("UNITY_ASSERTIONS")]
        public static void Assert(bool condition, Object context)
        {
            if (!condition) unityLogger.Log(LogType.Assert, (object)"Assertion failed", context);
        }

        [Conditional("UNITY_ASSERTIONS")]
        public static void Assert(bool condition, object message)
        {
            if (!condition) unityLogger.Log(LogType.Assert, message);
        }

        [Conditional("UNITY_ASSERTIONS")]
        public static void Assert(bool condition, string message)
        {
            if (!condition) unityLogger.Log(LogType.Assert, message);
        }

        [Conditional("UNITY_ASSERTIONS")]
        public static void Assert(bool condition, object message, Object context)
        {
            if (!condition) unityLogger.Log(LogType.Assert, message, context);
        }

        [Conditional("UNITY_ASSERTIONS")]
        public static void Assert(bool condition, string message, Object context)
        {
            if (!condition) unityLogger.Log(LogType.Assert, (object)message, context);
        }

        [Conditional("UNITY_ASSERTIONS")]
        public static void AssertFormat(bool condition, string format, params object[] args)
        {
            if (!condition) unityLogger.LogFormat(LogType.Assert, format, args);
        }

        [Conditional("UNITY_ASSERTIONS")]
        public static void AssertFormat(bool condition, Object context, string format, params object[] args)
        {
            if (!condition) unityLogger.LogFormat(LogType.Assert, context, format, args);
        }

        [Conditional("UNITY_ASSERTIONS")]
        public static void LogAssertion(object message) => unityLogger.Log(LogType.Assert, message);

        [Conditional("UNITY_ASSERTIONS")]
        public static void LogAssertion(object message, Object context) => unityLogger.Log(LogType.Assert, message, context);

        [Conditional("UNITY_ASSERTIONS")]
        public static void LogAssertionFormat(string format, params object[] args) => unityLogger.LogFormat(LogType.Assert, format, args);

        [Conditional("UNITY_ASSERTIONS")]
        public static void LogAssertionFormat(Object context, string format, params object[] args) => unityLogger.LogFormat(LogType.Assert, context, format, args);

        public static void Log(object message) => unityLogger.Log(LogType.Log, message);

        public static void Log(object message, Object context) => unityLogger.Log(LogType.Log, message, context);

        public static void LogFormat(string format, params object[] args) => unityLogger.LogFormat(LogType.Log, format, args);

        public static void LogFormat(Object context, string format, params object[] args) => unityLogger.LogFormat(LogType.Log, context, format, args);

        public static void LogFormat(LogType logType, LogOption logOptions, Object context, string format, params object[] args) =>
            unityLogger.LogFormat(logType, context, format, args);

        public static void LogWarning(object message) => unityLogger.Log(LogType.Warning, message);

        public static void LogWarning(object message, Object context) => unityLogger.Log(LogType.Warning, message, context);

        public static void LogWarningFormat(string format, params object[] args) => unityLogger.LogFormat(LogType.Warning, format, args);

        public static void LogWarningFormat(Object context, string format, params object[] args) => unityLogger.LogFormat(LogType.Warning, context, format, args);

        public static void LogError(object message) => unityLogger.Log(LogType.Error, message);

        public static void LogError(object message, Object context) => unityLogger.Log(LogType.Error, message, context);

        public static void LogErrorFormat(string format, params object[] args) => unityLogger.LogFormat(LogType.Error, format, args);

        public static void LogErrorFormat(Object context, string format, params object[] args) => unityLogger.LogFormat(LogType.Error, context, format, args);

        public static void LogException(Exception exception) => unityLogger.LogException(exception, null);

        public static void LogException(Exception exception, Object context) => unityLogger.LogException(exception, context);

        public static void Break() { }

        public static void DebugBreak() { }

        public static void ClearDeveloperConsole() { }

        public static void DrawLine(Vector3 start, Vector3 end, [Internal.DefaultValue("Color.white")] Color color, [Internal.DefaultValue("0.0f")] float duration, [Internal.DefaultValue("true")] bool depthTest) { }
        public static void DrawLine(Vector3 start, Vector3 end, Color color, float duration) { }
        public static void DrawLine(Vector3 start, Vector3 end, Color color) { }
        public static void DrawLine(Vector3 start, Vector3 end) { }
        public static void DrawRay(Vector3 start, Vector3 dir, [Internal.DefaultValue("Color.white")] Color color, [Internal.DefaultValue("0.0f")] float duration, [Internal.DefaultValue("true")] bool depthTest) { }
        public static void DrawRay(Vector3 start, Vector3 dir, Color color, float duration) { }
        public static void DrawRay(Vector3 start, Vector3 dir, Color color) { }
        public static void DrawRay(Vector3 start, Vector3 dir) { }

        public static int ExtractStackTraceNoAlloc(byte[] buffer, int bufferMax, string projectFolder) => 0;

        public static void RetrieveStartupLogs() { }
    }
}

namespace Unbound.Runtime
{
    /// <summary>Where log messages go: Application's log events, then the native console.</summary>
    internal static class Log
    {
        internal static void Write(UnityEngine.LogType type, string message, string stackTrace, UnityEngine.Object context, Exception exception = null)
        {
            UnityEngine.Application.Internal_InvokeLogCallbacks(message, stackTrace, type);
            string text = exception != null ? $"{message}\n{stackTrace}" : message;
            if (type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Assert)
                text = $"{message}\n{stackTrace}";
            Native.Log((int)type, text);
        }
    }
}
