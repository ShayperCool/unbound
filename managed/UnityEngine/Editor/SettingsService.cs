using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// Discovers SettingsProvider instances registered via [SettingsProvider],
    /// [SettingsProviderGroup] and [PreferenceItem] in loaded assemblies, checks path existence,
    /// and publishes change/repaint notifications. Built-in Editor settings pages and opening the
    /// SettingsWindow UI are not implemented in Unbound (OpenProjectSettings / OpenUserPreferences
    /// remain stubs). Matches the Unity 6000.0.71f1 Editor (tests/projects/settingsprovider).
    /// </summary>
    [InitializeOnLoad]
    public static partial class SettingsService
    {
        internal static event Action repaintAllSettingsWindow;
        internal static event Action settingsProviderChanged;

        public static void NotifySettingsProviderChanged() => settingsProviderChanged?.Invoke();

        public static void RepaintAllSettingsWindow() => repaintAllSettingsWindow?.Invoke();

        internal static IEnumerable<SettingsProvider> FilterAndWarnAgainstDuplicates(
            IEnumerable<SettingsProvider> settingsProviders,
            SettingsScope scope)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var provider in settingsProviders)
            {
                if (provider.scope != scope)
                {
                    yield return provider;
                    continue;
                }

                if (counts.TryGetValue(provider.settingsPath, out int existing))
                {
                    counts[provider.settingsPath] = existing + 1;
                }
                else
                {
                    counts.Add(provider.settingsPath, 1);
                    yield return provider;
                }
            }

            foreach (var pair in counts)
            {
                if (pair.Value > 1)
                    Debug.LogWarning($"There are {pair.Value} settings providers with the same name {pair.Key} in {scope} scope.");
            }
        }

        internal static SettingsProvider[] FetchSettingsProviders()
        {
            var providers = FetchSettingProviderFromAttribute()
                .Concat(FetchSettingProvidersFromAttribute())
                .Concat(FetchPreferenceItems())
                .Where(p => p != null);
            providers = FilterAndWarnAgainstDuplicates(providers, SettingsScope.Project);
            return FilterAndWarnAgainstDuplicates(providers, SettingsScope.User).ToArray();
        }

        internal static SettingsProvider[] FetchSettingsProviders(SettingsScope scope)
        {
            var providers = FetchSettingProviderFromAttribute()
                .Concat(FetchSettingProvidersFromAttribute())
                .Concat(FetchPreferenceItems())
                .Where(p => p != null && p.scope == scope);
            return FilterAndWarnAgainstDuplicates(providers, scope).ToArray();
        }

        public static bool Exists(string settingsPath)
        {
            var providers = FetchSettingsProviders();
            if (settingsPath == null)
                throw new NullReferenceException();
            foreach (var provider in providers)
            {
                if (settingsPath.Equals(provider.settingsPath, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>The methods of each attribute that have its shape, found once per game domain:
        /// a static method of another shape is reported then, once (an instance method is passed
        /// over silently), while factories run, and report their failures, on every lookup
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/settingsprovider fetch.logs,
        /// fetch.repeatedLogs and fetch.wrongShapeLogs).</summary>
        static int s_DiscoveredGeneration = -1;
        static MethodInfo[] s_ProviderMethods, s_GroupMethods, s_PreferenceMethods;

        static void Discover()
        {
            if (s_DiscoveredGeneration == GameDomain.Generation) return;
            s_ProviderMethods = MethodsOfShape<SettingsProviderAttribute>(typeof(SettingsProvider));
            s_GroupMethods = MethodsOfShape<SettingsProviderGroupAttribute>(typeof(SettingsProvider[]));
            s_PreferenceMethods = MethodsOfShape<PreferenceItem>(typeof(void));
            s_DiscoveredGeneration = GameDomain.Generation;
        }

        static MethodInfo[] MethodsOfShape<TAttribute>(Type returnType) where TAttribute : Attribute
        {
            var found = new List<MethodInfo>();
            foreach (var method in TypeCache.GetMethodsWithAttribute<TAttribute>())
            {
                if (!method.IsStatic) continue;
                if (method.ContainsGenericParameters)
                    Debug.LogError($"static {method} is a generic method. {typeof(TAttribute)} cannot be applied to it.");
                else if (method.ReturnType != returnType || method.GetParameters().Length != 0)
                    Debug.LogError($"static {method} does not match {typeof(TAttribute)} expected signature.\n Use static {TypeName(returnType)} signature()");
                else
                    found.Add(method);
            }
            return found.ToArray();
        }

        /// <summary>A type as a method's text names it: a primitive or void by its short name.</summary>
        static string TypeName(Type type) => type.IsPrimitive || type == typeof(void) ? type.Name : type.ToString();

        static IEnumerable<SettingsProvider> FetchSettingProviderFromAttribute()
        {
            Discover();
            foreach (var method in s_ProviderMethods)
            {
                SettingsProvider provider = null;
                try
                {
                    provider = (SettingsProvider)method.Invoke(null, null);
                }
                catch
                {
                    Debug.LogError("Cannot create Settings Provider for: " + method.Name);
                }
                yield return provider;
            }
        }

        static IEnumerable<SettingsProvider> FetchSettingProvidersFromAttribute()
        {
            Discover();
            foreach (var method in s_GroupMethods)
            {
                SettingsProvider[] group = null;
                try
                {
                    group = (SettingsProvider[])method.Invoke(null, null);
                }
                catch
                {
                    Debug.LogError("Cannot create Settings Providers for: " + method.Name);
                }
                if (group == null) continue;
                foreach (var provider in group)
                    yield return provider;
            }
        }

        static IEnumerable<SettingsProvider> FetchPreferenceItems()
        {
            Discover();
            foreach (var method in s_PreferenceMethods)
            {
                var callback = (Action)Delegate.CreateDelegate(typeof(Action), method);
                foreach (PreferenceItem attr in method.GetCustomAttributes(typeof(PreferenceItem), true))
                    yield return new SettingsProvider("Preferences/" + attr.name, SettingsScope.User) { guiHandler = _ => callback() };
            }
        }
    }
}
