using System;
using System.Collections.Concurrent;
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
                    AppendSource(text, frame, ProjectFolder);
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
                    AppendManagedMethod(text, method);
                    if (game) AppendSource(text, frame, ProjectFolder);
                    text.Append('\n');
                }
            }
            return (ExceptionMessage(chain[chain.Count - 1]), text.ToString());
        }

        /// <summary>An exception's own stack as Mono's <c>Exception.StackTrace</c> writes it, null
        /// when it was never thrown: one line per game frame,
        /// <c>"  at Type.Method (ParameterType name) [0xOFFSET] in path:line "</c>, the offset
        /// being where the frame's statement starts, the path absolute; a stack captured elsewhere
        /// and thrown again (ExceptionDispatchInfo) ends with Mono's line for that.</summary>
        internal static string MonoTrace(Exception exception)
        {
            // A class that writes its own stack keeps it.
            if (exception.GetType().GetProperty(nameof(Exception.StackTrace))?.GetMethod?.DeclaringType != typeof(Exception))
                return exception.StackTrace;
            if (exception.StackTrace == null) return null;
            var captured = new StackTrace(exception, true);
            var text = new StringBuilder();
            bool separate = false;
            for (int i = 0; i < captured.FrameCount; i++)
            {
                var frame = captured.GetFrame(i);
                var method = frame?.GetMethod();
                if (method != null && IsGameMethod(method))
                {
                    if (text.Length != 0) text.Append('\n');
                    if (separate) text.Append(ForeignStackEnd).Append('\n');
                    separate = false;
                    text.Append("  at ");
                    AppendCapturedMethod(text, method);
                    int offset = frame.GetILOffset();
                    string file = frame.GetFileName();
                    int line = frame.GetFileLineNumber();
                    if (offset >= 0 && PortablePdb.Of(method.Module.Assembly) is PortablePdb pdb
                        && pdb.Statement(method.MetadataToken, offset, out int start, out string document, out int statementLine))
                        (offset, file, line) = (start, document, statementLine);
                    text.Append(" [0x").Append(Math.Max(offset, 0).ToString("x5")).Append("] in ");
                    if (file == null) text.Append('<').Append(method.Module.ModuleVersionId.ToString("N")).Append('>');
                    else text.Append(AbsoluteSourcePath(file, ProjectInfo.ProjectPath));
                    text.Append(':').Append(line).Append(' ');
                }
                if (frame != null && IsLastForeignFrame(frame) && text.Length != 0) separate = true;
            }
            return text.ToString();
        }

        /// <summary>Mono's line after the frames of a stack captured elsewhere (a blank line follows).</summary>
        const string ForeignStackEnd = "--- End of stack trace from previous location where exception was thrown ---\n";

        static readonly PropertyInfo s_LastForeignFrame = typeof(StackFrame).GetProperty(
            "IsLastFrameFromForeignExceptionStackTrace", BindingFlags.Instance | BindingFlags.NonPublic);

        static bool IsLastForeignFrame(StackFrame frame) => s_LastForeignFrame?.GetValue(frame) is true;

        /// <summary>A source path as the Editor compiled it: in the project's folder for the
        /// project's own sources (a runtime project view resolved), relative to it ("./") for
        /// packages in its package cache (tests/projects/testrunner); other paths as they are.</summary>
        internal static string AbsoluteSourcePath(string file, string projectFolder)
        {
            string relative = SourcePath(file, projectFolder);
            if (relative == file.Replace('\\', '/') || Path.IsPathRooted(relative)) return relative;
            if (relative.StartsWith(PackageCache, StringComparison.Ordinal)) return "./" + relative;
            return projectFolder.Replace('\\', '/').TrimEnd('/') + "/" + relative;
        }

        const string PackageCache = "Library/PackageCache/";

        static readonly ConcurrentDictionary<MethodBase, string> s_UnitySources = new();

        /// <summary>Whether <paramref name="method"/> stands for a managed Unity method
        /// (<see cref="Unbound.UnityFrameAttribute"/>), and that method's source place as
        /// <c>path:line</c> (null without the release's .pdb).</summary>
        internal static string UnitySource(MethodBase method, out bool unity)
        {
            unity = false;
            if (method.Module.Assembly != s_Engine) return null;
            var frame = method.GetCustomAttribute<Unbound.UnityFrameAttribute>();
            unity = frame != null;
            if (!unity) return null;
            string data = UnityEditor.UnityInstallInfo.DataPath;
            if (string.IsNullOrEmpty(data)) return null;
            return s_UnitySources.GetOrAdd(method, m =>
            {
                var pdb = PortablePdb.OfFile(Path.Combine(data, "Managed", "UnityEngine", "UnityEngine.CoreModule.dll"));
                return pdb != null && pdb.CallSite(m.DeclaringType.FullName, m.Name, m.GetParameters().Length, frame.Callee, out var document, out int line)
                    ? document + ":" + line
                    : null;
            });
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

        static string ExceptionMessage(Exception exception, bool measured = true)
        {
            string message = MonoExceptions.MessageOf(exception);
            return measured && string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : exception.GetType().Name + ": " + message;
        }

        /// <summary><c>Namespace.Type:Method(ParameterType, ...)</c>, as Unity's managed stack
        /// texts name a frame's method.</summary>
        internal static void AppendManagedMethod(StringBuilder text, MethodBase method)
        {
            var type = method.DeclaringType;
            if (!string.IsNullOrEmpty(type.Namespace)) text.Append(type.Namespace).Append('.');
            text.Append(type.Name).Append(':').Append(method.Name).Append('(');
            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i != 0) text.Append(", ");
                text.Append(parameters[i].ParameterType.Name);
            }
            text.Append(')');
        }

        /// <summary>The project's folder as Unity removes it from source paths ("" without a project).</summary>
        internal static string ProjectFolder => string.IsNullOrEmpty(ProjectInfo.ProjectPath)
            ? ""
            : ProjectInfo.ProjectPath.Replace('\\', '/').TrimEnd('/') + "/";

        internal static void AppendSource(StringBuilder text, StackFrame frame, string projectFolder)
        {
            string file = frame.GetFileName();
            if (string.IsNullOrEmpty(file)) return;
            text.Append(" (at ").Append(StackSourcePath(file, projectFolder, ProjectInfo.ProjectPath)).Append(':')
                .Append(frame.GetFileLineNumber()).Append(')');
        }

        /// <summary>A frame's source path in Unity's stack texts: the path the Editor compiled
        /// (<see cref="AbsoluteSourcePath"/>, under <paramref name="project"/>), without a leading
        /// <paramref name="projectFolder"/> (matches tests/projects/appfacts).</summary>
        internal static string StackSourcePath(string file, string projectFolder, string project)
        {
            string compiled = AbsoluteSourcePath(file, project);
            if (string.IsNullOrEmpty(projectFolder)) return compiled;
            string prefix = projectFolder.Replace('\\', '/');
            return compiled.StartsWith(prefix, StringComparison.Ordinal) ? compiled.Substring(prefix.Length) : compiled;
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
