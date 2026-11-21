using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// SettingsProvider, SearchUtils and SettingsService discovery/filtering/notification contracts,
    /// as measured on the Unity 6000.0.71f1 Editor (tests/projects/settingsprovider).
    /// </summary>
    public sealed class SettingsProviderTests
    {
        const string TestPrefix = "SettingsProviderUnitTest/";
        static int s_PrefCalls;

        sealed class SampleStyles
        {
            public static GUIContent alpha = new GUIContent("Alpha Option");
            public static GUIContent beta { get; } = new GUIContent("Beta Setting");
            public static GUIContent duplicate = new GUIContent("alpha option");
            public static GUIContent empty = new GUIContent("");
            public static GUIContent nullText = new GUIContent((string)null);
            public static string plainString = "Not Content";
            static GUIContent privateContent = new GUIContent("Hidden");
            public GUIContent instanceContent = new GUIContent("Instance");
        }

        sealed class SampleSettingsAsset : ScriptableObject
        {
            public int itemCount = 4;
            public string displayTitle = "title";
            [HideInInspector] public int secretValue = 99;
        }

        sealed class OverriddenSearchProvider : SettingsProvider
        {
            public OverriddenSearchProvider(string path) : base(path, SettingsScope.Project, new[] { "keyword" }) { }
            public override bool HasSearchInterest(string searchContext) => searchContext == "override-only";
        }

        [SettingsProvider]
        public static SettingsProvider UnitSingle() =>
            new SettingsProvider("Project/" + TestPrefix + "Single", SettingsScope.Project, new[] { "unit" });

        [SettingsProvider]
        static SettingsProvider UnitPrivateUser() =>
            new SettingsProvider("Preferences/" + TestPrefix + "Private", SettingsScope.User);

        [SettingsProvider]
        static SettingsProvider UnitNull() => null;

        [SettingsProvider]
        static SettingsProvider UnitThrowing() => throw new InvalidOperationException("unit-throw");

        [SettingsProvider]
        static SettingsProvider UnitBadSignature(int extra) =>
            new SettingsProvider("Project/" + TestPrefix + "BadSig", SettingsScope.Project);

        [SettingsProviderGroup]
        static SettingsProvider[] UnitGroup() => new[]
        {
            new SettingsProvider("Project/" + TestPrefix + "GroupProj", SettingsScope.Project),
            null,
            new SettingsProvider("Preferences/" + TestPrefix + "GroupUser", SettingsScope.User),
        };

        [SettingsProviderGroup]
        static SettingsProvider[] UnitNullGroup() => null;

        [SettingsProviderGroup]
        static SettingsProvider[] UnitThrowingGroup() => throw new InvalidOperationException("unit-group-throw");

        [SettingsProviderGroup]
        static IEnumerable<SettingsProvider> UnitBadGroup() => Array.Empty<SettingsProvider>();

        [SettingsProvider]
        static SettingsProvider UnitDup1() =>
            new SettingsProvider("Project/" + TestPrefix + "Dup", SettingsScope.Project);

        [SettingsProvider]
        static SettingsProvider UnitDup2() =>
            new SettingsProvider("Project/" + TestPrefix + "Dup", SettingsScope.Project);

        [SettingsProvider]
        static SettingsProvider UnitCrossProj() =>
            new SettingsProvider("Project/" + TestPrefix + "Cross", SettingsScope.Project);

        [SettingsProvider]
        static SettingsProvider UnitCrossUser() =>
            new SettingsProvider("Project/" + TestPrefix + "Cross", SettingsScope.User);

#pragma warning disable CS0618
        [PreferenceItem(TestPrefix + "Legacy")]
        static void UnitLegacyPref() => s_PrefCalls++;

        [PreferenceItem(TestPrefix + "WrongPref")]
        static int UnitWrongPref() => 0;
#pragma warning restore CS0618

        [SettingsProvider]
        static SettingsProvider UnitGeneric<T>() => new SettingsProvider("Project/" + TestPrefix + "Generic", SettingsScope.Project);

        sealed class InstanceHolder
        {
            [SettingsProvider]
            SettingsProvider UnitInstance() => new SettingsProvider("Project/" + TestPrefix + "Instance", SettingsScope.Project);
        }

        [Fact]
        public void ConstructorNormalizesPathLabelTokensAndKeywords()
        {
            var provider = new SettingsProvider("Project\\Sub\nGroup\\Leaf", SettingsScope.Project, new[] { "alpha", "Beta", "alpha" });
            Assert.Equal("Project/Sub Group/Leaf", provider.settingsPath);
            Assert.Equal("Leaf", provider.label);
            Assert.Equal(SettingsScope.Project, provider.scope);
            Assert.Equal(new[] { "Project", "Sub Group", "Leaf" }, provider.pathTokens);
            Assert.Equal(new[] { "Beta", "alpha" }, provider.keywords.OrderBy(k => k, StringComparer.Ordinal).ToArray());

            var single = new SettingsProvider("Standalone", SettingsScope.User);
            Assert.Equal("Standalone", single.settingsPath);
            Assert.Equal("Standalone", single.label);
            Assert.Equal(SettingsScope.User, single.scope);
            Assert.Empty(single.keywords);

            var trailing = new SettingsProvider("Project/Trailing/", SettingsScope.Project);
            Assert.Equal("Project/Trailing/", trailing.settingsPath);
            Assert.Equal(string.Empty, trailing.label);

            Assert.Throws<NullReferenceException>(() => new SettingsProvider(null, SettingsScope.Project));

            provider.label = "Custom";
            Assert.Equal("Custom", provider.label);
            provider.label = "";
            Assert.Equal("Leaf", provider.label);
            provider.label = null;
            Assert.Equal("Leaf", provider.label);

            provider.keywords = new[] { "one", "two", "one" };
            Assert.Equal(new[] { "one", "two" }, provider.keywords.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            Assert.Throws<ArgumentNullException>(() => provider.keywords = null);
        }

        [Fact]
        public void LifecycleAndGuiCallbacksDelegateAndGuardActivation()
        {
            var calls = new List<string>();
            var provider = new SettingsProvider("Project/Callbacks", SettingsScope.Project)
            {
                activateHandler = (ctx, root) => calls.Add($"act:{ctx}:{(root != null ? "ve" : "null")}"),
                deactivateHandler = () => calls.Add("deact"),
                guiHandler = ctx => calls.Add("gui:" + ctx),
                titleBarGuiHandler = () => calls.Add("title"),
                footerBarGuiHandler = () => calls.Add("footer"),
                inspectorUpdateHandler = () => calls.Add("update"),
            };

            var root = new VisualElement();
            provider.Activate("first", root);
            provider.Activate("second", root); // already activated: must not invoke OnActivate again
            provider.OnGUI("ctx");
            provider.OnTitleBarGUI();
            provider.OnFooterBarGUI();
            provider.OnInspectorUpdate();
            provider.Repaint();
            provider.FocusLost();
            provider.Deactivate();
            provider.Deactivate(); // already deactivated: must not invoke OnDeactivate again

            Assert.Equal(new[] { "act:first:ve", "gui:ctx", "title", "footer", "update", "deact" }, calls);
        }

        [Fact]
        public void HasSearchInterestAndSearchUtilsMatchRules()
        {
            var provider = new SettingsProvider("Project/Search", SettingsScope.Project, new[] { "Audio Volume", "abc", "", null });
            Assert.True(provider.HasSearchInterest("audio volume"));
            Assert.True(provider.HasSearchInterest("vol"));
            Assert.True(provider.HasSearchInterest("aud vol"));
            Assert.True(provider.HasSearchInterest("  aud   vol  "));
            Assert.True(provider.HasSearchInterest("ab bc"));
            Assert.False(provider.HasSearchInterest("vol aud"));
            Assert.False(provider.HasSearchInterest("audio audio"));
            Assert.False(provider.HasSearchInterest("   "));
            Assert.False(provider.HasSearchInterest(""));
            Assert.False(provider.HasSearchInterest(null));

            Assert.True(SearchUtils.MatchSearchGroups("aud vol", "Audio Volume", out int start, out int end));
            Assert.Equal(0, start);
            Assert.Equal(8, end);
            Assert.True(SearchUtils.MatchSearch("VOL", "Audio Volume"));
            Assert.False(SearchUtils.MatchSearch(null, "Audio Volume"));

            provider.hasSearchInterestHandler = q => q == "override";
            Assert.True(provider.HasSearchInterest("override"));
            Assert.False(provider.HasSearchInterest("audio volume"));

            // Control outside the probe setup: subclass override replaces base keyword search.
            var overridden = new OverriddenSearchProvider("Project/Sub");
            Assert.True(overridden.HasSearchInterest("override-only"));
            Assert.False(overridden.HasSearchInterest("keyword"));
        }

        [Fact]
        public void KeywordExtractionHelpersMatchUnityRules()
        {
            var fromStyles = SettingsProvider.GetSearchKeywordsFromGUIContentProperties<SampleStyles>()
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new[] { "", "alpha option", "beta setting" }, fromStyles);

            var provider = new SettingsProvider("Project/Pop", SettingsScope.Project);
            provider.PopulateSearchKeywordsFromGUIContentProperties<SampleStyles>();
            Assert.Equal(fromStyles, provider.keywords.OrderBy(s => s, StringComparer.Ordinal).ToArray());

            var asset = ScriptableObject.CreateInstance<SampleSettingsAsset>();
            try
            {
                var fromSo = SettingsProvider.GetSearchKeywordsFromSerializedObject(new SerializedObject(asset))
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToArray();
                Assert.Contains("Item Count", fromSo);
                Assert.Contains("Display Title", fromSo);
                Assert.DoesNotContain("Secret Value", fromSo);
            }
            finally { Object.DestroyImmediate(asset); }

            AssetDatabase.List("[]");
            try
            {
                Assert.Empty(SettingsProvider.GetSearchKeywordsFromPath("Assets/NonExistent_SettingsProviderTests.asset"));
            }
            finally { AssetDatabase.Invalidate(); }
        }

        [Fact]
        public void SettingsServiceDiscoversFiltersAndNotifies()
        {
            var assemblies = (List<Assembly>)typeof(GameDomain).GetField("s_Assemblies", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var generation = typeof(GameDomain).GetField("s_Generation", BindingFlags.Static | BindingFlags.NonPublic);
            var original = assemblies.ToArray();
            int oldGeneration = (int)generation.GetValue(null);
            var logs = new List<(LogType Type, string Message)>();
            var oldHandler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new CaptureHandler(oldHandler, logs);
            try
            {
                assemblies.Clear();
                assemblies.Add(typeof(SettingsProviderTests).Assembly);
                generation.SetValue(null, oldGeneration + 1);

                Assert.True(SettingsService.Exists("Project/" + TestPrefix + "Single"));
                Assert.False(SettingsService.Exists("project/" + TestPrefix + "single"));
                Assert.True(SettingsService.Exists("Preferences/" + TestPrefix + "Private"));
                Assert.True(SettingsService.Exists("Project/" + TestPrefix + "GroupProj"));
                Assert.True(SettingsService.Exists("Preferences/" + TestPrefix + "GroupUser"));
                Assert.True(SettingsService.Exists("Preferences/" + TestPrefix + "Legacy"));
                Assert.False(SettingsService.Exists("Project/" + TestPrefix + "BadSig"));
                Assert.False(SettingsService.Exists("Project/" + TestPrefix + "Missing"));
                Assert.False(SettingsService.Exists("Project/" + TestPrefix + "Generic"));
                Assert.False(SettingsService.Exists("Project/" + TestPrefix + "Instance"));
                Assert.False(SettingsService.Exists("Preferences/" + TestPrefix + "WrongPref"));
                Assert.Throws<NullReferenceException>(() => SettingsService.Exists(null));

                // Methods of the wrong shape are reported once, on the first lookup; an instance
                // method is passed over; a throwing factory fails on every lookup.
                int lookups = 12;
                string Message(LogType type, string text) => type + ":" + text;
                var counts = logs.GroupBy(l => Message(l.Type, l.Message)).ToDictionary(g => g.Key, g => g.Count());
                Assert.Equal(1, counts[Message(LogType.Error, "static UnityEditor.SettingsProvider UnitBadSignature(Int32) does not match UnityEditor.SettingsProviderAttribute expected signature.\n Use static UnityEditor.SettingsProvider signature()")]);
                Assert.Equal(1, counts[Message(LogType.Error, "static System.Collections.Generic.IEnumerable`1[UnityEditor.SettingsProvider] UnitBadGroup() does not match UnityEditor.SettingsProviderGroupAttribute expected signature.\n Use static UnityEditor.SettingsProvider[] signature()")]);
                Assert.Equal(1, counts[Message(LogType.Error, "static Int32 UnitWrongPref() does not match UnityEditor.PreferenceItem expected signature.\n Use static Void signature()")]);
                Assert.Equal(1, counts[Message(LogType.Error, "static UnityEditor.SettingsProvider UnitGeneric[T]() is a generic method. UnityEditor.SettingsProviderAttribute cannot be applied to it.")]);
                Assert.DoesNotContain(logs, l => l.Message.Contains("UnitInstance"));
                Assert.Equal(lookups, counts[Message(LogType.Error, "Cannot create Settings Provider for: UnitThrowing")]);

                logs.Clear();
                var all = SettingsService.FetchSettingsProviders();
                Assert.Single(all, p => p.settingsPath == "Project/" + TestPrefix + "Dup");
                Assert.Equal(2, all.Count(p => p.settingsPath == "Project/" + TestPrefix + "Cross"));
                Assert.Contains(logs, l => l.Type == LogType.Warning && l.Message == $"There are 2 settings providers with the same name Project/{TestPrefix}Dup in Project scope.");
                Assert.Contains(logs, l => l.Type == LogType.Error && l.Message == "Cannot create Settings Provider for: UnitThrowing");
                Assert.Contains(logs, l => l.Type == LogType.Error && l.Message == "Cannot create Settings Providers for: UnitThrowingGroup");
                Assert.DoesNotContain(logs, l => l.Message.Contains("expected signature") || l.Message.Contains("generic method"));

                var userOnly = SettingsService.FetchSettingsProviders(SettingsScope.User);
                Assert.All(userOnly, p => Assert.Equal(SettingsScope.User, p.scope));
                var legacy = Assert.Single(userOnly, p => p.settingsPath == "Preferences/" + TestPrefix + "Legacy");
                s_PrefCalls = 0;
                legacy.OnGUI("");
                Assert.Equal(1, s_PrefCalls);

                // Control outside probe setup: 3 duplicates in FilterAndWarnAgainstDuplicates.
                logs.Clear();
                var triple = new[]
                {
                    new SettingsProvider("Project/Triple", SettingsScope.Project),
                    new SettingsProvider("Project/Triple", SettingsScope.Project),
                    new SettingsProvider("Project/Triple", SettingsScope.Project),
                };
                var filtered = SettingsService.FilterAndWarnAgainstDuplicates(triple, SettingsScope.Project).ToArray();
                Assert.Single(filtered);
                Assert.Contains(logs, l => l.Type == LogType.Warning && l.Message == "There are 3 settings providers with the same name Project/Triple in Project scope.");

                int changed = 0, repainted = 0;
                Action onChanged = () => changed++;
                Action onRepaint = () => repainted++;
                SettingsService.settingsProviderChanged += onChanged;
                SettingsService.repaintAllSettingsWindow += onRepaint;
                try
                {
                    SettingsService.NotifySettingsProviderChanged();
                    SettingsService.RepaintAllSettingsWindow();
                    SettingsService.RepaintAllSettingsWindow();
                }
                finally
                {
                    SettingsService.settingsProviderChanged -= onChanged;
                    SettingsService.repaintAllSettingsWindow -= onRepaint;
                }
                Assert.Equal(1, changed);
                Assert.Equal(2, repainted);
            }
            finally
            {
                Debug.unityLogger.logHandler = oldHandler;
                assemblies.Clear();
                assemblies.AddRange(original);
                generation.SetValue(null, oldGeneration);
            }
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<(LogType, string)> m_Logs;
            public CaptureHandler(ILogHandler inner, List<(LogType, string)> logs) { m_Inner = inner; m_Logs = logs; }
            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                m_Logs.Add((logType, args != null && args.Length > 0 ? string.Format(format, args) : format));
            }
            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }
    }
}
