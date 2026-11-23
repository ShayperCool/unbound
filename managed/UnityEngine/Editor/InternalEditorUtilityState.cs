using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditorInternal
{
    /// <summary>
    /// InternalEditorUtility members that editor scripts use, as the Unity 6000.0.71f1 Editor has
    /// them (tests/projects/editorinternals).
    /// </summary>
    public partial class InternalEditorUtility
    {
        /// <summary>Whether the scripts are waiting to be reloaded on request: Unbound reloads them
        /// when their sources change and takes no such request (EditorUtility.RequestScriptReload).</summary>
        [Unbound.EditorFaithful("no script reload is ever pending on request: RequestScriptReload reloads nothing")]
        internal static bool IsScriptReloadRequested() => false;

        [Unbound.EditorFaithful("Unbound has no editor views to repaint (as the batch-mode Editor)")]
        public static void RepaintAllViews() { }

        internal static bool s_WarnedRevision;

        /// <summary>The release with its build revision, as the Editor of the release states it
        /// ("6000.0.71f1 (907bc2d768b5)"). Without a known revision (no Hub modules.json beside the
        /// Editor, and none in the project's version file) the release alone, said once.</summary>
        public static string GetFullUnityVersion()
        {
            string version = UnityEngine.ProjectInfo.UnityVersion;
            string revision = UnityEngine.ProjectInfo.UnityRevision;
            if (!string.IsNullOrEmpty(revision)) return version + " (" + revision + ")";
            if (!s_WarnedRevision)
            {
                s_WarnedRevision = true;
                Debug.LogError($"Unbound: the build revision of Unity {version} is not known (no Hub modules.json beside an installed {version} Editor and none for {version} in ProjectSettings/ProjectVersion.txt): InternalEditorUtility.GetFullUnityVersion answers the release without it, so code that reads the revision out of it (as the performance testing package does) will fail.");
            }
            return version;
        }

        /// <summary>The release without its stage and build ("6000.0.71").</summary>
        public static string GetUnityVersionDigits()
        {
            string version = UnityEngine.ProjectInfo.UnityVersion ?? "";
            int end = 0, dots = 0;
            while (end < version.Length && (char.IsDigit(version[end]) || version[end] == '.' && ++dots < 3)) end++;
            return version.Substring(0, end);
        }

        /// <summary>The release ("6000.0.71f1").</summary>
        public static string GetUnityDisplayVersion() => UnityEngine.ProjectInfo.UnityVersion;

        /// <summary>The release and its build numbers ("6000.0.71f1.8720.19091").</summary>
        public static string GetUnityDisplayVersionVerbose()
        {
            var facts = BuildFacts(nameof(GetUnityDisplayVersionVerbose));
            return facts == null ? GetUnityDisplayVersion() : GetUnityDisplayVersion() + "." + facts.VerboseBuild;
        }

        /// <summary>The build revision ("907bc2d768b5"), as in <see cref="GetFullUnityVersion"/>;
        /// empty when it is not known.</summary>
        public static string GetUnityBuildHash()
        {
            string revision = UnityEngine.ProjectInfo.UnityRevision;
            if (string.IsNullOrEmpty(revision)) GetFullUnityVersion();
            return revision ?? "";
        }

        public static string GetUnityBuildBranch() => BuildFacts(nameof(GetUnityBuildBranch))?.Branch ?? "";

        public static int GetUnityRevision() => BuildFacts(nameof(GetUnityRevision))?.Revision ?? 0;

        /// <summary>The build's date, in seconds since 1970.</summary>
        public static int GetUnityVersionDate() => BuildFacts(nameof(GetUnityVersionDate))?.Date ?? 0;

        public static string GetUnityProductName() => BuildFacts(nameof(GetUnityProductName))?.ProductName ?? "";

        public static string GetUnityCopyright() => BuildFacts(nameof(GetUnityCopyright))?.Copyright ?? "";

        internal static readonly HashSet<string> s_UnmeasuredBuildFacts = new HashSet<string>();

        /// <summary>The release family's build facts; for a family without them an error, once per
        /// member, and null (the members answer empty or 0).</summary>
        static Unbound.Runtime.Compat.EditorBuildFacts BuildFacts(string member)
        {
            var facts = Unbound.Runtime.Compat.EditorBuild;
            if (facts == null && s_UnmeasuredBuildFacts.Add(member))
                Debug.LogError($"Unbound: InternalEditorUtility.{member} is not measured for Unity {UnityEngine.ProjectInfo.UnityVersion}: it answers empty (or 0).");
            return facts;
        }

        // The Inspector's expanded state: one for all objects of a type (a script's class is its
        // type), expanded until collapsed; a material has its own, collapsed until expanded. It
        // lasts for the session (the Editor also keeps it in the project's Library: not done).
        static readonly Dictionary<Type, bool> s_ExpandedTypes = new Dictionary<Type, bool>();
        static readonly Dictionary<int, bool> s_ExpandedMaterials = new Dictionary<int, bool>();

        /// <summary>Whether the Inspector shows an object's section open; false without an object.</summary>
        public static bool GetIsInspectorExpanded(Object obj)
        {
            if (obj == null) return false;
            if (obj is Material) return s_ExpandedMaterials.TryGetValue(obj.GetInstanceID(), out bool material) && material;
            return !s_ExpandedTypes.TryGetValue(obj.GetType(), out bool expanded) || expanded;
        }

        public static void SetIsInspectorExpanded(Object obj, bool isExpanded)
        {
            if (obj == null) return;
            if (obj is Material) s_ExpandedMaterials[obj.GetInstanceID()] = isExpanded;
            else s_ExpandedTypes[obj.GetType()] = isExpanded;
        }

        /// <summary>A layer mask from a mask over the named layers in order (the layer popups'):
        /// bit n is the n-th named layer; bits past the last one name nothing.</summary>
        public static LayerMask ConcatenatedLayersMaskToLayerMask(int concatenatedLayersMask)
        {
            int mask = 0, index = 0;
            var names = Unbound.Runtime.TagManager.Layers;
            for (int layer = 0; layer < names.Length && layer < 32; layer++)
            {
                if (string.IsNullOrEmpty(names[layer])) continue;
                if ((concatenatedLayersMask & (1 << index)) != 0) mask |= 1 << layer;
                index++;
            }
            return mask;
        }

        /// <summary>The mask over the named layers of a layer mask; a layer without a name counts
        /// as the named layer before it.</summary>
        public static int LayerMaskToConcatenatedLayersMask(LayerMask mask)
        {
            int concatenated = 0, index = -1, value = mask.value;
            var names = Unbound.Runtime.TagManager.Layers;
            for (int layer = 0; layer < 32; layer++)
            {
                if (layer < names.Length && !string.IsNullOrEmpty(names[layer])) index++;
                if (index >= 0 && (value & (1 << layer)) != 0) concatenated |= 1 << index;
            }
            return concatenated;
        }
    }
}
