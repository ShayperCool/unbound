using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Unbound.Runtime
{
    internal static partial class UnityStackTrace
    {
        internal static (string Message, string Trace) ExceptionText(Exception exception)
        {
            var chain = new List<Exception>();
            for (var current = exception; current != null; current = current.InnerException) chain.Add(current);
            var policy = Application.GetStackTraceLogType(LogType.Exception);
            bool measured = Compat.CapturedStackTracesMeasured && policy != StackTraceLogType.Full;
            if (measured && policy == StackTraceLogType.None)
                return (ExceptionMessage(chain[chain.Count - 1]), "");
            var text = new StringBuilder();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var current = chain[i];
                if (i != chain.Count - 1)
                {
                    if (!measured) text.Append('\n');
                    text.Append("Rethrow as ").Append(ExceptionMessage(current, measured)).Append('\n');
                }
                if (!measured) { text.Append(current.StackTrace ?? ""); continue; }
                var captured = new StackTrace(current, true);
                for (int frameIndex = 0; frameIndex < captured.FrameCount; frameIndex++)
                {
                    var frame = captured.GetFrame(frameIndex);
                    var method = frame?.GetMethod();
                    if (method == null || !IsGameMethod(method)) continue;
                    AppendCapturedMethod(text, method);
                    AppendSource(text, frame, ProjectInfo.ProjectPath);
                    text.Append('\n');
                }
            }
            // Runtime catches no longer have the throwing callback on their current stack.
            // A direct script log does, and includes its actual public logging entry point.
            if (!measured) return (ExceptionMessage(chain[chain.Count - 1], false), text.ToString());
            var call = new StackTrace(true);
            bool hasCaller = false;
            for (int i = 0; i < call.FrameCount; i++)
            {
                var method = call.GetFrame(i)?.GetMethod();
                if (method != null && IsGameMethod(method)) { hasCaller = true; break; }
            }
            if (hasCaller)
            {
                for (int i = 0; i < call.FrameCount; i++)
                {
                    var frame = call.GetFrame(i);
                    var method = frame?.GetMethod();
                    if (method == null || method.DeclaringType == null) continue;
                    bool game = IsGameMethod(method);
                    bool entry = method.DeclaringType == typeof(UnityEngine.Debug) && method.Name == nameof(UnityEngine.Debug.LogException);
                    if (!game && !entry) continue;
                    var type = method.DeclaringType;
                    if (!string.IsNullOrEmpty(type.Namespace)) text.Append(type.Namespace).Append('.');
                    text.Append(type.Name).Append(':').Append(method.Name).Append('(');
                    var parameters = method.GetParameters();
                    for (int parameter = 0; parameter < parameters.Length; parameter++)
                    {
                        if (parameter != 0) text.Append(", ");
                        text.Append(parameters[parameter].ParameterType.Name);
                    }
                    text.Append(')');
                    if (game) AppendSource(text, frame, ProjectInfo.ProjectPath);
                    text.Append('\n');
                }
            }
            return (ExceptionMessage(chain[chain.Count - 1]), text.ToString());
        }

        static void AppendCapturedMethod(StringBuilder text, MethodBase method)
        {
            var owner = method.DeclaringType;
            if (owner.IsConstructedGenericType)
            {
                owner = owner.GetGenericTypeDefinition();
                method = method.Module.ResolveMethod(method.MetadataToken);
            }
            if (method is MethodInfo generic && generic.IsGenericMethod)
                method = generic.GetGenericMethodDefinition();
            text.Append(owner.ToString()).Append('.').Append(method.Name);
            if (method.IsGenericMethod)
            {
                text.Append('[');
                var arguments = method.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i != 0) text.Append(',');
                    text.Append(arguments[i].Name);
                }
                text.Append(']');
            }
            text.Append(" (");
            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i != 0) text.Append(", ");
                var type = parameters[i].ParameterType;
                if (type.IsConstructedGenericType) type = type.GetGenericTypeDefinition();
                text.Append(type.ToString());
                if (parameters[i].Name != null) text.Append(' ').Append(parameters[i].Name);
            }
            text.Append(')');
        }

        static string ExceptionMessage(Exception exception, bool measured = true) => measured && string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name : exception.GetType().Name + ": " + exception.Message;

        static void AppendSource(StringBuilder text, StackFrame frame, string projectFolder)
        {
            string file = frame.GetFileName();
            if (string.IsNullOrEmpty(file)) return;
            text.Append(" (at ").Append(SourcePath(file, projectFolder)).Append(':')
                .Append(frame.GetFileLineNumber()).Append(')');
        }

        static readonly string[] s_SourceRoots = { "Assets", "Packages" };

        internal static string SourcePath(string file, string projectFolder)
        {
            file = file.Replace('\\', '/');
            if (string.IsNullOrEmpty(projectFolder)) return file;
            string logical = projectFolder.Replace('\\', '/').TrimEnd('/') + "/";
            if (file.StartsWith(logical, StringComparison.Ordinal)) return file.Substring(logical.Length);
            if (!Path.IsPathRooted(projectFolder) || !Directory.Exists(projectFolder)) return file;
            try
            {
                string physical = ConsoleLogPath.Resolve(projectFolder).Replace('\\', '/').TrimEnd('/') + "/";
                if (file.StartsWith(physical, StringComparison.Ordinal)) return file.Substring(physical.Length);
                // A runtime project view links source roots individually.
                foreach (string sourceRoot in s_SourceRoots)
                {
                    string directory = Path.Combine(projectFolder, sourceRoot);
                    if (!Directory.Exists(directory)) continue;
                    string resolved = ConsoleLogPath.Resolve(directory).Replace('\\', '/').TrimEnd('/') + "/";
                    if (file.StartsWith(resolved, StringComparison.Ordinal))
                        return sourceRoot + "/" + file.Substring(resolved.Length);
                }
                return file;
            }
            catch (IOException) { return file; }
            catch (UnauthorizedAccessException) { return file; }
        }
    }
}
