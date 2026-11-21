using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor.ShaderKeywordFilter
{
    // The keyword filter attributes render pipelines put on their settings fields, which the
    // Editor's shader build reads. Unbound builds no players, so they only hold what they are
    // given and answer the internal questions as the Editor does (matches the Unity 6000.0.71f1
    // Editor, tests/projects/keywordfilter); the settings walk that collects them stays a stub.

    public enum FilterAction
    {
        Select = 0,
        Remove = 1,
        SelectOrRemove = 2,
    }

    /// <summary>One keyword rule a build applies: the keyword, whether the empty keyword counts,
    /// the action and why.</summary>
    internal partial struct FilterRule
    {
        internal string keywordName;
        internal bool withEmptyKeyword;
        internal FilterAction action;
        internal string resolutionMessage;

        internal FilterRule(string keywordName, bool withEmptyKeyword, FilterAction action, string resolutionMessage)
        {
            this.keywordName = keywordName;
            this.withEmptyKeyword = withEmptyKeyword;
            this.action = action;
            this.resolutionMessage = resolutionMessage;
        }
    }

    /// <summary>
    /// A keyword rule on a settings field: an action on the named keywords, taken when the field's
    /// value equals the condition (or, negated, when it does not); a select-or-remove rule always
    /// applies, selecting on a match and removing otherwise. The file is the declaring file's name,
    /// for the build's messages.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public partial class FilterAttribute : Attribute
    {
        public enum Precedence
        {
            Normal = 0,
            Override = 1,
        }

        public enum EvaluationMode
        {
            Normal = 0,
            Negated = 1,
        }

        static bool s_DebugLogging;

        readonly FilterAction m_Action;
        readonly Precedence m_Precedence;
        readonly EvaluationMode m_EvaluationMode;
        readonly string[] m_Names;
        readonly object m_Condition;
        readonly string m_FileName;
        readonly int m_LineNumber;

        public FilterAttribute(FilterAction action, Precedence precedence, EvaluationMode evaluationMode, object condition, string fileName, int lineNumber, params string[] keywordNames)
        {
            m_Action = action;
            m_Precedence = precedence;
            m_EvaluationMode = evaluationMode;
            m_Condition = condition;
            m_Names = keywordNames;
            m_FileName = fileName;
            m_LineNumber = lineNumber;
            if (s_DebugLogging)
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0} attribute declared at {1}:{2} for keyword(s) '{3}' predicated on the value '{4}'",
                    GetFilterActionName(m_Action), m_FileName, m_LineNumber, string.Join(", ", m_Names), m_Condition);
        }

        internal Precedence RulePrecedence => m_Precedence;
        internal string[] KeywordNames => m_Names;
        internal FilterAction Action => m_Action;

        /// <summary>The precedence a derived attribute's priority flag asks for.</summary>
        internal static Precedence PrecedenceOf(bool overridePriority) => overridePriority ? Precedence.Override : Precedence.Normal;

        internal string GetFilterActionName(FilterAction action) => action switch
        {
            FilterAction.Select => "Select",
            FilterAction.Remove => "Remove",
            FilterAction.SelectOrRemove => "SelectOrRemove",
            _ => "Unknown",
        };

        internal void EnableLogging() => s_DebugLogging = true;

        internal void DisableLogging() => s_DebugLogging = false;

        internal string GetFormattedResolutionMessageActive(object data) =>
            Resolution($"Applying {GetFilterActionName(m_Action)} attribute declared at {m_FileName}:{m_LineNumber} because condition was '{data}'");

        internal string GetFormattedResolutionMessageInactive(object data) =>
            Resolution($"Skipping {GetFilterActionName(m_Action)} attribute declared at {m_FileName}:{m_LineNumber} because condition was '{data}'");

        internal string GetFormattedResolutionMessageOverride(object data, FilterRule originalRule) =>
            Resolution($"Overriding {GetFilterActionName(originalRule.action)} attribute for keyword '{originalRule.keywordName}' with {GetFilterActionName(m_Action)} attribute declared at {m_FileName}:{m_LineNumber}");

        static string Resolution(string message)
        {
            if (s_DebugLogging) Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, message);
            return message;
        }

        /// <summary>Whether the rule applies for the field's value, and its action then. A null
        /// condition throws NullReferenceException, as in the Editor.</summary>
        internal bool GetActiveFilterAction(object data, out FilterAction action)
        {
            bool matches = m_Condition.Equals(data) != (m_EvaluationMode == EvaluationMode.Negated);
            if (m_Action == FilterAction.SelectOrRemove)
            {
                action = matches ? FilterAction.Select : FilterAction.Remove;
                return true;
            }
            action = m_Action;
            return matches;
        }
    }

    public partial class RemoveIfAttribute : FilterAttribute
    {
        public RemoveIfAttribute(object condition, bool overridePriority = false, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, params string[] keywordNames)
            : base(FilterAction.Remove, PrecedenceOf(overridePriority), EvaluationMode.Normal, condition, Path.GetFileName(filePath), lineNumber, keywordNames) { }
    }

    public partial class RemoveIfNotAttribute : FilterAttribute
    {
        public RemoveIfNotAttribute(object condition, bool overridePriority = false, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, params string[] keywordNames)
            : base(FilterAction.Remove, PrecedenceOf(overridePriority), EvaluationMode.Negated, condition, Path.GetFileName(filePath), lineNumber, keywordNames) { }
    }

    /// <summary>Removes on a match, selects otherwise (a negated select-or-remove).</summary>
    public partial class RemoveOrSelectAttribute : FilterAttribute
    {
        public RemoveOrSelectAttribute(object condition, bool overridePriority = false, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, params string[] keywordNames)
            : base(FilterAction.SelectOrRemove, PrecedenceOf(overridePriority), EvaluationMode.Negated, condition, Path.GetFileName(filePath), lineNumber, keywordNames) { }
    }

    public partial class SelectIfAttribute : FilterAttribute
    {
        public SelectIfAttribute(object condition, bool overridePriority = false, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, params string[] keywordNames)
            : base(FilterAction.Select, PrecedenceOf(overridePriority), EvaluationMode.Normal, condition, Path.GetFileName(filePath), lineNumber, keywordNames) { }
    }

    public partial class SelectIfNotAttribute : FilterAttribute
    {
        public SelectIfNotAttribute(object condition, bool overridePriority = false, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, params string[] keywordNames)
            : base(FilterAction.Select, PrecedenceOf(overridePriority), EvaluationMode.Negated, condition, Path.GetFileName(filePath), lineNumber, keywordNames) { }
    }

    public partial class SelectOrRemoveAttribute : FilterAttribute
    {
        public SelectOrRemoveAttribute(object condition, bool overridePriority = false, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, params string[] keywordNames)
            : base(FilterAction.SelectOrRemove, PrecedenceOf(overridePriority), EvaluationMode.Normal, condition, Path.GetFileName(filePath), lineNumber, keywordNames) { }
    }

    /// <summary>Limits a field's rules to some graphics APIs: they apply for a listed API, or,
    /// negated, for any other.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field, AllowMultiple = false)]
    public partial class GraphicsAPIConstraintAttribute : Attribute
    {
        readonly bool m_Negate;
        readonly GraphicsDeviceType[] m_GraphicsDeviceTypes;

        public GraphicsAPIConstraintAttribute(bool negate, params GraphicsDeviceType[] graphicsDeviceTypes)
        {
            m_Negate = negate;
            m_GraphicsDeviceTypes = graphicsDeviceTypes;
        }

        internal bool Negated => m_Negate;
        internal GraphicsDeviceType[] GraphicsAPIs => m_GraphicsDeviceTypes;

        internal bool ShouldApplyRules(GraphicsDeviceType currentGraphicDeviceType) =>
            Array.IndexOf(m_GraphicsDeviceTypes, currentGraphicDeviceType) >= 0 != m_Negate;
    }

    public partial class ApplyRulesIfGraphicsAPIAttribute : GraphicsAPIConstraintAttribute
    {
        public ApplyRulesIfGraphicsAPIAttribute(params GraphicsDeviceType[] graphicsDeviceTypes) : base(false, graphicsDeviceTypes) { }
    }

    public partial class ApplyRulesIfNotGraphicsAPIAttribute : GraphicsAPIConstraintAttribute
    {
        public ApplyRulesIfNotGraphicsAPIAttribute(params GraphicsDeviceType[] graphicsDeviceTypes) : base(true, graphicsDeviceTypes) { }
    }

    /// <summary>Limits a field's rules to shader tags, given as name/value pairs: they apply when
    /// one of the current pairs equals one of the attribute's, or, negated, when none does; an
    /// unpaired last name takes no part.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field, AllowMultiple = true)]
    public partial class TagConstraintAttribute : Attribute
    {
        readonly bool m_Negate;
        readonly string[] m_Tags;

        public TagConstraintAttribute(bool negate, params string[] tags)
        {
            m_Negate = negate;
            m_Tags = tags;
        }

        internal bool Negated => m_Negate;
        internal string[] Tags => m_Tags;

        internal bool ShouldApplyRules(params string[] currentTags)
        {
            for (int i = 0; i + 1 < currentTags.Length; i += 2)
                for (int j = 0; j + 1 < m_Tags.Length; j += 2)
                    if (currentTags[i].Equals(m_Tags[j]) && currentTags[i + 1].Equals(m_Tags[j + 1]))
                        return !m_Negate;
            return m_Negate;
        }
    }

    public partial class ApplyRulesIfTagsEqualAttribute : TagConstraintAttribute
    {
        public ApplyRulesIfTagsEqualAttribute(params string[] tags) : base(false, tags) { }
    }

    public partial class ApplyRulesIfTagsNotEqualAttribute : TagConstraintAttribute
    {
        public ApplyRulesIfTagsNotEqualAttribute(params string[] tags) : base(true, tags) { }
    }
}
