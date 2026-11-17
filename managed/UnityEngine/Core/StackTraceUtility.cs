using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Unbound.Runtime;

namespace Unbound
{
    /// <summary>
    /// Marks an engine method that stands for the managed Unity method of the same type, name and
    /// parameters: stack texts list its frame as Unity lists that method's, with the place in
    /// Unity's source where that method calls <see cref="Callee"/> (read from the release's
    /// UnityEngine.CoreModule .pdb; no place without it).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class UnityFrameAttribute : Attribute
    {
        public UnityFrameAttribute(string callee) => Callee = callee;

        public string Callee { get; }
    }
}

namespace UnityEngine
{
    /// <summary>
    /// Stack texts as the Unity 6000.0.71f1 Editor writes them (tests/projects/apierrors
    /// <c>format.extract.*</c>). An exception's text is its innermost exception's
    /// "Type: Message" line, then each exception's own stack in Mono's layout, innermost first,
    /// the outer ones each after "Rethrow as Type: Message", then the caller's stack.
    /// </summary>
    public static partial class StackTraceUtility
    {
        static string projectFolder;

        /// <summary>The prefix removed from source paths: the project's folder unless set.</summary>
        static string ProjectFolder => projectFolder ?? UnityStackTrace.ProjectFolder;

        internal static void SetProjectFolder(string folder)
        {
            projectFolder = string.IsNullOrEmpty(folder) ? folder : folder.Replace("\\", "/");
        }

        [Unbound.UnityFrame("ExtractStackTraceNoAlloc")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe string ExtractStackTrace()
        {
            const int bufferSize = 16384;
            byte* buffer = stackalloc byte[bufferSize];
            int length = Debug.ExtractStackTraceNoAlloc(buffer, bufferSize, ProjectFolder);
            if (length > 0) return new string((sbyte*)buffer, 0, length, Encoding.UTF8);
            return ExtractFormattedStackTrace(new StackTrace(1, true));
        }

        [Unbound.UnityFrame("ExtractStringFromExceptionInternal")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ExtractStringFromException(object exception)
        {
            ExtractStringFromExceptionInternal(exception, out var message, out var stackTrace);
            return message + "\n" + stackTrace;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ExtractStringFromExceptionInternal(object exceptiono, out string message, out string stackTrace)
        {
            if (exceptiono == null)
                throw new ArgumentException("ExtractStringFromExceptionInternal called with null exception");
            if (exceptiono is not Exception exception)
                throw new ArgumentException("ExtractStringFromExceptionInternal called with an exception that was not of type System.Exception");
            message = "";
            // Null while the outermost exception was never thrown (and has no inner one).
            string trace = "";
            for (var current = exception; current != null; current = current.InnerException)
            {
                string own = UnityStackTrace.MonoTrace(current);
                trace = trace.Length != 0 ? own + "\n" + trace : own;
                string line = current.GetType().Name;
                string text = MonoExceptions.MessageOf(current) ?? "";
                if (text.Trim().Length != 0) line += ": " + text;
                message = line;
                if (current.InnerException != null) trace = "Rethrow as " + line + "\n" + trace;
            }
            stackTrace = trace + "\n" + ExtractFormattedStackTrace(new StackTrace(1, true));
        }

        /// <summary>One line per frame, <c>Namespace.Type:Method(ParameterType, ...)</c> with
        /// <c>(at path:line)</c> where the frame has a source, the project's folder removed from the
        /// path. Only the game's frames and those of engine methods standing for Unity's managed
        /// ones are listed (<see cref="UnityStackTrace"/>).</summary>
        internal static string ExtractFormattedStackTrace(StackTrace stackFrames)
        {
            var text = new StringBuilder(255);
            for (int i = 0; i < stackFrames.FrameCount; i++)
            {
                var frame = stackFrames.GetFrame(i);
                var method = frame?.GetMethod();
                if (method?.DeclaringType == null) continue;
                string unitySource = UnityStackTrace.UnitySource(method, out bool unity);
                if (!unity && !UnityStackTrace.IsGameMethod(method)) continue;
                UnityStackTrace.AppendManagedMethod(text, method);
                if (unity)
                {
                    if (unitySource != null) text.Append(" (at ").Append(unitySource).Append(')');
                }
                else if (!method.IsDefined(typeof(HideInCallstackAttribute), true))
                    UnityStackTrace.AppendSource(text, frame, ProjectFolder);
                text.Append('\n');
            }
            return text.ToString();
        }
    }

    internal partial class SetupCoroutine
    {
        /// <summary>Advances a coroutine: every step of one runs through here, as in Unity, where
        /// stack texts list this frame below the routine's.</summary>
        [Unbound.UnityFrame("MoveNext")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe void InvokeMoveNext(System.Collections.IEnumerator enumerator, IntPtr returnValueAddress)
        {
            if (returnValueAddress == IntPtr.Zero)
                throw new ArgumentException("Return value address cannot be 0.", "returnValueAddress");
            *(bool*)(void*)returnValueAddress = enumerator.MoveNext();
        }
    }
}

namespace UnityEngine
{
    /// <summary>Leaves a method's source place out of stack texts (its frame stays listed).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed partial class HideInCallstackAttribute : Attribute
    {
    }
}
