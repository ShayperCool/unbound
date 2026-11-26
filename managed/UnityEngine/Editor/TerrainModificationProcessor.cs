namespace UnityEditor
{
    /// <summary>
    /// The terrain editor's save hook (Unity's published C#, TerrainEditorUtility.cs): before a
    /// save it applies the terrain paint operations it delayed, then saves every path it is given.
    /// Unbound has no PaintContext (its members are not implemented and say so), so no paint
    /// operation is ever delayed: the hook only returns the paths.
    /// </summary>
    internal partial class TerrainModificationProcessor : AssetModificationProcessor
    {
        public static string[] OnWillSaveAssets(string[] paths) => paths;
    }
}
