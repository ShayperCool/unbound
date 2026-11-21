using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.ShaderKeywordFilter;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Records, in Play mode, what the ShaderKeywordFilter attributes hold and answer: each attribute
/// as a field declares it and as code makes it, read through its (internal) members, the actions
/// each answers for matching and other values, and the constraints' rule checks.
/// </summary>
static class KeywordFilterProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    enum Quality { Low, High }

    // Declared as packages declare them on settings fields; the file and line come from here.
    class Settings
    {
        [RemoveIf(true, keywordNames: "KEY_A")]
        [SelectIf(Quality.High, overridePriority: true, keywordNames: new[] { "KEY_B", "KEY_C" })]
        public bool declared;

        [ApplyRulesIfTagsEqual("RenderPipeline", "UniversalPipeline")]
        [ApplyRulesIfNotGraphicsAPI(GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan)]
        public bool constrained;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("KEYWORD_FILTER_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);

        var declared = typeof(Settings).GetField("declared").GetCustomAttributes(typeof(FilterAttribute), false).Cast<FilterAttribute>()
            .OrderBy(a => a.GetType().Name).ToArray();
        for (int i = 0; i < declared.Length; i++) Check("declared." + declared[i].GetType().Name, () => Describe(declared[i], Quality.High, true, false));
        var constrained = typeof(Settings).GetField("constrained");
        Check("declared.tags", () => DescribeTags((TagConstraintAttribute)constrained.GetCustomAttributes(typeof(TagConstraintAttribute), false).Single()));
        Check("declared.graphics", () => DescribeGraphics((GraphicsAPIConstraintAttribute)constrained.GetCustomAttributes(typeof(GraphicsAPIConstraintAttribute), false).Single()));

        Check("made.removeIf", () => Describe(new RemoveIfAttribute(1, false, "Dir/File.cs", 7, "K"), 1, 2, "1"));
        Check("made.removeIfNot", () => Describe(new RemoveIfNotAttribute(1, true, "/abs/Other.cs", 8, "K1", "K2"), 1, 2, null));
        Check("made.removeOrSelect", () => Describe(new RemoveOrSelectAttribute("x", false, "", 0), "x", "y", null));
        Check("made.selectIf", () => Describe(new SelectIfAttribute(Quality.Low, false, "a\\b.cs", 3, "S"), Quality.Low, Quality.High, 0));
        Check("made.selectIfNot", () => Describe(new SelectIfNotAttribute(false, true, null, -1), false, true, "False"));
        Check("made.selectOrRemove", () => Describe(new SelectOrRemoveAttribute(2.5f, false, "f.cs", 1, "A", "B"), 2.5f, 2.5, null));
        Check("made.base", () => Describe(new FilterAttribute(FilterAction.Remove, FilterAttribute.Precedence.Override, FilterAttribute.EvaluationMode.Negated, "c", "file", 9), "c", "d", null));
        Check("made.nullCondition", () =>
        {
            var attribute = new RemoveIfAttribute(null, false, "n.cs", 2, "K");
            return Describe(attribute, null, "x", null);
        });

        Check("made.tagsEqual", () => DescribeTags(new ApplyRulesIfTagsEqualAttribute("A", "1", "B", "2")));
        Check("made.tagsNotEqual", () => DescribeTags(new ApplyRulesIfTagsNotEqualAttribute("A", "1")));
        Check("made.tagsOdd", () => DescribeTags(new TagConstraintAttribute(false, "A", "1", "B")));
        Check("made.graphicsIf", () => DescribeGraphics(new ApplyRulesIfGraphicsAPIAttribute(GraphicsDeviceType.Metal)));
        Check("made.graphicsNone", () => DescribeGraphics(new GraphicsAPIConstraintAttribute(true)));
        Check("made.usage", () => string.Join("|", new[] { typeof(FilterAttribute), typeof(GraphicsAPIConstraintAttribute), typeof(TagConstraintAttribute) }
            .Select(type =>
            {
                var usage = type.GetCustomAttribute<AttributeUsageAttribute>();
                return type.Name + "=" + (usage == null ? "<none>" : usage.ValidOn + "/" + usage.AllowMultiple + "/" + usage.Inherited);
            })));

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
        EditorApplication.Exit(0);
    }

    static object Get(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(name, Any | BindingFlags.DeclaredOnly);
            if (property != null) return property.GetValue(target);
            var field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(target);
        }
        return "<missing>";
    }

    static object Call(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, Any).Invoke(target, args); }
        catch (TargetInvocationException e) { return "throws " + e.InnerException.GetType().Name; }
    }

    static string Text(object value) => value == null ? "<null>"
        : value is string s ? "\"" + s + "\""
        : value is Array a ? "[" + string.Join(",", a.Cast<object>().Select(Text)) + "]"
        : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

    static string Active(FilterAttribute attribute, object data)
    {
        var args = new object[] { data, null };
        try
        {
            var result = typeof(FilterAttribute).GetMethod("GetActiveFilterAction", Any).Invoke(attribute, args);
            return result + "/" + args[1];
        }
        catch (TargetInvocationException e) { return "throws " + e.InnerException.GetType().Name; }
    }

    static string Describe(FilterAttribute attribute, object match, object other, object third)
    {
        var ruleType = typeof(FilterAttribute).Assembly.GetType("UnityEditor.ShaderKeywordFilter.FilterRule");
        var rule = Activator.CreateInstance(ruleType, Any, null, new object[] { "OLD_KEY", true, FilterAction.Select, "why" }, null);
        return string.Join("|", new[]
        {
            "action=" + Text(Get(attribute, "Action")), "precedence=" + Text(Get(attribute, "RulePrecedence")),
            "mode=" + Text(Get(attribute, "m_EvaluationMode")), "names=" + Text(Get(attribute, "KeywordNames")),
            "condition=" + Text(Get(attribute, "m_Condition")), "file=" + Text(Get(attribute, "m_FileName")),
            "line=" + Text(Get(attribute, "m_LineNumber")), "actionName=" + Text(Call(attribute, "GetFilterActionName", Get(attribute, "Action"))),
            "onMatch=" + Active(attribute, match), "onOther=" + Active(attribute, other), "onThird=" + Active(attribute, third),
            "active=" + Text(Call(attribute, "GetFormattedResolutionMessageActive", match)),
            "inactive=" + Text(Call(attribute, "GetFormattedResolutionMessageInactive", other)),
            "override=" + Text(Call(attribute, "GetFormattedResolutionMessageOverride", match, rule)),
        });
    }

    static string DescribeTags(TagConstraintAttribute attribute) => string.Join("|", new[]
    {
        "negated=" + Text(Get(attribute, "Negated")), "tags=" + Text(Get(attribute, "Tags")),
        "match=" + Text(Call(attribute, "ShouldApplyRules", (object)new[] { "A", "1" })),
        "second=" + Text(Call(attribute, "ShouldApplyRules", (object)new[] { "X", "9", "B", "2" })),
        "other=" + Text(Call(attribute, "ShouldApplyRules", (object)new[] { "A", "2" })),
        "none=" + Text(Call(attribute, "ShouldApplyRules", (object)new string[0])),
        "odd=" + Text(Call(attribute, "ShouldApplyRules", (object)new[] { "A" })),
    });

    static string DescribeGraphics(GraphicsAPIConstraintAttribute attribute) => string.Join("|", new[]
    {
        "negated=" + Text(Get(attribute, "Negated")), "apis=" + Text(Get(attribute, "GraphicsAPIs")),
        "metal=" + Text(Call(attribute, "ShouldApplyRules", GraphicsDeviceType.Metal)),
        "vulkan=" + Text(Call(attribute, "ShouldApplyRules", GraphicsDeviceType.Vulkan)),
    });

    static void Check(string key, Func<string> read)
    {
        string value;
        try { value = read(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ":" + e.Message; }
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
