using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
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

        internal abstract ShortcutEntry CreateShortcutEntry(MethodInfo methodInfo);
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

        Action m_NoArgumentsAction;

        void NoArgumentShortcutMethodProxy(ShortcutArguments arguments) => m_NoArgumentsAction();

        /// <summary>The shortcut of the method it is on: a method without parameters runs through
        /// a proxy that drops the arguments; a clutch attribute makes a clutch.</summary>
        internal override ShortcutEntry CreateShortcutEntry(MethodInfo methodInfo)
        {
            var id = new Identifier(methodInfo, this);
            var type = this is ClutchShortcutAttribute ? ShortcutType.Clutch : ShortcutType.Action;
            Action<ShortcutArguments> action;
            if (methodInfo.GetParameters().Any())
                action = (Action<ShortcutArguments>)Delegate.CreateDelegate(typeof(Action<ShortcutArguments>), null, methodInfo);
            else
            {
                m_NoArgumentsAction = (Action)Delegate.CreateDelegate(typeof(Action), null, methodInfo);
                action = NoArgumentShortcutMethodProxy;
            }
            var entry = new ShortcutEntry(id, defaultBinding.keyCombinationSequence, action, context, tag, type, displayName, priority);
            foreach (var reserved in methodInfo.GetCustomAttributes(typeof(ReserveModifiersAttribute), inherit: true))
                entry.m_ReservedModifier |= ((ReserveModifiersAttribute)reserved).Modifiers;
            return entry;
        }
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

        /// <summary>A priority left at its default: a global shortcut's, or a context's.</summary>
        internal static int AssignPriority(Type context, int suggestedPriority = int.MaxValue)
        {
            if (suggestedPriority != int.MaxValue)
                return suggestedPriority;
            return context == null || context == ContextManager.globalContextType ? DefaultGlobalPriority : DefaultContextPriority;
        }
    }

}
