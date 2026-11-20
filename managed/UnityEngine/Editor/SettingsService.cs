using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        static IEnumerable<SettingsProvider> FetchSettingProviderFromAttribute()
        {
            foreach (var method in TypeCache.GetMethodsWithAttribute<SettingsProviderAttribute>())
            {
                if (!MatchesSignature(method, typeof(SettingsProvider)))
                {
                    Debug.LogError($"{FormatMethodSignature(method)} does not match {typeof(SettingsProviderAttribute)} expected signature.\n Use static {typeof(SettingsProvider)} signature()");
                    continue;
                }

                SettingsProvider provider = null;
                try
                {
                    var factory = Delegate.CreateDelegate(typeof(Func<SettingsProvider>), method) as Func<SettingsProvider>;
                    provider = factory?.Invoke();
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
            foreach (var method in TypeCache.GetMethodsWithAttribute<SettingsProviderGroupAttribute>())
            {
                if (!MatchesSignature(method, typeof(SettingsProvider[])))
                {
                    Debug.LogError($"{FormatMethodSignature(method)} does not match {typeof(SettingsProviderGroupAttribute)} expected signature.\n Use static {typeof(SettingsProvider[])} signature()");
                    continue;
                }

                SettingsProvider[] group = null;
                try
                {
                    var factory = Delegate.CreateDelegate(typeof(Func<SettingsProvider[]>), method) as Func<SettingsProvider[]>;
                    group = factory?.Invoke();
                }
                catch
                {
                    Debug.LogError("Cannot create Settings Providers for: " + method.Name);
                }

                if (group != null)
                {
                    foreach (var provider in group)
                        yield return provider;
                }
            }
        }

        static IEnumerable<SettingsProvider> FetchPreferenceItems()
        {
            foreach (var method in TypeCache.GetMethodsWithAttribute<PreferenceItem>())
            {
                if (!method.IsStatic || method.ContainsGenericParameters || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
                    continue;

                var callback = Delegate.CreateDelegate(typeof(Action), method, false) as Action;
                if (callback == null)
                    continue;

                foreach (PreferenceItem attr in method.GetCustomAttributes(typeof(PreferenceItem), true))
                {
                    SettingsProvider provider = null;
                    try
                    {
                        provider = new SettingsProvider("Preferences/" + attr.name, SettingsScope.User)
                        {
                            guiHandler = _ => callback(),
                        };
                    }
                    catch
                    {
                        Debug.LogError("Cannot create preference wrapper for: " + attr.name);
                    }
                    yield return provider;
                }
            }
        }

        static bool MatchesSignature(MethodInfo method, Type returnType) =>
            method.IsStatic && !method.ContainsGenericParameters && method.ReturnType == returnType && method.GetParameters().Length == 0;

        static string FormatMethodSignature(MethodInfo method)
        {
            var parameters = method.GetParameters();
            string paramList = parameters.Length == 0
                ? string.Empty
                : string.Join(", ", parameters.Select(p => p.ParameterType.Name));
            return (method.IsStatic ? "static " : string.Empty) + method.ReturnType + " " + method.Name + "(" + paramList + ")";
        }
    }
}
