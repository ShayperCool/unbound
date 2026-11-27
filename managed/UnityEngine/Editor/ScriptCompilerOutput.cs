using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UnityEditor.Scripting.Compilers
{
    internal enum CompilerMessageType
    {
        Error = 0,
        Warning = 1,
        Information = 2,
    }

    /// <summary>A message of a compiler's output, as a parser makes it (Unity's internal type, which
    /// Burst's editor uses).</summary>
    internal partial struct CompilerMessage
    {
        public string message;
        public string file;
        public int line;
        public int column;
        public CompilerMessageType type;
        // Unity keeps this field unused for Burst's sake.
        internal string assemblyName;
    }

    /// <summary>Turns a compiler's output lines into messages with the parser's line pattern, as
    /// Unity's own base class does (Burst's editor derives its parser from it).</summary>
    internal abstract partial class CompilerOutputParserBase
    {
        /// <summary>The longest line matched whole; a longer one is matched by its first
        /// <see cref="k_LongLinePrefix"/> characters.</summary>
        const int k_LongLine = 1000;
        const int k_LongLinePrefix = 100;

        protected static CompilerMessage CreateInternalCompilerErrorMessage(string[] compileroutput) => new CompilerMessage
        {
            file = "",
            message = string.Join(Environment.NewLine, compileroutput),
            type = CompilerMessageType.Error,
            line = 0,
            column = 0,
        };

        protected internal static CompilerMessage CreateCompilerMessageFromMatchedRegex(string line, Match m, string errorId, string informationId = null)
        {
            var message = new CompilerMessage();
            if (m.Groups["filename"].Success) message.file = m.Groups["filename"].Value;
            if (m.Groups["line"].Success) message.line = int.Parse(m.Groups["line"].Value);
            if (m.Groups["column"].Success) message.column = int.Parse(m.Groups["column"].Value);
            message.message = line;
            string messageType = m.Groups["type"].Value;
            if (messageType == errorId) message.type = CompilerMessageType.Error;
            else if (!string.IsNullOrEmpty(informationId) && messageType == informationId) message.type = CompilerMessageType.Information;
            else message.type = CompilerMessageType.Warning;
            return message;
        }

        public virtual IEnumerable<CompilerMessage> Parse(string[] errorOutput, bool compilationHadFailure) =>
            Parse(errorOutput, new string[0], compilationHadFailure);

        /// <summary>Every line the pattern (else the internal-error pattern) matches becomes a message;
        /// a failed compilation without an error message gets one of the whole output.</summary>
        public virtual IEnumerable<CompilerMessage> Parse(string[] errorOutput, string[] standardOutput, bool compilationHadFailure, string assemblyName_unused = null)
        {
            bool hasErrors = false;
            var messages = new List<CompilerMessage>();
            var regex = GetOutputRegex();
            var internalErrorRegex = GetInternalErrorOutputRegex();
            foreach (var line in errorOutput)
            {
                if (!ShouldParseLine(line)) continue;
                var matched = line.Length > k_LongLine ? line.Substring(0, k_LongLinePrefix) : line;
                var m = regex.Match(matched);
                if (!m.Success)
                {
                    if (internalErrorRegex != null) m = internalErrorRegex.Match(matched);
                    if (!m.Success) continue;
                }
                var message = CreateCompilerMessageFromMatchedRegex(line, m, GetErrorIdentifier(), GetInformationIdentifier());
                if (message.type == CompilerMessageType.Error) hasErrors = true;
                messages.Add(message);
            }
            if (compilationHadFailure && !hasErrors) messages.Add(CreateInternalCompilerErrorMessage(errorOutput));
            return messages;
        }

        [Unbound.EditorFaithful("Unity's own base-class default; parsers override it")]
        protected virtual bool ShouldParseLine(string line) => true;

        protected abstract string GetErrorIdentifier();

        [Unbound.EditorFaithful("Unity's own base-class default; parsers override it")]
        protected virtual string GetInformationIdentifier() => "info";

        protected abstract Regex GetOutputRegex();

        [Unbound.EditorFaithful("Unity's own base-class default; parsers override it")]
        protected virtual Regex GetInternalErrorOutputRegex() => null;
    }
}
