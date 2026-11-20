using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor
{
    public enum SettingsScope
    {
        User = 0,
        Project = 1,
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed partial class SettingsProviderAttribute : Attribute
    {
        public SettingsProviderAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed partial class SettingsProviderGroupAttribute : Attribute
    {
        public SettingsProviderGroupAttribute() { }
    }

    [Obsolete("[PreferenceItem] is deprecated. Use [SettingsProvider] instead.")]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed partial class PreferenceItem : Attribute
    {
        public string name;

        public PreferenceItem(string name) => this.name = name;
    }

    /// <summary>
    /// A Project Settings or Preferences page descriptor with search keywords and lifecycle/GUI
    /// callbacks. Matches the Unity 6000.0.71f1 Editor (tests/projects/settingsprovider).
    /// </summary>
    public partial class SettingsProvider
    {
        string m_Label;
        readonly string m_Name;
        HashSet<string> m_Keywords;
        bool m_Activated;

        internal string[] pathTokens { get; }
        internal Texture2D icon { get; set; }

        public string settingsPath { get; }
        public SettingsScope scope { get; }

        public string label
        {
            get => string.IsNullOrEmpty(m_Label) ? m_Name : m_Label;
            set => m_Label = L10n.Tr(value);
        }

        public IEnumerable<string> keywords
        {
            get => m_Keywords;
            set => m_Keywords = new HashSet<string>(value);
        }

        public Action<string> guiHandler { get; set; }
        public Action titleBarGuiHandler { get; set; }
        public Action footerBarGuiHandler { get; set; }
        public Action<string, VisualElement> activateHandler { get; set; }
        public Action deactivateHandler { get; set; }
        public Func<string, bool> hasSearchInterestHandler { get; set; }
        public Action inspectorUpdateHandler { get; set; }

        public SettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
        {
            settingsPath = path.Replace("\n", " ").Replace("\\", "/");
            int lastSlash = settingsPath.LastIndexOf("/", StringComparison.Ordinal);
            string leaf = lastSlash != -1 ? settingsPath.Substring(lastSlash + 1) : settingsPath;
            m_Name = L10n.Tr(leaf);
            pathTokens = settingsPath.Split('/');
            scope = scopes;
            m_Keywords = keywords != null ? new HashSet<string>(keywords) : new HashSet<string>();
        }

        public virtual void OnActivate(string searchContext, VisualElement rootElement) =>
            activateHandler?.Invoke(searchContext, rootElement);

        public virtual void OnDeactivate() => deactivateHandler?.Invoke();

        public virtual bool HasSearchInterest(string searchContext)
        {
            if (hasSearchInterestHandler != null)
                return hasSearchInterestHandler(searchContext);

            foreach (string keyword in keywords)
            {
                if (SearchUtils.MatchSearchGroups(searchContext, keyword))
                    return true;
            }

            return false;
        }

        public virtual void OnGUI(string searchContext) => guiHandler?.Invoke(searchContext);

        public virtual void OnTitleBarGUI() => titleBarGuiHandler?.Invoke();

        public virtual void OnFooterBarGUI() => footerBarGuiHandler?.Invoke();

        public virtual void OnInspectorUpdate() => inspectorUpdateHandler?.Invoke();

        [Unbound.EditorFaithful("no settings window is open, so there is no window to repaint")]
        public void Repaint() { }

        public void PopulateSearchKeywordsFromGUIContentProperties<T>() =>
            keywords = GetSearchKeywordsFromGUIContentProperties<T>();

        [Unbound.EditorFaithful("Unity's base implementation")]
        internal virtual void FocusLost() { }

        internal void Activate(string searchContext, VisualElement rootElement)
        {
            if (!m_Activated)
                OnActivate(searchContext, rootElement);
            m_Activated = true;
        }

        internal void Deactivate()
        {
            if (m_Activated)
            {
                m_Activated = false;
                OnDeactivate();
            }
        }

        public static IEnumerable<string> GetSearchKeywordsFromGUIContentProperties<T>()
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static;
            var fieldTexts = typeof(T)
                .GetFields(Flags)
                .Where(f => typeof(GUIContent).IsAssignableFrom(f.FieldType))
                .Select(f => ((GUIContent)f.GetValue(null)).text);
            var propertyTexts = typeof(T)
                .GetProperties(Flags)
                .Where(p => typeof(GUIContent).IsAssignableFrom(p.PropertyType))
                .Select(p => ((GUIContent)p.GetValue(null, null)).text);
            return fieldTexts
                .Concat(propertyTexts)
                .Where(s => s != null)
                .Select(s => s.ToLowerInvariant())
                .Distinct();
        }

        public static IEnumerable<string> GetSearchKeywordsFromSerializedObject(SerializedObject serializedObject)
        {
            var result = new HashSet<string>();
            var iterator = serializedObject.GetIterator();
            while (iterator.NextVisible(true))
                result.Add(iterator.displayName);
            return result;
        }

        public static IEnumerable<string> GetSearchKeywordsFromPath(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset != null)
                return GetSearchKeywordsFromSerializedObject(new SerializedObject(asset));
            return Array.Empty<string>();
        }
    }
}
