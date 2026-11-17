using System;
using System.Globalization;

namespace Unbound.Runtime
{
    /// <summary>
    /// Exception texts as the Editor's Mono runtime writes them (Unity 6000.0.71f1,
    /// tests/projects/apierrors <c>format.*</c>). An argument exception with a parameter name has
    /// the message, then "\nParameter name: x", then "\nActual value was v." for an out-of-range
    /// value with one; .NET writes " (Parameter 'x')" after the message instead. The exceptions
    /// themselves stay the class library's (type, parameter name, actual value): game code reads
    /// their texts through these (Message, ToString() and the formatting calls are redirected when
    /// its assemblies load, Unbound.Host.NumberFormatShims), and so does the engine's log.
    /// </summary>
    internal static class MonoExceptions
    {
        const string ParameterName = "\nParameter name: ";

        /// <summary>What Unity's runtime throws for a call through a null reference (no period
        /// at the end, unlike .NET's message).</summary>
        internal static NullReferenceException NullReference() => new NullReferenceException("Object reference not set to an instance of an object");

        /// <summary>An exception's message as Mono writes it.</summary>
        internal static string MessageOf(Exception exception) => Mono(exception, exception.Message);

        /// <summary>An exception's ToString() as Mono writes the head of each exception in it
        /// ("Class: message", Mono's messages) and joins an inner exception on the same line
        /// (" ---> ", where .NET starts a new line). The stack lines stay the runtime's.</summary>
        internal static string ToStringOf(Exception exception)
        {
            string text = exception.ToString();
            int from = 0;
            for (var current = exception; current != null; current = current.InnerException)
            {
                string message = current.Message;
                string head = string.IsNullOrEmpty(message) ? current.GetType().ToString() : current.GetType() + ": " + message;
                int start = text.IndexOf(head, from, StringComparison.Ordinal);
                if (start < 0) break;
                int end = start + head.Length;
                if (!string.IsNullOrEmpty(message))
                {
                    string mono = Mono(current, message);
                    text = text.Substring(0, end - message.Length) + mono + text.Substring(end);
                    end += mono.Length - message.Length;
                }
                if (current.InnerException != null && string.CompareOrdinal(text, end, NetInner, 0, NetInner.Length) == 0)
                    text = text.Remove(end, Environment.NewLine.Length);
                from = end;
            }
            return text;
        }

        /// <summary>How .NET starts an inner exception's text (Mono: " ---> " on the same line).</summary>
        static readonly string NetInner = Environment.NewLine + " ---> ";

        /// <summary>A message with .NET's " (Parameter 'x')" of an argument exception with a
        /// parameter name written as Mono writes it, and an out-of-range float or double actual
        /// value in Mono's number format.</summary>
        static string Mono(Exception exception, string message)
        {
            if (exception is not ArgumentException argument || message == null) return message;
            if (argument is ArgumentOutOfRangeException { ActualValue: float or double } range)
            {
                // The class library writes the value with the current culture's default format.
                string net = ActualValue + string.Format(CultureInfo.CurrentCulture, "{0}", range.ActualValue) + ".";
                int value = message.LastIndexOf(net, StringComparison.Ordinal);
                if (value >= 0)
                {
                    string mono = ActualValue + MonoNumberFormat.ObjectToString(range.ActualValue) + ".";
                    message = message.Substring(0, value) + mono + message.Substring(value + net.Length);
                }
            }
            if (string.IsNullOrEmpty(argument.ParamName)) return message;
            string parameter = " (Parameter '" + argument.ParamName + "')";
            int at = message.LastIndexOf(parameter, StringComparison.Ordinal);
            return at < 0 ? message : message.Substring(0, at) + ParameterName + argument.ParamName + message.Substring(at + parameter.Length);
        }

        const string ActualValue = "Actual value was ";
    }
}
