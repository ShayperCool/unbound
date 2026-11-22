using System;
using System.Collections.Generic;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The Project window's helpers. Unbound opens no Project window, so naming an asset being
    /// created does not wait for the user: the end action runs at once, on a unique path under
    /// Assets, then is cleaned up and the created object selected (matches the Unity 6000.0.71f1
    /// Editor in Play mode, tests/projects/projectwindowutil). The asset-creating helpers that
    /// pass their own end actions (CreateAsset, CreateFolder, CreateScene, ...) stay stubs.
    /// </summary>
    public partial class ProjectWindowUtil
    {
        /// <summary>The instance ID an asset being created gets when the caller has none.</summary>
        const int AssetBeingCreatedInstanceID = int.MaxValue - 1;

        public ProjectWindowUtil() { }

        public static void StartNameEditingIfProjectWindowExists(int instanceID, EndNameEditAction endAction, string pathName, Texture2D icon, string resourceFile) =>
            StartNameEditingIfProjectWindowExists(instanceID, endAction, pathName, icon, resourceFile, true);

        /// <summary>Runs <paramref name="endAction"/> as if the name were accepted: on the path
        /// under Assets (prefixed unless it starts with "assets/" in any case), made unique; then
        /// selects the object of <paramref name="instanceID"/> (0 stands for the asset being created,
        /// which no object answers).</summary>
        public static void StartNameEditingIfProjectWindowExists(int instanceID, EndNameEditAction endAction, string pathName, Texture2D icon, string resourceFile, bool selectAssetBeingCreated)
        {
            if (instanceID == 0) instanceID = AssetBeingCreatedInstanceID;
            if (!pathName.StartsWith("assets/", StringComparison.CurrentCultureIgnoreCase)) pathName = "Assets/" + pathName;
            EndNameEditAction(endAction, instanceID, pathName, resourceFile, true);
            if (selectAssetBeingCreated) Selection.activeObject = EditorUtility.InstanceIDToObject(instanceID);
        }

        /// <summary>Ends a name edit: the action, accepted or cancelled, on the unique form of the
        /// path, then its clean-up.</summary>
        internal static void EndNameEditAction(EndNameEditAction action, int instanceId, string pathName, string resourceFile, bool accepted)
        {
            pathName = AssetDatabase.GenerateUniqueAssetPath(pathName);
            if (action == null) return;
            if (accepted) action.Action(instanceId, pathName, resourceFile);
            else action.Cancelled(instanceId, pathName, resourceFile);
            action.CleanUp();
        }

        /// <summary>The folder the Project window shows: Assets, without one.</summary>
        internal static string GetActiveFolderPath() => "Assets";

        /// <summary>False: no two-column Project window shows a folder.</summary>
        internal static bool TryGetActiveFolderPath(out string path)
        {
            path = string.Empty;
            return false;
        }

        /// <summary>Selects the object (framing it would need a Project window).</summary>
        public static void ShowCreatedAsset(Object o) => Selection.activeObject = o;

        public static bool IsFolder(int instanceID) => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(instanceID));

        /// <summary>The path up to its last '/', once outer slashes are trimmed; null for an empty
        /// path or one without a '/'.</summary>
        public static string GetContainingFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Trim('/');
            int slash = path.LastIndexOf('/');
            return slash < 0 ? null : path.Substring(0, slash);
        }

        /// <summary>The given folders, sorted and without outer slashes, less each one inside the
        /// last folder kept before it (its path starts with that folder's followed by '/'); a set
        /// of one or none comes back as given. The paths are sorted before the '/' is added, so
        /// "Assets/A" comes before "Assets/A 1", and then "Assets/A/Sub" is kept, as in the
        /// Editor.</summary>
        public static string[] GetBaseFolders(string[] folders)
        {
            if (folders.Length <= 1) return folders;
            var sorted = new List<string>(folders.Length);
            foreach (var folder in folders) sorted.Add(folder.Trim('/'));
            sorted.Sort();
            for (int i = 0; i < sorted.Count; i++) sorted[i] += "/";
            var bases = new List<string> { sorted[0] };
            foreach (var folder in sorted)
                if (!folder.StartsWith(bases[bases.Count - 1], StringComparison.Ordinal)) bases.Add(folder);
            for (int i = 0; i < bases.Count; i++) bases[i] = bases[i].Trim('/');
            return bases.ToArray();
        }
    }
}

namespace UnityEditor.ProjectWindowCallback
{
    /// <summary>What a Project window name edit does once it ends. A new one is hidden and not
    /// saved; cleaning up destroys it.</summary>
    public abstract partial class EndNameEditAction : ScriptableObject
    {
        protected EndNameEditAction() { }

        public virtual void OnEnable() => hideFlags = HideFlags.HideAndDontSave;

        public abstract void Action(int instanceId, string pathName, string resourceFile);

        [Unbound.EditorFaithful("the base end action does nothing when a name edit is cancelled")]
        public virtual void Cancelled(int instanceId, string pathName, string resourceFile) { }

        public virtual void CleanUp() => DestroyImmediate(this);
    }
}
