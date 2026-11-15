using System;

namespace Unbound.Runtime
{
    /// <summary>
    /// Argument exceptions with the messages Unity's Mono runtime gives them. Unity's code passes a
    /// parameter name (often a whole sentence); Mono writes it after the message on its own line,
    /// where .NET appends " (Parameter '...')". The parameter name itself is not kept, as .NET would
    /// append it again.
    /// </summary>
    internal static class MonoExceptions
    {
        const string ParameterName = "\nParameter name: ";

        /// <summary>Unity's <c>new ArgumentNullException(paramName)</c>.</summary>
        internal static ArgumentNullException Null(string paramName) =>
            new ArgumentNullException(null, "Value cannot be null." + ParameterName + paramName);

        /// <summary>Unity's <c>new ArgumentOutOfRangeException(paramName)</c>.</summary>
        internal static ArgumentOutOfRangeException OutOfRange(string paramName) =>
            new ArgumentOutOfRangeException(null, "Specified argument was out of the range of valid values." + ParameterName + paramName);

        /// <summary>Unity's <c>new ArgumentOutOfRangeException(paramName, message)</c>.</summary>
        internal static ArgumentOutOfRangeException OutOfRange(string paramName, string message) =>
            new ArgumentOutOfRangeException(null, message + ParameterName + paramName);

        /// <summary>Unity's <c>new ArgumentException(message, paramName)</c>.</summary>
        internal static ArgumentException Argument(string message, string paramName) =>
            new ArgumentException(message + ParameterName + paramName);
    }
}
