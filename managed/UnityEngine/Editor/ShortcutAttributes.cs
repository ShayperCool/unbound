using System;
using System.ComponentModel;
using UnityEngine;

namespace UnityEditor.ShortcutManagement
{
    [Flags]
    public enum ShortcutModifiers
    {
        None = 0,
        Alt = 1,
        Action = 2,
        Shift = 4,
        Control = 8,
    }

    public enum ShortcutStage
    {
        Begin,
        End,
    }

    public abstract partial class ShortcutBaseAttribute : Attribute
    {
        protected ShortcutBaseAttribute() { }
    }

    /// <summary>
    /// A shortcut a static method declares, as Unity's own C# writes it: its id (also its display
    /// name until one is set), context, tag, default binding and priority (a global shortcut's
    /// default priority below a context's).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public partial class ShortcutAttribute : ShortcutBaseAttribute
    {
        internal string identifier { get; }
        internal Type context { get; }
        internal string tag { get; }
        internal ShortcutBinding defaultBinding { get; }
        internal int priority { get; }
        public string displayName { get; set; }

        ShortcutAttribute(string id, Type context, string tag, int priority, ShortcutBinding defaultBinding)
        {
            identifier = id;
            this.context = context;
            this.tag = tag;
            this.defaultBinding = defaultBinding;
            this.priority = ShortcutAttributeUtility.AssignPriority(context, priority);
            displayName = identifier;
        }

        public ShortcutAttribute(string id, [DefaultValue("null")] Type context = null)
            : this(id, context, null, int.MaxValue, ShortcutBinding.empty) { }

        public ShortcutAttribute(string id, Type context, KeyCode defaultKeyCode, [DefaultValue("None")] ShortcutModifiers defaultShortcutModifiers = ShortcutModifiers.None)
            : this(id, context, null, int.MaxValue, new ShortcutBinding(new KeyCombination(defaultKeyCode, defaultShortcutModifiers))) { }

        public ShortcutAttribute(string id, Type context, string tag, KeyCode defaultKeyCode, [DefaultValue("None")] ShortcutModifiers defaultShortcutModifiers = ShortcutModifiers.None)
            : this(id, context, tag, int.MaxValue, new ShortcutBinding(new KeyCombination(defaultKeyCode, defaultShortcutModifiers))) { }

        public ShortcutAttribute(string id, Type context, string tag, KeyCode defaultKeyCode, [DefaultValue("None")] ShortcutModifiers defaultShortcutModifiers, int priority)
            : this(id, context, tag, priority, new ShortcutBinding(new KeyCombination(defaultKeyCode, defaultShortcutModifiers))) { }

        public ShortcutAttribute(string id, KeyCode defaultKeyCode, [DefaultValue("None")] ShortcutModifiers defaultShortcutModifiers = ShortcutModifiers.None)
            : this(id, null, null, int.MaxValue, new ShortcutBinding(new KeyCombination(defaultKeyCode, defaultShortcutModifiers))) { }
    }

    /// <summary>A shortcut that acts while its keys are held (begins on press, ends on release).</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public partial class ClutchShortcutAttribute : ShortcutAttribute
    {
        internal readonly Type clutchActivatedContext;

        public ClutchShortcutAttribute(string id, [DefaultValue("null")] Type context = null) : base(id, context) { }

        public ClutchShortcutAttribute(string id, Type context, KeyCode defaultKeyCode, [DefaultValue("None")] ShortcutModifiers defaultShortcutModifiers = ShortcutModifiers.None)
            : base(id, context, defaultKeyCode, defaultShortcutModifiers) { }

        public ClutchShortcutAttribute(string id, Type context, string tag, KeyCode defaultKeyCode, [DefaultValue("None")] ShortcutModifiers defaultShortcutModifiers = ShortcutModifiers.None)
            : base(id, context, tag, defaultKeyCode, defaultShortcutModifiers) { }

        public ClutchShortcutAttribute(string id, Type context, string tag, KeyCode defaultKeyCode, [DefaultValue("None")] ShortcutModifiers defaultShortcutModifiers, int priority)
            : base(id, context, tag, defaultKeyCode, defaultShortcutModifiers, priority) { }

        public ClutchShortcutAttribute(string id, KeyCode defaultKeyCode, [DefaultValue("None")] ShortcutModifiers defaultShortcutModifiers = ShortcutModifiers.None)
            : base(id, defaultKeyCode, defaultShortcutModifiers) { }

        internal ClutchShortcutAttribute(string id, Type context, Type clutchActivatedContext, KeyCode defaultKeyCode, ShortcutModifiers defaultShortcutModifiers = ShortcutModifiers.None)
            : base(id, context, defaultKeyCode, defaultShortcutModifiers)
        {
            this.clutchActivatedContext = clutchActivatedContext;
        }
    }

    /// <summary>The preference key a shortcut replaced (its name and default).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal partial class FormerlyPrefKeyAsAttribute : Attribute
    {
        public readonly string name;
        public readonly string defaultValue;

        public FormerlyPrefKeyAsAttribute(string name, string defaultValue)
        {
            this.name = name;
            this.defaultValue = defaultValue;
        }
    }

    /// <summary>The modifiers a shortcut keeps for itself while it is active.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public partial class ReserveModifiersAttribute : Attribute
    {
        public ShortcutModifiers Modifiers { get; }

        public ReserveModifiersAttribute(ShortcutModifiers modifiers) => Modifiers = modifiers;
    }

    internal partial class ShortcutAttributeUtility
    {
        internal const int DefaultGlobalPriority = 1000000;
        internal const int DefaultContextPriority = 1000;

        // The context manager's global context (ContextManager.globalContextType). The context
        // manager is not written by hand yet, so its type is looked up by name.
        static readonly Type s_GlobalContextType = typeof(ShortcutAttributeUtility).Assembly.GetType("UnityEditor.ShortcutManagement.ContextManager+GlobalContext");

        /// <summary>A priority left at its default: a global shortcut's, or a context's.</summary>
        internal static int AssignPriority(Type context, int suggestedPriority = int.MaxValue)
        {
            if (suggestedPriority != int.MaxValue)
                return suggestedPriority;
            return context == null || (s_GlobalContextType != null && context == s_GlobalContextType) ? DefaultGlobalPriority : DefaultContextPriority;
        }
    }

}
