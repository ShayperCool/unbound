namespace UnityEngine
{
    public sealed partial class QualitySettings
    {
        /// <summary>The project's QualitySettings object: the object of ProjectSettings/QualitySettings.asset
        /// that AssetDatabase and Unsupported.GetSerializedAssetInterfaceSingleton give, whose
        /// SerializedObject lists the quality levels. Matches the Unity 6000.0.71f1 Editor
        /// (tests/projects/coreremainder).</summary>
        public static Object GetQualitySettings() => Unbound.Runtime.SettingsAssets.Singleton("QualitySettings");
    }
}

namespace UnityEngine.Rendering
{
    public sealed partial class GraphicsSettings
    {
        /// <summary>The project's GraphicsSettings object, as <see cref="QualitySettings.GetQualitySettings"/>.</summary>
        public static Object GetGraphicsSettings() => Unbound.Runtime.SettingsAssets.Singleton("GraphicsSettings");
    }
}
