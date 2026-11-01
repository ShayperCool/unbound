using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Unbound.Runtime
{
    /// <summary>Stack traces as the Editor's scripting runtime writes them: one frame per line,
    /// <c>Namespace.Type:Method (int,string) (at path:line)</c>, nested types after <c>/</c>,
    /// built-in types by their short runtime names (<c>single</c>, <c>intptr</c>, <c>byte*</c>),
    /// parameters separated by commas without spaces, a source location where the frame has one.
    /// Only the game's frames are listed: the engine's own frames (Unbound's runtime, the class
    /// libraries) stand for the Editor's native code, which writes none.</summary>
    internal static class UnityStackTrace
    {
        /// <summary>The game frames above the <paramref name="skipFrames"/> innermost ones (engine frames
        /// are left out anyway), the first line being
        /// <paramref name="firstLine"/> when given (the engine method the trace was asked from).
        /// Paths starting with <paramref name="projectFolder"/> lose that prefix.</summary>
        internal static string Format(int skipFrames, string firstLine, string projectFolder)
        {
            var text = new StringBuilder();
            if (firstLine != null) text.Append(firstLine).Append('\n');
            var trace = new StackTrace(skipFrames, true);
            for (int i = 0; i < trace.FrameCount; i++)
            {
                var frame = trace.GetFrame(i);
                var method = frame?.GetMethod();
                if (method == null || !IsGameMethod(method)) continue;
                AppendMethod(text, method);
                string file = frame.GetFileName();
                if (!string.IsNullOrEmpty(file))
                {
                    file = file.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(projectFolder) && file.StartsWith(projectFolder, StringComparison.Ordinal))
                        file = file.Substring(projectFolder.Length);
                    text.Append(" (at ").Append(file).Append(':').Append(frame.GetFileLineNumber()).Append(')');
                }
                text.Append('\n');
            }
            return text.ToString();
        }

        static readonly Assembly s_Engine = typeof(UnityStackTrace).Assembly;
        static readonly Assembly s_CoreLib = typeof(object).Assembly;

        static bool IsGameMethod(MethodBase method)
        {
            var assembly = method.DeclaringType?.Assembly ?? method.Module.Assembly;
            if (assembly == s_Engine || assembly == s_CoreLib) return false;
            string name = assembly.GetName().Name ?? "";
            return !name.StartsWith("Unbound.", StringComparison.Ordinal) && !name.StartsWith("System.", StringComparison.Ordinal)
                && name != "System" && name != "netstandard" && name != "mscorlib";
        }

        internal static void AppendMethod(StringBuilder text, MethodBase method)
        {
            if (method.DeclaringType != null) AppendType(text, method.DeclaringType, definition: true);
            text.Append(':').Append(method.Name);
            if (method.IsGenericMethod)
            {
                text.Append('<');
                var arguments = method.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0) text.Append(',');
                    AppendType(text, arguments[i], definition: false);
                }
                text.Append('>');
            }
            text.Append(" (");
            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) text.Append(',');
                AppendType(text, parameters[i].ParameterType, definition: false);
            }
            text.Append(')');
        }

        internal static void AppendType(StringBuilder text, Type type, bool definition)
        {
            if (type.IsByRef) { AppendType(text, type.GetElementType(), false); text.Append('&'); return; }
            if (type.IsPointer) { AppendType(text, type.GetElementType(), false); text.Append('*'); return; }
            if (type.IsArray)
            {
                AppendType(text, type.GetElementType(), false);
                text.Append('[').Append(',', type.GetArrayRank() - 1).Append(']');
                return;
            }
            if (!definition && !type.IsGenericParameter)
            {
                string builtin = BuiltinName(type);
                if (builtin != null) { text.Append(builtin); return; }
            }
            if (type.IsGenericParameter) { text.Append(type.Name); return; }
            if (type.IsNested && type.DeclaringType != null)
            {
                AppendType(text, type.DeclaringType, definition: true);
                text.Append('/');
            }
            else if (!string.IsNullOrEmpty(type.Namespace)) text.Append(type.Namespace).Append('.');
            text.Append(type.Name);
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                text.Append('<');
                var arguments = type.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0) text.Append(',');
                    AppendType(text, arguments[i], definition: false);
                }
                text.Append('>');
            }
        }

        static string BuiltinName(Type type)
        {
            if (type == typeof(void)) return "void";
            if (type == typeof(IntPtr)) return "intptr";
            if (type == typeof(UIntPtr)) return "uintptr";
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: return type.IsEnum ? null : "bool";
                case TypeCode.Char: return type.IsEnum ? null : "char";
                case TypeCode.SByte: return type.IsEnum ? null : "sbyte";
                case TypeCode.Byte: return type.IsEnum ? null : "byte";
                case TypeCode.Int16: return type.IsEnum ? null : "int16";
                case TypeCode.UInt16: return type.IsEnum ? null : "uint16";
                case TypeCode.Int32: return type.IsEnum ? null : "int";
                case TypeCode.UInt32: return type.IsEnum ? null : "uint";
                case TypeCode.Int64: return type.IsEnum ? null : "long";
                case TypeCode.UInt64: return type.IsEnum ? null : "ulong";
                case TypeCode.Single: return "single";
                case TypeCode.Double: return "double";
                case TypeCode.String: return "string";
                case TypeCode.Object: return type == typeof(object) ? "object" : null;
                default: return null;
            }
        }
    }
}
