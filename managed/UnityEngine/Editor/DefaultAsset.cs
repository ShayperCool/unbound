using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The object the Editor loads for an asset it imports to no objects of its own: a folder, a
    /// file of no importer's kind or under a special root folder (DefaultImporter), a plug-in
    /// (PluginImporter). Matches the Unity 6000.0.71f1 Editor, tests/projects/defaultasset and
    /// tests/projects/spriteatlaseditor.
    /// </summary>
    public partial class DefaultAsset : UnityEngine.Object
    {
        /// <summary>The file ID of the object standing in for such an asset, a DefaultAsset or a
        /// scene's SceneAsset.</summary>
        internal const long FileId = 102900000;

        private protected DefaultAsset() { }

        /// <summary>The message its inspector shows: none for any asset Unbound imports
        /// (tests/projects/defaultasset inspector.*).</summary>
        internal string message => "";

        /// <summary>Whether the inspector message is a warning.</summary>
        internal bool isWarning => false;

        internal static DefaultAsset Create(string name, HideFlags hideFlags)
        {
            using (ObjectRegistry.AsAssets())
                return new DefaultAsset { name = name, IsPersistentAsset = true, hideFlags = hideFlags };
        }
    }
}
