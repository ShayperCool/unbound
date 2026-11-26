using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEditor.ShortcutManagement
{
    /// <summary>
    /// The Editor's shortcut manager. Unbound's Editor has no shortcut UI: the default profile is
    /// the only one, no shortcut is known or rebound, and the change events never fire. The
    /// instance answers every member of <see cref="IShortcutManager"/> so (a proxy over it).
    /// </summary>
    public static partial class ShortcutManager
    {
        public const string defaultProfileId = "Default";

        public static IShortcutManager instance { get; } = (IShortcutManager)DispatchProxy.Create(typeof(IShortcutManager), typeof(DefaultShortcutManager));

        // The Editor routes these to its shortcut controller's context manager. The tags are the
        // Editor-wide set every context manager shares (an Editor singleton), so any manager
        // registers them as the Editor's does; the shortcut controller is not written by hand yet.
        static ContextManager s_TagContextManager;
        static ContextManager tagContextManager => s_TagContextManager ??= new ContextManager();

        public static void RegisterTag(string tag) => tagContextManager.RegisterTag(tag);

        public static void RegisterTag(Enum e) => tagContextManager.RegisterTag(e);

        public static void UnregisterTag(string tag) => tagContextManager.UnregisterTag(tag);

        public static void UnregisterTag(Enum e) => tagContextManager.UnregisterTag(e);

        // A tool context is the Editor's shortcut controller's own (not one shared set, as the
        // tags are), and it only decides which shortcuts the keys reach.
        const string NoContexts = "no key reaches a shortcut (as in the batch-mode Editor), and the shortcut controller whose context manager would hold the context is not written by hand yet";

        [Unbound.EditorFaithful(NoContexts)]
        public static void RegisterContext(IShortcutContext context) { }

        [Unbound.EditorFaithful(NoContexts)]
        public static void UnregisterContext(IShortcutContext context) { }
    }

    public partial interface IShortcutManager
    {
    }

    /// <summary>A tool's shortcut context: its shortcuts apply while it is active.</summary>
    public partial interface IShortcutContext
    {
        bool active { get; }
    }

    /// <summary>The default profile alone, no shortcuts.</summary>
    internal class DefaultShortcutManager : DispatchProxy
    {
        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod == null) return null;
            switch (targetMethod.Name)
            {
                case "get_activeProfileId": return ShortcutManager.defaultProfileId;
                case "GetAvailableProfileIds": return new[] { ShortcutManager.defaultProfileId };
                case "IsProfileIdValid": return args?.Length > 0 && (args[0] as string) == ShortcutManager.defaultProfileId;
                case "IsProfileReadOnly": return args?.Length > 0 && (args[0] as string) == ShortcutManager.defaultProfileId;
                case "GetAvailableShortcutIds": return Array.Empty<string>();
            }
            var type = targetMethod.ReturnType;
            return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
        }
    }
}
