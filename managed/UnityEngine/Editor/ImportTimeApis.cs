using System.Collections.Generic;

namespace UnityEditor
{
    public static partial class ArrayUtility
    {
        /// <summary>Whether the array holds the item (Unity's own C#, Editor/Mono/ArrayUtility.cs).</summary>
        public static bool Contains<T>(T[] array, T item)
        {
            List<T> list = new List<T>(array);
            return list.Contains(item);
        }
    }

    /// <summary>A font file's importer. Its settings are not read yet (they say so when used); an
    /// importer is made for every font the project imports.</summary>
    public sealed partial class TrueTypeFontImporter : AssetImporter
    {
        public TrueTypeFontImporter() { }
    }
}

namespace UnityEditor.Presets
{
    /// <summary>The project's preset settings (ProjectSettings/PresetManager.asset): read as an
    /// object when the project loads; the default presets are not applied yet (AddPresetType says so).</summary>
    internal partial class PresetManager : UnityEditorInternal.ProjectSettingsBase
    {
        public PresetManager() { }
    }
}
