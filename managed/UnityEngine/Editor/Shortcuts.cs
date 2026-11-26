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

        // Tags only choose which shortcuts are active; no key reaches a shortcut in Unbound's
        // Editor (as in the batch-mode one), and the active tags are not observable otherwise.
        const string NoShortcuts = "no key reaches a shortcut (as in the batch-mode Editor); tags have no other observable effect";

        [Unbound.EditorFaithful(NoShortcuts)]
        public static void RegisterTag(string tag) { }

        [Unbound.EditorFaithful(NoShortcuts)]
        public static void RegisterTag(Enum e) { }

        [Unbound.EditorFaithful(NoShortcuts)]
        public static void UnregisterTag(string tag) { }

        [Unbound.EditorFaithful(NoShortcuts)]
        public static void UnregisterTag(Enum e) { }

        // A tool context only decides which shortcuts its keys reach, as tags do.
        const string NoContexts = "no key reaches a shortcut (as in the batch-mode Editor); a tool context has no other observable effect";

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
