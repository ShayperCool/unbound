using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Unbound.Runtime;

namespace UnityEditor
{
    /// <summary>
    /// Activates Multiplayer Play Mode virtual players requested via <c>unbound run --virtual-player</c>
    /// by invoking the package's own <c>UnityPlayer.Activate</c> path (standing for the Activate toggle
    /// in Unity's Multiplayer Play Mode window). Never auto-activates from saved state; fails loudly
    /// when the package is missing, its version is unsupported, or a player name/index is invalid.
    /// </summary>
    internal static class VirtualPlayerActivation
    {
        internal const string PackageName = "com.unity.multiplayer.playmode";
        internal const string SupportedVersion = "1.5.0";

        const string MultiplayerPlaymodeTypeName = "Unity.Multiplayer.Playmode.Workflow.Editor.MultiplayerPlaymode";
        const string UnityPlayerTypeName = "Unity.Multiplayer.Playmode.Workflow.Editor.UnityPlayer";

        static string[] s_Requested = Array.Empty<string>();

        internal static void Configure(string[] requested)
        {
            s_Requested = requested != null && requested.Length > 0 ? (string[])requested.Clone() : Array.Empty<string>();
        }

        internal static bool IsSupportedVersion(string version) =>
            string.Equals(version, SupportedVersion, StringComparison.Ordinal);

        /// <summary>Activates every virtual player requested on the command line, if any.</summary>
        internal static void ActivateRequested()
        {
            if (s_Requested.Length == 0) return;

            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PackageName);
            if (package == null)
            {
                throw new InvalidOperationException(
                    $"Unbound: --virtual-player requires the {PackageName} package, which is not installed in this project.");
            }
            if (!IsSupportedVersion(package.version))
            {
                throw new InvalidOperationException(
                    $"Unbound: --virtual-player does not support {PackageName} version '{package.version}' (supported: {SupportedVersion}).");
            }

            var mppmType = FindType(MultiplayerPlaymodeTypeName) ?? throw new InvalidOperationException(
                $"Unbound: --virtual-player could not find '{MultiplayerPlaymodeTypeName}' in {PackageName} {package.version}.");
            var playerType = FindType(UnityPlayerTypeName) ?? throw new InvalidOperationException(
                $"Unbound: --virtual-player could not find '{UnityPlayerTypeName}' in {PackageName} {package.version}.");

            const BindingFlags anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            const BindingFlags anyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var playersProp = mppmType.GetProperty("Players", anyStatic) ?? throw new InvalidOperationException(
                $"Unbound: --virtual-player could not find '{MultiplayerPlaymodeTypeName}.Players' in {PackageName} {package.version}.");
            var nameProp = playerType.GetProperty("Name", anyInstance) ?? throw new InvalidOperationException(
                $"Unbound: --virtual-player could not find '{UnityPlayerTypeName}.Name' in {PackageName} {package.version}.");
            var typeProp = playerType.GetProperty("Type", anyInstance) ?? throw new InvalidOperationException(
                $"Unbound: --virtual-player could not find '{UnityPlayerTypeName}.Type' in {PackageName} {package.version}.");
            var activateMethod = FindActivateMethod(playerType) ?? throw new InvalidOperationException(
                $"Unbound: --virtual-player could not find '{UnityPlayerTypeName}.Activate(out ActivationError, List<string>)' in {PackageName} {package.version}.");

            // Reading Players runs MultiplayerPlaymode's static constructor, which initializes
            // PlayerOne..PlayerFour from VirtualProjectWorkflow.OnInitialized.
            if (playersProp.GetValue(null) is not Array players || players.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Unbound: --virtual-player found no initialized players in {PackageName} {package.version} (Multiplayer Play Mode workflow did not initialize).");
            }

            var entries = new PlayerEntry[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                var instance = players.GetValue(i) ?? throw new InvalidOperationException(
                    $"Unbound: --virtual-player encountered a null player entry at index {i + 1}.");
                string name = nameProp.GetValue(instance) as string ?? string.Empty;
                string kind = typeProp.GetValue(instance)?.ToString() ?? string.Empty;
                entries[i] = new PlayerEntry(i + 1, name, string.Equals(kind, "Main", StringComparison.Ordinal), instance);
            }

            foreach (var raw in s_Requested)
            {
                var entry = ResolveTarget(raw, entries);
                var invokeArgs = new object[] { null, null };
                object ret;
                try
                {
                    ret = activateMethod.Invoke(entry.Instance, invokeArgs);
                }
                catch (TargetInvocationException tie) when (tie.InnerException != null)
                {
                    throw new InvalidOperationException(
                        $"Unbound: activating virtual player '{entry.Name}' (index {entry.Index}) threw {tie.InnerException.GetType().Name}: {tie.InnerException.Message}",
                        tie.InnerException);
                }
                bool ok = ret is bool b && b;
                string error = invokeArgs[0]?.ToString() ?? "Unknown";
                if (!ok || !string.Equals(error, "None", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unbound: activating virtual player '{entry.Name}' (index {entry.Index}) failed ({error}).");
                }
            }
        }

        internal readonly struct PlayerEntry
        {
            public readonly int Index;
            public readonly string Name;
            public readonly bool IsMain;
            public readonly object Instance;

            public PlayerEntry(int index, string name, bool isMain, object instance)
            {
                Index = index;
                Name = name;
                IsMain = isMain;
                Instance = instance;
            }
        }

        internal static PlayerEntry ResolveTarget(string rawSpec, PlayerEntry[] entries)
        {
            string spec = (rawSpec ?? string.Empty).Trim();
            if (spec.Length == 0)
            {
                throw new InvalidOperationException(
                    "Unbound: --virtual-player requires a non-empty player name or 1-based index.");
            }
            if (int.TryParse(spec, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oneBased))
            {
                if (oneBased < 1 || oneBased > entries.Length)
                {
                    throw new InvalidOperationException(
                        $"Unbound: --virtual-player '{spec}' is out of range (expected 2..{entries.Length} or a virtual player name).");
                }
                var byIndex = entries[oneBased - 1];
                if (byIndex.IsMain)
                {
                    throw new InvalidOperationException(
                        $"Unbound: --virtual-player '{spec}' selects the main player ('{byIndex.Name}'); virtual players are 2..{entries.Length}.");
                }
                return byIndex;
            }
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(entries[i].Name, spec, StringComparison.OrdinalIgnoreCase))
                {
                    if (entries[i].IsMain)
                    {
                        throw new InvalidOperationException(
                            $"Unbound: --virtual-player '{spec}' selects the main player; virtual players are 2..{entries.Length}.");
                    }
                    return entries[i];
                }
            }
            var available = new List<string>();
            for (int i = 0; i < entries.Length; i++)
            {
                if (!entries[i].IsMain) available.Add($"{entries[i].Index} ('{entries[i].Name}')");
            }
            throw new InvalidOperationException(
                $"Unbound: --virtual-player '{spec}' did not match any virtual player (available: {string.Join(", ", available)}).");
        }

        static MethodInfo FindActivateMethod(Type playerType)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var m in playerType.GetMethods(flags))
            {
                if (m.Name != "Activate" || m.ReturnType != typeof(bool)) continue;
                var ps = m.GetParameters();
                if (ps.Length == 2
                    && ps[0].IsOut
                    && ps[0].ParameterType.IsByRef
                    && ps[0].ParameterType.GetElementType()?.IsEnum == true
                    && ps[1].ParameterType == typeof(List<string>))
                {
                    return m;
                }
            }
            return null;
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in GameDomain.Assemblies())
            {
                var t = assembly.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
