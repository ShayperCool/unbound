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
                MethodBase inner = null;
                for (int frameIndex = 0; frameIndex < captured.FrameCount; inner = captured.GetFrame(frameIndex)?.GetMethod(), frameIndex++)
                {
                    var frame = captured.GetFrame(frameIndex);
                    var method = frame?.GetMethod();
                    if (method == null) continue;
                    var place = UnityPlace(method, inner, out bool unity);
                    if (!unity && !IsGameMethod(method)) continue;
                    AppendCapturedMethod(text, method);
                    if (!unity) AppendSource(text, frame, ProjectFolder);
                    else if (place.Document != null) text.Append(" (at ").Append(place.Document).Append(':').Append(place.Line).Append(')');
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
                bool unity = false;
                for (int i = 0; i < call.FrameCount; i++)
                {
                    var frame = call.GetFrame(i);
                    var method = frame?.GetMethod();
                    if (method == null || method.DeclaringType == null) continue;
                    bool game = IsGameMethod(method);
                    bool entry = method.DeclaringType == typeof(UnityEngine.Debug) && method.Name == nameof(UnityEngine.Debug.LogException);
                    string unitySource = game || entry ? null : UnitySource(method, i > 0 ? call.GetFrame(i - 1)?.GetMethod() : null, out unity);
                    if (!game && !entry && !unity) continue;
                    AppendManagedMethod(text, method);
                    if (game) AppendSource(text, frame, ProjectFolder);
                    else if (unitySource != null) text.Append(" (at ").Append(unitySource).Append(')');
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
            MethodBase inner = null;
            for (int i = 0; i < captured.FrameCount; inner = captured.GetFrame(i)?.GetMethod(), i++)
            {
                var frame = captured.GetFrame(i);
                var method = frame?.GetMethod();
                bool unity = false;
                var unityPlace = method != null ? UnityPlace(method, inner, out unity) : default;
                if (method != null && (unity || IsGameMethod(method)))
                {
                    if (text.Length != 0) text.Append('\n');
                    if (separate) text.Append(ForeignStackEnd).Append('\n');
                    separate = false;
                    text.Append("  at ");
                    AppendCapturedMethod(text, method);
                    var (offset, file, line) = unity ? (unityPlace.Offset, unityPlace.Document, unityPlace.Line) : Place(frame, method);
                    text.Append(" [0x").Append(Math.Max(offset, 0).ToString("x5")).Append("] in ");
                    if (file == null) text.Append('<').Append(method.Module.ModuleVersionId.ToString("N")).Append('>');
                    else text.Append(AbsoluteSourcePath(file, ProjectInfo.ProjectPath));
                    text.Append(':').Append(line).Append(' ');
                }
                if (frame != null && IsLastForeignFrame(frame) && text.Length != 0) separate = true;
            }
            return text.ToString();
        }

        /// <summary>Where a frame is: the start of the statement holding its instruction, that
        /// statement's document and line, from the assembly's .pdb with any hot-reload edits applied
        /// (the runtime's own answer without one).</summary>
        static (int Offset, string File, int Line) Place(StackFrame frame, MethodBase method)
        {
            int offset = frame.GetILOffset();
            if (offset >= 0 && method != null && PortablePdb.Of(method.Module.Assembly) is PortablePdb pdb
                && pdb.Statement(method.MetadataToken, offset, out int start, out string document, out int line))
                return (start, document ?? frame.GetFileName(), line);
            return (offset, frame.GetFileName(), frame.GetFileLineNumber());
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

        static readonly ConcurrentDictionary<(MethodBase, string, int), (int, string, int)> s_UnityPlaces = new();

        /// <summary>Whether <paramref name="method"/> stands for a managed Unity method
        /// (<see cref="Unbound.UnityFrameAttribute"/>), and that method's source place as
        /// <c>path:line</c> (null without the release's .pdb).</summary>
        internal static string UnitySource(MethodBase method, out bool unity) => UnitySource(method, null, out unity);

        /// <summary><see cref="UnitySource(MethodBase, out bool)"/> of a frame whose callee is
        /// <paramref name="inner"/> (the next frame inward, null for none).</summary>
        internal static string UnitySource(MethodBase method, MethodBase inner, out bool unity)
        {
            var place = UnityPlace(method, inner, out unity);
            return place.Document == null ? null : place.Document + ":" + place.Line;
        }

        /// <summary>Whether <paramref name="method"/> stands for a managed Unity method, and where
        /// that method makes the call the frame is in: the statement's IL offset, document and line
        /// in the release's UnityEngine.CoreModule .pdb (no document without it). The call is the
        /// first to the attribute's callee, or, where the callee is an engine method standing for
        /// Unity's (<paramref name="inner"/>), the first call of that method (the method its override
        /// overrides): UnityEvent.Invoke calls an InvokableCall's Invoke() or a call's Invoke(object[])
        /// at different places (matches the Unity 6000.0.71f1 Editor, tests/projects/eventstacks).</summary>
        internal static (int Offset, string Document, int Line) UnityPlace(MethodBase method, MethodBase inner, out bool unity)
        {
            unity = false;
            if (method.Module.Assembly != s_Engine) return default;
            var frame = method.GetCustomAttribute<Unbound.UnityFrameAttribute>();
            unity = frame != null;
            if (!unity) return default;
            string data = UnityEditor.UnityInstallInfo.DataPath;
            if (string.IsNullOrEmpty(data)) return default;
            string owner = null;
            int count = -1;
            if (inner != null && inner.Module.Assembly == s_Engine && inner.IsDefined(typeof(Unbound.UnityFrameAttribute), false))
            {
                var called = inner is MethodInfo info ? info.GetBaseDefinition() : inner;
                owner = DefinitionName(called.DeclaringType);
                count = called.GetParameters().Length;
            }
            return s_UnityPlaces.GetOrAdd((method, owner, count), key =>
            {
                var (m, calleeOwner, calleeParameters) = key;
                var pdb = PortablePdb.OfFile(Path.Combine(data, "Managed", "UnityEngine", "UnityEngine.CoreModule.dll"));
                var definition = m.DeclaringType.IsConstructedGenericType ? m.Module.ResolveMethod(m.MetadataToken) : m;
                var parameters = Array.ConvertAll(definition.GetParameters(), p => PortablePdb.ParameterTypeName(p.ParameterType));
                return pdb != null && pdb.CallSite(DefinitionName(m.DeclaringType), m.Name, parameters.Length, parameters, frame.Callee,
                        calleeOwner, calleeParameters, out int start, out var document, out int line)
                    ? (start, document, line)
                    : (0, null, 0);
            });
        }

        /// <summary><c>Namespace.Name</c> of a type, a constructed generic one by its definition's.</summary>
        static string DefinitionName(Type type)
        {
            if (type.IsConstructedGenericType) type = type.GetGenericTypeDefinition();
            return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
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
            // Only an edit makes the runtime's line differ from the .pdb's.
            var (_, file, line) = PortablePdb.Edited
                ? Place(frame, frame.GetMethod())
                : (0, frame.GetFileName(), frame.GetFileLineNumber());
            if (string.IsNullOrEmpty(file)) return;
            text.Append(" (at ").Append(StackSourcePath(file, projectFolder, ProjectInfo.ProjectPath)).Append(':')
                .Append(line).Append(')');
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
