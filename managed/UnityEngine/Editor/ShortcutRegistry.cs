using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Bindings;

namespace UnityEditor.ShortcutManagement
{
    [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
    internal partial interface IContextManager
    {
        bool playModeContextIsActive { get; }
        void SetFocusedWindow(EditorWindow window);
        void RegisterToolContext(IShortcutContext context);
        void DeregisterToolContext(IShortcutContext context);
        bool HasAnyPriorityContext();
        bool HasPriorityContextOfType(Type type);
        bool HasActiveContextOfType(Type type, bool useActiveForHelperBar = false);
        bool DoContextsConflict(Type context1, Type context2);
        object GetContextInstanceOfType(Type type, bool filterActive, bool useActiveForHelperBar = false);
        List<Type> GetActiveContexts();
        List<string> GetActiveTags();
        void RegisterTag(string tag);
        void RegisterTag(Enum e);
        void UnregisterTag(string tag);
        void UnregisterTag(Enum e);
        bool HasTag(string tag);
    }

    internal partial interface IHelperBarShortcutContext
    {
        bool helperBarActive { get; }
    }

    /// <summary>A context type whose instances come before tool contexts.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal partial class PriorityContextAttribute : Attribute
    {
    }

    internal enum ShortcutType
    {
        Action,
        Clutch,
        Menu,
    }

    /// <summary>
    /// Which shortcut contexts are active, as Unity's own C# keeps them: the global context
    /// always, the focused window, then registered priority contexts and tool contexts that are
    /// active, and the Editor-wide set of tags (kept in an Editor singleton).
    /// </summary>
    internal partial class ContextManager : IContextManager
    {
        class TagManager : ScriptableSingleton<TagManager>
        {
            [SerializeField] string[] m_SerializedTags;

            public HashSet<string> Tags { get; private set; }

            void OnEnable()
            {
                Tags = new HashSet<string>();
                if (m_SerializedTags != null)
                    foreach (var tag in m_SerializedTags)
                        Tags.Add(tag);
            }

            void OnDisable() => m_SerializedTags = Tags.ToArray();
        }

        /// <summary>The context of the shortcuts that declare none.</summary>
        internal partial class GlobalContext
        {
        }

        public static readonly GlobalContext globalContext = new GlobalContext();
        public static readonly Type globalContextType = typeof(GlobalContext);

        readonly WeakReference m_FocusedWindow = new WeakReference(null);
        readonly List<IShortcutContext> m_PriorityContexts = new List<IShortcutContext>();
        readonly List<IShortcutContext> m_ToolContexts = new List<IShortcutContext>();
        static readonly Dictionary<Type, bool> s_IsPriorityContextCache = new Dictionary<Type, bool>();

        public static Action onTagChange;

        public int activeContextCount =>
            1 + (focusedWindow != null ? 1 : 0) + m_PriorityContexts.Count(c => c.active) + m_ToolContexts.Count(c => c.active);

        public bool playModeContextIsActive => focusedWindow is GameView && EditorApplication.isPlaying && !EditorApplication.isPaused;

        EditorWindow focusedWindow => m_FocusedWindow.Target != null && m_FocusedWindow.IsAlive ? (EditorWindow)m_FocusedWindow.Target : null;

        public void SetFocusedWindow(EditorWindow window) => m_FocusedWindow.Target = window;

        static bool IsPriorityContext(IShortcutContext context) => IsPriorityContext(context.GetType());

        static bool IsPriorityContext(Type context)
        {
            if (!s_IsPriorityContextCache.TryGetValue(context, out var priority))
            {
                priority = Attribute.GetCustomAttribute(context, typeof(PriorityContextAttribute)) != null;
                s_IsPriorityContextCache[context] = priority;
            }
            return priority;
        }

        /// <summary>Two contexts of the same kind (priority or not) where one derives from the other.</summary>
        public bool DoContextsConflict(Type context1, Type context2)
        {
            if (IsPriorityContext(context1) != IsPriorityContext(context2))
                return false;
            return context1.IsAssignableFrom(context2) || context2.IsAssignableFrom(context1);
        }

        void RegisterPriorityContext(IShortcutContext context)
        {
            if (!m_PriorityContexts.Contains(context))
                m_PriorityContexts.Add(context);
        }

        void DeregisterPriorityContext(IShortcutContext context) => m_PriorityContexts.Remove(context);

        public void RegisterToolContext(IShortcutContext context)
        {
            if (context == null) return;
            if (IsPriorityContext(context))
                RegisterPriorityContext(context);
            else if (!m_ToolContexts.Contains(context))
                m_ToolContexts.Add(context);
        }

        public void DeregisterToolContext(IShortcutContext context)
        {
            if (context == null) return;
            if (m_PriorityContexts.Contains(context))
                DeregisterPriorityContext(context);
            else if (m_ToolContexts.Contains(context))
                m_ToolContexts.Remove(context);
        }

        public bool HasAnyPriorityContext() => m_PriorityContexts.Count > 0;

        public bool HasPriorityContextOfType(Type type) => GetPriorityContextOfType(type) != null;

        public bool HasActiveContextOfType(Type type, bool useActiveForHelperBar = false) =>
            GetContextInstanceOfType(type, filterActive: true, useActiveForHelperBar) != null;

        static bool IsActive(IShortcutContext context, bool useActiveForHelperBar) =>
            useActiveForHelperBar && context is IHelperBarShortcutContext helperBar ? helperBar.helperBarActive : context.active;

        /// <summary>The first tool context that is active and of the type; without the active
        /// filter, the first tool context of any type (as the Editor's check is grouped).</summary>
        internal object GetToolContextOfType(Type type, bool filterActive = true, bool useActiveForHelperBar = false)
        {
            foreach (var context in m_ToolContexts)
                if (!filterActive || (IsActive(context, useActiveForHelperBar) && type.IsInstanceOfType(context)))
                    return context;
            return null;
        }

        internal object GetPriorityContextOfType(Type type, bool filterActive = true, bool useActiveForHelperBar = false)
        {
            foreach (var context in m_PriorityContexts)
                if ((!filterActive || IsActive(context, useActiveForHelperBar)) && type.IsInstanceOfType(context))
                    return context;
            return null;
        }

        public object GetContextInstanceOfType(Type type, bool filterActive = true, bool useActiveForHelperBar = false)
        {
            if (type == null) return null;
            if (type == globalContextType) return globalContext;
            if (m_FocusedWindow != null && m_FocusedWindow.IsAlive && type.IsInstanceOfType(m_FocusedWindow.Target))
                return m_FocusedWindow.Target;
            return GetPriorityContextOfType(type, filterActive, useActiveForHelperBar)
                ?? GetToolContextOfType(type, filterActive, useActiveForHelperBar);
        }

        internal static string EnumTagFormat(Enum tag) => tag.GetType().Name + "." + tag;

        public void RegisterTag(string tag)
        {
            if (ScriptableSingleton<TagManager>.instance.Tags.Add(tag))
                onTagChange?.Invoke();
        }

        /// <summary>The enum value's tag, after removing the tags of the enum's other values.</summary>
        public void RegisterTag(Enum e)
        {
            foreach (var value in Enum.GetValues(e.GetType()))
                UnregisterTag(EnumTagFormat((Enum)value));
            RegisterTag(EnumTagFormat(e));
        }

        public void UnregisterTag(string tag)
        {
            if (ScriptableSingleton<TagManager>.instance.Tags.Remove(tag))
                onTagChange?.Invoke();
        }

        public void UnregisterTag(Enum e) => UnregisterTag(EnumTagFormat(e));

        public bool HasTag(string tag) => tag != null && ScriptableSingleton<TagManager>.instance.Tags.Contains(tag);

        List<Type> IContextManager.GetActiveContexts()
        {
            var contexts = new List<Type> { globalContextType };
            var focused = m_FocusedWindow.Target?.GetType();
            if (focused != null) contexts.Add(focused);
            contexts.AddRange(m_PriorityContexts.Where(p => p.active).Select(p => p.GetType()));
            contexts.AddRange(m_ToolContexts.Where(c => c.active).Select(c => c.GetType()));
            return contexts;
        }

        public List<string> GetActiveTags() => ScriptableSingleton<TagManager>.instance.Tags.ToList();
    }

    internal partial interface IDirectory
    {
        void GetAllShortcuts(List<ShortcutEntry> output);
        void FindShortcutEntries(List<KeyCombination> combinationSequence, Type[] context, string[] tags, List<ShortcutEntry> outputShortcuts);
        void FindShortcutEntries(List<KeyCombination> combinationSequence, IContextManager contextManager, List<ShortcutEntry> outputShortcuts);
        void FindShortcutEntries(List<KeyCombination> combinationSequence, List<ShortcutEntry> outputShortcuts);
        void FindPotentialShortcutEntries(IContextManager contextManager, List<ShortcutEntry> outputShortcuts, bool useActiveForHelperBar);
        ShortcutEntry FindShortcutEntry(Identifier identifier);
        ShortcutEntry FindShortcutEntry(string identifier);
        void FindPotentialConflicts(Type context, string tag, IList<KeyCombination> binding, IList<ShortcutEntry> output, IContextManager contextManager);
        void FindShortcutsWithConflicts(List<ShortcutEntry> output, IContextManager contextManager);
    }

    /// <summary>
    /// The shortcuts, indexed by their first key, as Unity's own C# looks them up: entries of a
    /// specific context before global ones, and tagged entries only while one of their tags is
    /// active (else only untagged ones).
    /// </summary>
    internal partial class Directory : IDirectory
    {
        Dictionary<KeyCode, List<ShortcutEntry>> m_IndexedShortcutEntries;
        List<ShortcutEntry> m_ShortcutEntries;

        /// <summary>The keyboard and mouse keys up to the last mouse button, and F16 to F24.</summary>
        public static bool IsKeyCodeSupported(KeyCode key) => key <= KeyCode.Mouse6 || (key >= KeyCode.F16 && key <= KeyCode.F24);

        public Directory(IEnumerable<ShortcutEntry> entries) => Initialize(entries);

        public void Initialize(IEnumerable<ShortcutEntry> entries)
        {
            m_ShortcutEntries = new List<ShortcutEntry>(entries.Count());
            m_IndexedShortcutEntries = new Dictionary<KeyCode, List<ShortcutEntry>>(entries.Count());
            foreach (var entry in entries)
            {
                m_ShortcutEntries.Add(entry);
                if (entry.combinations.Any())
                    AddEntry(entry.combinations.First().keyCode, entry);
            }
        }

        public void GetAllShortcuts(List<ShortcutEntry> output) => output.AddRange(m_ShortcutEntries);

        List<ShortcutEntry> GetShortcutEntriesForPrimaryKey(List<KeyCombination> combinationSequence)
        {
            if (combinationSequence == null || combinationSequence.Count < 1) return null;
            return m_IndexedShortcutEntries.GetValueOrDefault(combinationSequence[0].keyCode);
        }

        public void FindShortcutEntries(List<KeyCombination> combinationSequence, Type[] context, string[] tags, List<ShortcutEntry> outputShortcuts)
        {
            outputShortcuts.Clear();
            var candidates = GetShortcutEntriesForPrimaryKey(combinationSequence);
            if (candidates == null) return;
            foreach (var entry in candidates)
                if (entry.StartsWith(combinationSequence) && ShortcutEntryMatchesAnyContext(entry.context, context) && entry.context != typeof(ContextManager.GlobalContext))
                    outputShortcuts.Add(entry);
            if (outputShortcuts.Count == 0)
                foreach (var entry in candidates)
                    if (entry.StartsWith(combinationSequence) && ShortcutEntryMatchesAnyContext(entry.context, context) && entry.context == typeof(ContextManager.GlobalContext))
                        outputShortcuts.Add(entry);
            if (tags == null) return;
            FilterByTags(outputShortcuts, tag => tags.Contains(tag));
        }

        public void FindShortcutEntries(List<KeyCombination> combinationSequence, IContextManager contextManager, List<ShortcutEntry> outputShortcuts)
        {
            outputShortcuts.Clear();
            var candidates = GetShortcutEntriesForPrimaryKey(combinationSequence);
            if (candidates == null) return;
            foreach (var entry in candidates)
                if (entry.StartsWith(combinationSequence) && ShortcutEntrySatisfiesContextManager(contextManager, entry) && entry.context != typeof(ContextManager.GlobalContext))
                    outputShortcuts.Add(entry);
            if (outputShortcuts.Count == 0)
                foreach (var entry in candidates)
                    if (entry.StartsWith(combinationSequence) && ShortcutEntrySatisfiesContextManager(contextManager, entry) && entry.context == typeof(ContextManager.GlobalContext))
                        outputShortcuts.Add(entry);
            FilterByTags(outputShortcuts, contextManager.HasTag);
        }

        /// <summary>When any found entry's tag is active, keeps the entries with an active tag;
        /// otherwise keeps the untagged ones.</summary>
        static void FilterByTags(List<ShortcutEntry> found, Func<string, bool> active)
        {
            bool anyActive = false;
            foreach (var entry in found)
                anyActive |= active(entry.tag);
            for (int i = found.Count - 1; i >= 0; i--)
                if (anyActive ? !active(found[i].tag) : found[i].tag != null)
                    found.RemoveAt(i);
        }

        public void FindShortcutEntries(List<KeyCombination> combinationSequence, List<ShortcutEntry> outputShortcuts) =>
            FindShortcutEntries(combinationSequence, null, null, outputShortcuts);

        public void FindPotentialShortcutEntries(IContextManager contextManager, List<ShortcutEntry> outputShortcuts, bool useActiveForHelperBar)
        {
            outputShortcuts.Clear();
            var all = new List<ShortcutEntry>();
            GetAllShortcuts(all);
            foreach (var entry in all)
                if (ShortcutEntrySatisfiesContextManager(contextManager, entry, useActiveForHelperBar) && (string.IsNullOrWhiteSpace(entry.tag) || contextManager.HasTag(entry.tag)))
                    outputShortcuts.Add(entry);
        }

        public ShortcutEntry FindShortcutEntry(Identifier identifier) => m_ShortcutEntries.FirstOrDefault(s => s.identifier.Equals(identifier));

        public ShortcutEntry FindShortcutEntry(string shortcutId) => FindShortcutEntry(new Identifier { path = shortcutId });

        /// <summary>The entries of a conflicting context and the same tag whose keys the binding
        /// starts with, or that start with the binding.</summary>
        public void FindPotentialConflicts(Type context, string tag, IList<KeyCombination> binding, IList<ShortcutEntry> output, IContextManager contextManager)
        {
            if (!binding.Any()) return;
            var prefix = new List<KeyCombination>(binding.Count);
            output.Clear();
            var candidates = m_IndexedShortcutEntries.GetValueOrDefault(binding[0].keyCode);
            if (candidates == null || !candidates.Any()) return;
            foreach (var entry in candidates)
            {
                if (!contextManager.DoContextsConflict(entry.context, context) || !string.Equals(tag, entry.tag))
                    continue;
                bool matched = false;
                prefix.Clear();
                foreach (var combination in binding)
                {
                    prefix.Add(combination);
                    if (entry.FullyMatches(prefix))
                    {
                        matched = true;
                        break;
                    }
                }
                if (matched || entry.StartsWith(prefix))
                    output.Add(entry);
            }
        }

        public void FindShortcutsWithConflicts(List<ShortcutEntry> output, IContextManager contextManager)
        {
            var added = new HashSet<int>();
            foreach (var entries in m_IndexedShortcutEntries.Values)
            {
                if (entries == null || entries.Count < 2) continue;
                for (int i = 0; i < entries.Count; i++)
                {
                    int first = entries[i].GetHashCode();
                    for (int j = i + 1; j < entries.Count; j++)
                    {
                        int second = entries[j].GetHashCode();
                        if (!DoShortcutEntriesConflict(entries[i], entries[j], contextManager)) continue;
                        if (added.Add(first)) output.Add(entries[i]);
                        if (added.Add(second)) output.Add(entries[j]);
                    }
                }
            }
        }

        void AddEntry(KeyCode keyCode, ShortcutEntry entry)
        {
            if (!m_IndexedShortcutEntries.TryGetValue(keyCode, out var entries))
                m_IndexedShortcutEntries.Add(keyCode, entries = new List<ShortcutEntry>());
            entries.Add(entry);
        }

        /// <summary>Conflicting contexts, the same tag, and keys where one starts the other; an
        /// action and a clutch that share a mouse button do not conflict.</summary>
        bool DoShortcutEntriesConflict(ShortcutEntry shortcutEntry1, ShortcutEntry shortcutEntry2, IContextManager contextManager)
        {
            if (!contextManager.DoContextsConflict(shortcutEntry1.context, shortcutEntry2.context)) return false;
            if (!string.Equals(shortcutEntry1.tag, shortcutEntry2.tag)) return false;
            bool sharesMouse = shortcutEntry1.StartsWith(shortcutEntry2.combinations, KeyCombination.k_MouseKeyCodes)
                || shortcutEntry2.StartsWith(shortcutEntry1.combinations, KeyCombination.k_MouseKeyCodes);
            bool actionAndClutch = (shortcutEntry1.type == ShortcutType.Action && shortcutEntry2.type == ShortcutType.Clutch)
                || (shortcutEntry1.type == ShortcutType.Clutch && shortcutEntry2.type == ShortcutType.Action);
            if (actionAndClutch && sharesMouse) return false;
            return shortcutEntry1.StartsWith(shortcutEntry2.combinations) || shortcutEntry2.StartsWith(shortcutEntry1.combinations);
        }

        static bool ShortcutEntryMatchesAnyContext(Type shortcutEntryContext, Type[] contextList)
        {
            if (contextList == null) return true;
            foreach (var context in contextList)
                if (ShortcutEntryMatchesContext(shortcutEntryContext, context))
                    return true;
            return false;
        }

        static bool ShortcutEntryMatchesContext(Type shortcutEntryContext, Type context) =>
            context == shortcutEntryContext || (shortcutEntryContext != null && shortcutEntryContext.IsAssignableFrom(context));

        static bool ShortcutEntrySatisfiesContextManager(IContextManager contextManager, ShortcutEntry entry, bool useActiveForHelperBar = false) =>
            contextManager.HasActiveContextOfType(entry.context, useActiveForHelperBar) && (!contextManager.playModeContextIsActive || entry.type == ShortcutType.Menu);
    }

    /// <summary>
    /// One shortcut as Unity's own C# holds it: id, display name (the id's path unless given),
    /// default key combinations, context (the global one unless given), tag, kind, priority and the
    /// modifiers its context or method reserves. Overriding its keys (from a profile, which also
    /// sets a menu item's hotkey) is not written by hand yet.
    /// </summary>
    internal partial class ShortcutEntry
    {
        readonly Identifier m_Identifier;
        readonly string m_DisplayName;
        readonly List<KeyCombination> m_DefaultCombinations = new List<KeyCombination>();
#pragma warning disable CS0649 // set by the profile overrides, still generated stubs
        List<KeyCombination> m_OverriddenCombinations;
#pragma warning restore CS0649
        readonly Action<ShortcutArguments> m_Action;
        readonly Type m_Context;
        readonly Type m_ClutchActivatedContext;
        readonly string m_Tag;
        readonly ShortcutType m_Type;
        readonly int m_Priority;
        internal ShortcutModifiers m_ReservedModifier;

        public Identifier identifier => m_Identifier;
        public string displayName => m_DisplayName;
        public IList<KeyCombination> combinations => activeCombination;
        public bool overridden => m_OverriddenCombinations != null;
        public Action<ShortcutArguments> action => m_Action;
        public Type context => m_Context;
        public Type clutchContext => m_ClutchActivatedContext;
        public string tag => m_Tag;
        public ShortcutType type => m_Type;
        public int priority => m_Priority;

        List<KeyCombination> activeCombination => m_OverriddenCombinations ?? m_DefaultCombinations;

        internal ShortcutEntry(Identifier id, IEnumerable<KeyCombination> defaultCombination, Action<ShortcutArguments> action, Type context, ShortcutType type, string displayName = null, int priority = int.MaxValue)
            : this(id, defaultCombination, action, context, null, type, displayName, priority) { }

        internal ShortcutEntry(Identifier id, IEnumerable<KeyCombination> defaultCombination, Action<ShortcutArguments> action, Type context, string tag, ShortcutType type, string displayName = null, int priority = int.MaxValue)
        {
            m_Identifier = id;
            m_DefaultCombinations = defaultCombination.ToList();
            m_Context = context ?? ContextManager.globalContextType;
            m_Tag = tag;
            m_Action = action;
            m_Type = type;
            m_DisplayName = displayName ?? id.path;
            m_Priority = ShortcutAttributeUtility.AssignPriority(context, priority);
            if (typeof(IShortcutContext).IsAssignableFrom(m_Context))
                foreach (var reserved in m_Context.GetCustomAttributes(typeof(ReserveModifiersAttribute), inherit: true))
                    m_ReservedModifier |= ((ReserveModifiersAttribute)reserved).Modifiers;
            if (m_Action == null) return;
            foreach (var clutch in m_Action.Method.GetCustomAttributes<ClutchShortcutAttribute>())
                if (clutch.clutchActivatedContext != null)
                    m_ClutchActivatedContext = clutch.clutchActivatedContext;
        }

        public override string ToString() =>
            displayName + " " + string.Join(",", combinations.Select(c => c.ToString()).ToArray()) + " [" + context?.Name + "] "
            + (!string.IsNullOrWhiteSpace(tag) ? "[" + tag + "]" : "");

        /// <summary>Whether the keys start with the prefix: all but its last combination equal,
        /// the last equal without the reserved modifiers (and, with key codes given, both of them
        /// among those).</summary>
        public bool StartsWith(IList<KeyCombination> prefix, IEnumerable<KeyCode> keyCodes = null)
        {
            if (activeCombination.Count < prefix.Count) return false;
            if (prefix.Count == 0) return true;
            for (int i = 0; i < prefix.Count - 1; i++)
                if (!prefix[i].Equals(activeCombination[i]))
                    return false;
            var last = prefix.Last();
            var own = activeCombination[prefix.Count - 1];
            if (m_ReservedModifier != ShortcutModifiers.None)
            {
                last = new KeyCombination(last.keyCode, last.modifiers & ~m_ReservedModifier);
                own = new KeyCombination(own.keyCode, own.modifiers & ~m_ReservedModifier);
            }
            if (!last.Equals(own)) return false;
            return keyCodes == null || (HasSpecifiedKeyCode(keyCodes, last.keyCode) & HasSpecifiedKeyCode(keyCodes, own.keyCode));
        }

        static bool HasSpecifiedKeyCode(IEnumerable<KeyCode> keyCodes, KeyCode currentKeyCode) =>
            keyCodes != null && keyCodes.Contains(currentKeyCode);

        public bool FullyMatches(List<KeyCombination> other) => activeCombination.Count == other.Count && StartsWith(other);

        internal List<KeyCombination> GetDefaultCombinations() => m_DefaultCombinations;
    }
}
