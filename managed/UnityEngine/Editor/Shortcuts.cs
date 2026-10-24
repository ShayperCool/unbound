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

        public static void RegisterTag(string tag) { }

        public static void RegisterTag(Enum e) { }

        public static void UnregisterTag(string tag) { }

        public static void UnregisterTag(Enum e) { }
    }

    public partial interface IShortcutManager
    {
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
