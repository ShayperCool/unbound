using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEditor
{
    /// <summary>A scene's baked lighting data as Lightmapping.lightingDataAsset names it: one
    /// object per asset, named as its file (the built-in one "LightingData", measured: Unity
    /// 6000.0.71f1 Editor, tests/projects/tiersettings). Its contents are read by the renderer
    /// (RenderSettings.ReadLightingData), not through this object.</summary>
    [UnityEngine.ExcludeFromPreset]
    public sealed partial class LightingDataAsset : UnityEngine.Object
    {
        /// <summary>The name of the built-in Lighting Data asset.</summary>
        const string BuiltinName = "LightingData";
        /// <summary>The GUID of the built-in extra resources, where the built-in one is.</summary>
        const string BuiltinExtraGuid = "0000000000000000f000000000000000";

        static readonly Dictionary<(string Guid, long FileId), LightingDataAsset> s_Assets = new Dictionary<(string, long), LightingDataAsset>();

        LightingDataAsset(string name)
        {
            IsPersistentAsset = true;
            m_Name = name;
        }

        /// <summary>The object of a scene's m_LightingDataAsset reference; null for none.</summary>
        internal static LightingDataAsset Of(ObjectRef reference)
        {
            if (reference.IsNull || string.IsNullOrEmpty(reference.Guid)) return null;
            var key = (reference.Guid, reference.FileId);
            if (s_Assets.TryGetValue(key, out var asset) && asset != null) return asset;
            string name = reference.Guid == BuiltinExtraGuid
                ? BuiltinName
                : System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(reference.Guid));
            using (ObjectRegistry.AsAssets())
                asset = new LightingDataAsset(name);
            s_Assets[key] = asset;
            return asset;
        }
    }
}
