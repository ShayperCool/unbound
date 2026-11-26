using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>What the Editor's interface language settings answer in Play mode; see README.md.</summary>
public class EditorLocalizationProbe : MonoBehaviour
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    readonly List<string> m_Lines = new List<string>();

    void Add(string key, object value)
    {
        string text = value == null ? "null" : value is Array a ? string.Join(",", a.Cast<object>()) : Convert.ToString(value, CultureInfo.InvariantCulture);
        m_Lines.Add("{\"k\": \"" + key + "\", \"v\": \"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
    }

    void Try(string key, Func<object> read)
    {
        try { Add(key, read()); }
        catch (Exception e) { Add(key, "throws " + (e is TargetInvocationException t && t.InnerException != null ? t.InnerException.GetType().Name : e.GetType().Name)); }
    }

    void Start()
    {
#if UNITY_EDITOR
        var editor = typeof(UnityEditor.Editor).Assembly;
        var db = editor.GetType("UnityEditor.LocalizationDatabase");
        object Call(string name, params object[] args) =>
            db.GetMethods(Any).First(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(null, args);
        object Get(string name) => db.GetProperty(name, Any).GetValue(null);
        Add("batchMode", Application.isBatchMode);
        Try("currentEditorLanguage", () => Get("currentEditorLanguage"));
        Try("enableEditorLocalization", () => Get("enableEditorLocalization"));
        Try("GetDefaultEditorLanguage", () => Call("GetDefaultEditorLanguage"));
        Try("GetAvailableEditorLanguages", () => Call("GetAvailableEditorLanguages"));
        Try("GetLocalizationResourceFolder", () =>
        {
            var folder = (string)Call("GetLocalizationResourceFolder");
            var contents = UnityEditor.EditorApplication.applicationContentsPath;
            return folder != null && folder.StartsWith(contents, StringComparison.Ordinal) ? "<contents>" + folder.Substring(contents.Length) : folder;
        });
        foreach (var lang in ((SystemLanguage[])Enum.GetValues(typeof(SystemLanguage))).Distinct())
            Try("GetCulture." + lang, () => Call("GetCulture", lang));
        Try("GetLocalizedString.Play", () => Call("GetLocalizedString", "Play"));
        Try("GetLocalizedString.unknown", () => Call("GetLocalizedString", "Unbound probe text"));
        Try("GetLocalizedString.empty", () => Call("GetLocalizedString", ""));
        Try("GetLocalizedStringInLang.Japanese.Play", () => Call("GetLocalizedStringInLang", SystemLanguage.Japanese, "Play"));
        Try("GetLocalizedStringInLang.English.Play", () => Call("GetLocalizedStringInLang", SystemLanguage.English, "Play"));
        Try("GetLocalizedStringWithGroupName.Play", () => Call("GetLocalizedStringWithGroupName", "Play", "Unbound.Group"));
        Try("GetContextGroupName", () => Call("GetContextGroupName"));
        Try("noLocalizationGroupName", () => Get("noLocalizationGroupName"));
        Try("MarkForTranslation", () => Call("MarkForTranslation", "Unbound text"));
        var l10n = editor.GetType("UnityEditor.L10n");
        Try("L10n.Tr.Play", () => l10n.GetMethods(Any).First(m => m.Name == "Tr" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)).Invoke(null, new object[] { "Play" }));
#endif
        Add("complete", "true");
        m_Lines[m_Lines.Count - 1] = "{\"k\": \"complete\", \"v\": true}";
        string dir = Environment.GetEnvironmentVariable("EDITOR_LOCALIZATION_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "values.jsonl"), m_Lines);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
