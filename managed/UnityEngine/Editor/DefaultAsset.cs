using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The placeholder asset the Editor loads for folders, unrecognized/unclaimed files,
    /// extensionless files, compiler response/trigger files, files under
    /// <c>Assets/StreamingAssets/</c>, <c>Assets/WebGLTemplates/</c>, and
    /// <c>Assets/WebPlayerTemplates/</c>, and precompiled plugins: local file ID 102900000
    /// (matches the Unity 6000.0.71f1 Editor, tests/projects/defaultasset and
    /// tests/projects/spriteatlaseditor).
    /// </summary>
    public partial class DefaultAsset : UnityEngine.Object
    {
        internal const long FileId = 102900000;
        internal const long FolderFileId = FileId;

        private protected DefaultAsset() { }

        internal static DefaultAsset Create(string name, HideFlags hideFlags = HideFlags.NotEditable)
        {
            using (ObjectRegistry.AsAssets())
                return new DefaultAsset { name = name, IsPersistentAsset = true, hideFlags = hideFlags };
        }

        internal static DefaultAsset ForFolder(string name) => Create(name, HideFlags.NotEditable);
    }
}
