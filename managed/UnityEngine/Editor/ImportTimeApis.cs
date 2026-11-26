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

    /// <summary>A compute shader's importer, made for every compute shader the project imports.</summary>
    public sealed partial class ComputeShaderImporter : AssetImporter
    {
        public ComputeShaderImporter() { }
    }
}

namespace UnityEditor.Presets
{
    /// <summary>The project's preset settings (ProjectSettings/PresetManager.asset): read as an
    /// object when the project loads. Its default presets are not applied to new imports or objects
    /// yet: a project that sets some is told so when the settings load.</summary>
    internal partial class PresetManager : UnityEditorInternal.ProjectSettingsBase
    {
        public PresetManager() { }

        internal const string DefaultPresetsNotice = "PresetManager default presets";

        /// <summary>The settings file was read: says when it sets default presets, which Unbound
        /// does not apply, in the layout of serializedVersion 2 (<c>m_DefaultPresets</c>) or the one
        /// before (<c>m_DefaultList</c>, which projects of this release still hold).</summary>
        internal static void Loaded(Unbound.Runtime.YText body)
        {
            if (Sets(body?.Get("m_DefaultPresets")) || Sets(body?.Get("m_DefaultList")))
                Unbound.Runtime.Stubs.Ignored(DefaultPresetsNotice, "are not applied to new imports or objects yet");
        }

        static bool Sets(Unbound.Runtime.YText list) => list != null && (list.Keys.Count > 0 || list.Items.Count > 0);
    }
}
