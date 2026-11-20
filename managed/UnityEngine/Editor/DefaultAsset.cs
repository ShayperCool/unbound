using Unbound.Runtime;

namespace UnityEditor
{
    /// <summary>
    /// The asset a folder loads as: named after the folder, at file ID 102900000 (what references to
    /// a folder, such as a sprite atlas's folder packable, name). Matches the Unity 6000.0.71f1
    /// Editor (tests/projects/spriteatlaseditor: a folder packable answers its DefaultAsset).
    /// </summary>
    public partial class DefaultAsset : UnityEngine.Object
    {
        internal const long FolderFileId = 102900000;

        private protected DefaultAsset() { }

        internal static DefaultAsset ForFolder(string name)
        {
            using (ObjectRegistry.AsAssets())
                return new DefaultAsset { name = name, IsPersistentAsset = true };
        }
    }
}
