namespace UnityEditorInternal
{
    /// <summary>A package's package.json as the asset database loads it: a TextAsset of the
    /// file's text, named "package" (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/packagefolders builtinFile.*, registryManifest.*).</summary>
    public sealed partial class PackageManifest : UnityEngine.TextAsset
    {
        /// <summary>The local file ID of the manifest's object in its file, the same in every
        /// package (tests/projects/packagefolders *.localId).</summary>
        internal const long FileId = 8385607101436470782;

        internal PackageManifest(string file, string name, long size) : base(file, name, size) { }
    }
}
