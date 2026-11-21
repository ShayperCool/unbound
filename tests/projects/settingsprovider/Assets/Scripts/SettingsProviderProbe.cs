using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Records, in Play mode, SettingsProvider construction, path normalization, labels, keywords,
/// search matching (HasSearchInterest), virtual callback delegation, keyword extraction helpers,
/// SettingsProviderAttribute / SettingsProviderGroupAttribute / PreferenceItem discovery,
/// duplicate filtering and warnings, and SettingsService.Exists / notification events.
/// </summary>
static class SettingsProviderProbe
{
    const string Prefix = "UnboundProbe/";
    static int s_PrefCalls;

    class ProbeStyles
    {
        public static GUIContent first = new GUIContent("First Label");
        public static GUIContent second { get; } = new GUIContent("Second Label");
        public static GUIContent duplicate = new GUIContent("first label");
        public static GUIContent emptyText = new GUIContent("");
        public static GUIContent nullText = new GUIContent((string)null);
        public static string notContent = "Ignored String";
        static GUIContent privateContent = new GUIContent("Private Ignored");
        public GUIContent instanceContent = new GUIContent("Instance Ignored");
    }

    class DerivedGUIContent : GUIContent
    {
        public DerivedGUIContent(string text) : base(text) { }
    }

    class DerivedStyles
    {
        public static DerivedGUIContent derived = new DerivedGUIContent("Derived Content");
    }

    class ProbeAsset : ScriptableObject
    {
        public int countValue = 3;
        public string titleText = "ok";
        [HideInInspector] public int hiddenValue = 9;
    }

    class CustomProvider : SettingsProvider
    {
        public readonly List<string> Events = new List<string>();
        public CustomProvider(string path, SettingsScope scope, IEnumerable<string> keywords = null) : base(path, scope, keywords) { }
        public override void OnActivate(string searchContext, VisualElement rootElement) { Events.Add("override.activate:" + searchContext); base.OnActivate(searchContext, rootElement); }
        public override void OnDeactivate() { Events.Add("override.deactivate"); base.OnDeactivate(); }
        public override void OnGUI(string searchContext) { Events.Add("override.gui:" + searchContext); base.OnGUI(searchContext); }
        public override void OnTitleBarGUI() { Events.Add("override.title"); base.OnTitleBarGUI(); }
        public override void OnFooterBarGUI() { Events.Add("override.footer"); base.OnFooterBarGUI(); }
        public override void OnInspectorUpdate() { Events.Add("override.update"); base.OnInspectorUpdate(); }
    }

    [SettingsProvider]
    public static SettingsProvider CreateSingle() =>
        new SettingsProvider("Project/" + Prefix + "Single", SettingsScope.Project, new[] { "single", "tool" });

    [SettingsProvider]
    static SettingsProvider CreatePrivateUser() =>
        new SettingsProvider("Preferences/" + Prefix + "PrivateUser", SettingsScope.User, new[] { "private" });

    [SettingsProvider]
    static SettingsProvider CreateNull() => null;

    [SettingsProvider]
    static SettingsProvider CreateThrowing() => throw new InvalidOperationException("provider-ctor-fail");

    [SettingsProvider]
    static SettingsProvider CreateBadSignature(int ignored) =>
        new SettingsProvider("Project/" + Prefix + "BadSig", SettingsScope.Project);

    [SettingsProviderGroup]
    static SettingsProvider[] CreateGroup() => new[]
    {
        new SettingsProvider("Project/" + Prefix + "GroupA", SettingsScope.Project, new[] { "groupA" }),
        null,
        new SettingsProvider("Preferences/" + Prefix + "GroupB", SettingsScope.User, new[] { "groupB" }),
    };

    [SettingsProviderGroup]
    static SettingsProvider[] CreateNullGroup() => null;

    [SettingsProviderGroup]
    static SettingsProvider[] CreateThrowingGroup() => throw new InvalidOperationException("group-fail");

    [SettingsProviderGroup]
    static IEnumerable<SettingsProvider> CreateEnumerableGroup() => new[]
    {
        new SettingsProvider("Project/" + Prefix + "EnumerableGroup", SettingsScope.Project),
    };

    [SettingsProvider]
    static SettingsProvider CreateDupFirst() =>
        new SettingsProvider("Project/" + Prefix + "Dup", SettingsScope.Project) { label = "DupFirst" };

    [SettingsProvider]
    static SettingsProvider CreateDupSecond() =>
        new SettingsProvider("Project/" + Prefix + "Dup", SettingsScope.Project) { label = "DupSecond" };

    [SettingsProvider]
    static SettingsProvider CreateCrossProject() =>
        new SettingsProvider("Project/" + Prefix + "CrossScope", SettingsScope.Project);

    [SettingsProvider]
    static SettingsProvider CreateCrossUser() =>
        new SettingsProvider("Project/" + Prefix + "CrossScope", SettingsScope.User);

#pragma warning disable CS0618
    [PreferenceItem(Prefix + "LegacyPref")]
    static void LegacyPrefGUI() => s_PrefCalls++;
#pragma warning restore CS0618

    // Declarations of the wrong shape, under paths the rows above do not list.
    class InstanceHolder
    {
        [SettingsProvider]
        SettingsProvider InstanceSettings() => new SettingsProvider("Project/OtherProbe/Instance", SettingsScope.Project);
    }

    [SettingsProvider]
    static SettingsProvider GenericSettings<T>() => new SettingsProvider("Project/OtherProbe/Generic", SettingsScope.Project);

#pragma warning disable CS0618
    [PreferenceItem("OtherProbeWrongPref")]
    static int WrongPrefItem() => 0;
#pragma warning restore CS0618

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("SETTINGS_PROVIDER_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
        void Check(string key, Func<string> read)
        {
            try { Put(key, read()); }
            catch (ArgumentNullException e) { Put(key, "throws ArgumentNullException:" + e.ParamName); }
            catch (NullReferenceException) { Put(key, "throws NullReferenceException"); }
            catch (Exception e) { Put(key, "throws " + e.GetType().Name + ": " + e.Message); }
        }

        // 1. SettingsProvider constructor, path normalization, label, scope, keywords.
        var basic = new SettingsProvider("Project/My Tool/General", SettingsScope.Project, new[] { "alpha", "Beta", "alpha" });
        Check("ctor.basic.path", () => basic.settingsPath);
        Check("ctor.basic.label", () => basic.label);
        Check("ctor.basic.scope", () => basic.scope.ToString());
        Check("ctor.basic.keywords", () => JoinSorted(basic.keywords));

        var norm = new SettingsProvider("Project\\Sub\nName\\Leaf", SettingsScope.User);
        Check("ctor.norm.path", () => norm.settingsPath);
        Check("ctor.norm.label", () => norm.label);
        Check("ctor.norm.scope", () => norm.scope.ToString());
        Check("ctor.norm.keywords", () => JoinSorted(norm.keywords));

        var singleToken = new SettingsProvider("SingleToken", SettingsScope.Project);
        Check("ctor.single.path", () => singleToken.settingsPath);
        Check("ctor.single.label", () => singleToken.label);

        var trailing = new SettingsProvider("Project/Trail/", SettingsScope.Project);
        Check("ctor.trailing.path", () => trailing.settingsPath);
        Check("ctor.trailing.label", () => trailing.label);

        Check("ctor.nullPath", () => new SettingsProvider(null, SettingsScope.Project).settingsPath);

        // Label setter and fallback to last path token.
        basic.label = "Custom Label";
        Check("label.custom", () => basic.label);
        basic.label = "";
        Check("label.emptyFallback", () => basic.label);
        basic.label = null;
        Check("label.nullFallback", () => basic.label);

        // Keywords setter.
        basic.keywords = new[] { "x", "y", "x" };
        Check("keywords.set", () => JoinSorted(basic.keywords));
        Check("keywords.setNull", () => { basic.keywords = null; return "ok"; });

        // 2. Virtual methods and handler delegates.
        var defaultProvider = new SettingsProvider("Project/DefaultHandlers", SettingsScope.Project);
        Check("handlers.empty", () =>
        {
            defaultProvider.OnActivate("s", null);
            defaultProvider.OnDeactivate();
            defaultProvider.OnGUI("s");
            defaultProvider.OnTitleBarGUI();
            defaultProvider.OnFooterBarGUI();
            defaultProvider.OnInspectorUpdate();
            defaultProvider.Repaint();
            return "ok";
        });

        var calls = new List<string>();
        var custom = new CustomProvider("Project/Custom", SettingsScope.Project)
        {
            activateHandler = (ctx, root) => calls.Add($"act:{ctx}:{(root == null ? "null" : "el")}"),
            deactivateHandler = () => calls.Add("deact"),
            guiHandler = ctx => calls.Add("gui:" + ctx),
            titleBarGuiHandler = () => calls.Add("title"),
            footerBarGuiHandler = () => calls.Add("footer"),
            inspectorUpdateHandler = () => calls.Add("update"),
        };
        var ve = new VisualElement();
        custom.OnActivate("q1", ve);
        custom.OnDeactivate();
        custom.OnGUI("q2");
        custom.OnTitleBarGUI();
        custom.OnFooterBarGUI();
        custom.OnInspectorUpdate();
        Check("handlers.delegates", () => string.Join("|", calls));
        Check("handlers.overrides", () => string.Join("|", custom.Events));

        // 3. HasSearchInterest and search matching rules.
        var search = new SettingsProvider("Project/Search", SettingsScope.Project, new[] { "Audio Volume", "Master Gain", "FPS", "abc", "", null });
        Check("search.exactLower", () => search.HasSearchInterest("audio volume").ToString());
        Check("search.exactUpper", () => search.HasSearchInterest("FPS").ToString());
        Check("search.exactCaseFold", () => search.HasSearchInterest("fps").ToString());
        Check("search.substr", () => search.HasSearchInterest("vol").ToString());
        Check("search.missing", () => search.HasSearchInterest("missing").ToString());
        Check("search.multiOrdered", () => search.HasSearchInterest("aud vol").ToString());
        Check("search.multiPartial", () => search.HasSearchInterest("au lume").ToString());
        Check("search.multiReversed", () => search.HasSearchInterest("vol aud").ToString());
        Check("search.multiExtraSpaces", () => search.HasSearchInterest("  aud   vol  ").ToString());
        Check("search.spacesOnly", () => search.HasSearchInterest("   ").ToString());
        Check("search.empty", () => search.HasSearchInterest("").ToString());
        Check("search.null", () => search.HasSearchInterest(null).ToString());
        Check("search.overlapSameWord", () => search.HasSearchInterest("audio audio").ToString());
        Check("search.overlapBoundaryChar", () => search.HasSearchInterest("ab bc").ToString());

        search.hasSearchInterestHandler = ctx => ctx == "special";
        Check("search.handlerTrue", () => search.HasSearchInterest("special").ToString());
        Check("search.handlerFalse", () => search.HasSearchInterest("audio volume").ToString());

        // 4. Keyword extraction helpers.
        Check("keywords.fromGUIContent", () => JoinSorted(SettingsProvider.GetSearchKeywordsFromGUIContentProperties<ProbeStyles>()));
        Check("keywords.fromGUIContentDerived", () => JoinSorted(SettingsProvider.GetSearchKeywordsFromGUIContentProperties<DerivedStyles>()));
        var pop = new SettingsProvider("Project/Pop", SettingsScope.Project);
        pop.PopulateSearchKeywordsFromGUIContentProperties<ProbeStyles>();
        Check("keywords.populate", () => JoinSorted(pop.keywords));

        var asset = ScriptableObject.CreateInstance<ProbeAsset>();
        try
        {
            var so = new SerializedObject(asset);
            Check("keywords.fromSerializedObject", () => JoinSorted(SettingsProvider.GetSearchKeywordsFromSerializedObject(so)));
        }
        finally { UnityEngine.Object.DestroyImmediate(asset); }
        Check("keywords.fromMissingPath", () => JoinSorted(SettingsProvider.GetSearchKeywordsFromPath("Assets/DoesNotExist_UnboundProbe.asset")));

        // 5. SettingsService discovery, duplicate filtering, Exists, and events.
        var logs = new List<string>();
        var wrongShapeLogs = new List<string>();
        Application.LogCallback onLog = (msg, stack, type) =>
        {
            if (msg != null && (msg.Contains("UnboundProbe") || msg.Contains("CreateThrowing") || msg.Contains("CreateBadSignature") || msg.Contains("CreateEnumerableGroup")))
                logs.Add(type + ":" + msg);
            if (msg != null && (msg.Contains("InstanceSettings") || msg.Contains("GenericSettings") || msg.Contains("WrongPrefItem")))
                wrongShapeLogs.Add(type + ":" + msg);
        };
        Application.logMessageReceived += onLog;
        try
        {
            Check("exists.single", () => SettingsService.Exists("Project/" + Prefix + "Single").ToString());
            Check("exists.singleWrongCase", () => SettingsService.Exists("project/" + Prefix + "single").ToString());
            Check("exists.privateUser", () => SettingsService.Exists("Preferences/" + Prefix + "PrivateUser").ToString());
            Check("exists.groupA", () => SettingsService.Exists("Project/" + Prefix + "GroupA").ToString());
            Check("exists.groupB", () => SettingsService.Exists("Preferences/" + Prefix + "GroupB").ToString());
            Check("exists.enumerableGroup", () => SettingsService.Exists("Project/" + Prefix + "EnumerableGroup").ToString());
            Check("exists.legacyPref", () => SettingsService.Exists("Preferences/" + Prefix + "LegacyPref").ToString());
            Check("exists.badSig", () => SettingsService.Exists("Project/" + Prefix + "BadSig").ToString());
            Check("exists.missing", () => SettingsService.Exists("Project/" + Prefix + "Missing").ToString());
            Check("exists.null", () => SettingsService.Exists(null).ToString());

            var fetchAll = typeof(SettingsService).GetMethod("FetchSettingsProviders", BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            var fetchScope = typeof(SettingsService).GetMethod("FetchSettingsProviders", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(SettingsScope) }, null);
            var allProviders = (SettingsProvider[])fetchAll.Invoke(null, null);
            var probeAll = allProviders.Where(p => p.settingsPath.Contains(Prefix)).OrderBy(p => p.scope).ThenBy(p => p.settingsPath, StringComparer.Ordinal).ToArray();
            Check("fetch.all", () => string.Join("|", probeAll.Select(p =>
            {
                string lbl = p.settingsPath.EndsWith("/Dup", StringComparison.Ordinal)
                    ? (p.label == "DupFirst" || p.label == "DupSecond" ? "<dup-valid>" : p.label)
                    : p.label;
                return $"{p.scope}:{p.settingsPath}:{lbl}";
            })));

            var projProviders = (SettingsProvider[])fetchScope.Invoke(null, new object[] { SettingsScope.Project });
            var probeProj = projProviders.Where(p => p.settingsPath.Contains(Prefix)).OrderBy(p => p.settingsPath, StringComparer.Ordinal).ToArray();
            Check("fetch.project", () => string.Join("|", probeProj.Select(p => $"{p.scope}:{p.settingsPath}")));

            var userProviders = (SettingsProvider[])fetchScope.Invoke(null, new object[] { SettingsScope.User });
            var probeUser = userProviders.Where(p => p.settingsPath.Contains(Prefix)).OrderBy(p => p.settingsPath, StringComparer.Ordinal).ToArray();
            Check("fetch.user", () => string.Join("|", probeUser.Select(p => $"{p.scope}:{p.settingsPath}")));

            // Legacy PreferenceItem wrapper executes callback via OnGUI.
            var legacy = probeUser.FirstOrDefault(p => p.settingsPath == "Preferences/" + Prefix + "LegacyPref");
            s_PrefCalls = 0;
            legacy?.OnGUI("");
            Check("legacyPref.onGUI", () => s_PrefCalls.ToString());

            // Verify duplicate warning text and provider error logs (deduplicated & sorted for determinism).
            var distinctLogs = logs.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            Check("fetch.logs", () => string.Join("|", distinctLogs));
            // What the declarations of the wrong shape log on the first lookups, each message counted.
            Check("fetch.wrongShapeLogs", () => string.Join("|", wrongShapeLogs.GroupBy(s => s, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Count() + "x " + g.Key)));
        }
        finally
        {
            Application.logMessageReceived -= onLog;
        }

        // How often each discovery message is logged over two more lookups, and what the
        // declarations of the wrong shape log.
        var repeated = new List<string>();
        Application.LogCallback onRepeat = (msg, stack, type) =>
        {
            if (msg != null && (msg.Contains("UnboundProbe") || msg.Contains("OtherProbe") || msg.Contains("CreateThrowing") || msg.Contains("CreateBadSignature")
                || msg.Contains("CreateEnumerableGroup") || msg.Contains("InstanceSettings") || msg.Contains("GenericSettings") || msg.Contains("WrongPrefItem")))
                repeated.Add(type + ":" + msg);
        };
        Application.logMessageReceived += onRepeat;
        try
        {
            SettingsService.Exists("Project/" + Prefix + "Single");
            SettingsService.Exists("Project/" + Prefix + "Single");
            Check("fetch.repeatedLogs", () => string.Join("|", repeated.GroupBy(s => s, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Count() + "x " + g.Key)));
            Check("exists.wrongShapes", () => string.Join(",", new[] { "Project/OtherProbe/Instance", "Project/OtherProbe/Generic", "Preferences/OtherProbeWrongPref" }
                .Select(path => path + "=" + SettingsService.Exists(path))));
        }
        finally
        {
            Application.logMessageReceived -= onRepeat;
        }

        // SettingsService notification methods and internal events.
        int changedCount = 0, repaintCount = 0;
        var changedEvt = typeof(SettingsService).GetEvent("settingsProviderChanged", BindingFlags.Static | BindingFlags.NonPublic);
        var repaintEvt = typeof(SettingsService).GetEvent("repaintAllSettingsWindow", BindingFlags.Static | BindingFlags.NonPublic);
        Action onChanged = () => changedCount++;
        Action onRepaint = () => repaintCount++;
        Check("notify.noListeners", () =>
        {
            SettingsService.NotifySettingsProviderChanged();
            SettingsService.RepaintAllSettingsWindow();
            return "ok";
        });
        changedEvt.AddMethod.Invoke(null, new object[] { onChanged });
        repaintEvt.AddMethod.Invoke(null, new object[] { onRepaint });
        try
        {
            SettingsService.NotifySettingsProviderChanged();
            SettingsService.RepaintAllSettingsWindow();
            SettingsService.RepaintAllSettingsWindow();
        }
        finally
        {
            changedEvt.RemoveMethod.Invoke(null, new object[] { onChanged });
            repaintEvt.RemoveMethod.Invoke(null, new object[] { onRepaint });
        }
        Check("notify.counts", () => $"{changedCount}/{repaintCount}");

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string JoinSorted(IEnumerable<string> items) =>
        items == null ? "<null>" : string.Join("|", items.Select(s => s ?? "<null>").OrderBy(s => s, StringComparer.Ordinal));

    static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s ?? "<null>")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
