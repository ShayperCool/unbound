using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The terrain layer checks and tiling fields render pipelines' terrain shader GUIs draw, as
    /// Unity's own C# writes them: a missing diffuse texture, a texture that does not repeat or has
    /// no mip maps, and a normal map not imported as one each draw a warning box; the tiling
    /// settings are the layer's size and offset fields. The layer selection grid and the layer's
    /// inspector need the terrain inspectors, which Unbound does not have (still stubs).
    /// </summary>
    public static partial class TerrainLayerUtility
    {
        sealed class Styles
        {
            internal readonly GUIContent textureAssign = EditorGUIUtility.TrTextContent("Assign a tiling texture", EditorGUIUtility.GetHelpIcon(MessageType.Warning));
            internal readonly GUIContent textureMustHaveRepeatWrapMode = EditorGUIUtility.TrTextContent("Texture wrap mode must be set to Repeat", EditorGUIUtility.GetHelpIcon(MessageType.Warning));
            internal readonly GUIContent textureMustHaveMips = EditorGUIUtility.TrTextContent("Texture must have mip maps", EditorGUIUtility.GetHelpIcon(MessageType.Warning));
            internal readonly GUIContent normalMapIncorrectTextureType = EditorGUIUtility.TrTextContent("Normal texture should be imported as Normal Map", EditorGUIUtility.GetHelpIcon(MessageType.Warning));
            internal readonly GUIContent tilingSettings = EditorGUIUtility.TrTextContent("Tiling Settings");
            internal readonly GUIContent tilingSize = EditorGUIUtility.TrTextContent("Size");
            internal readonly GUIContent tilingOffset = EditorGUIUtility.TrTextContent("Offset");
        }

        static Styles s_Styles;
        static Styles styles => s_Styles ??= new Styles();

        public static void ValidateDiffuseTextureUI(Texture2D texture)
        {
            if (texture == null) EditorGUILayout.HelpBox(styles.textureAssign);
            if (texture != null && texture.wrapMode != TextureWrapMode.Repeat) EditorGUILayout.HelpBox(styles.textureMustHaveRepeatWrapMode);
            if (texture != null && texture.mipmapCount <= 1) EditorGUILayout.HelpBox(styles.textureMustHaveMips);
        }

        /// <summary>Whether the texture is imported as a normal map; a texture that is no asset
        /// counts as one.</summary>
        public static bool CheckNormalMapTextureType(Texture2D texture)
        {
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath)) return true;
            return AssetImporter.GetAtPath(assetPath) is TextureImporter importer && importer.textureType == TextureImporterType.NormalMap;
        }

        public static void ValidateNormalMapTextureUI(Texture2D texture, bool normalMapTextureType)
        {
            if (texture != null && texture.wrapMode != TextureWrapMode.Repeat) EditorGUILayout.HelpBox(styles.textureMustHaveRepeatWrapMode);
            if (texture != null && texture.mipmapCount <= 1) EditorGUILayout.HelpBox(styles.textureMustHaveMips);
            if (!normalMapTextureType) EditorGUILayout.HelpBox(styles.normalMapIncorrectTextureType);
        }

        public static void ValidateMaskMapTextureUI(Texture2D texture)
        {
            if (texture != null && texture.wrapMode != TextureWrapMode.Repeat) EditorGUILayout.HelpBox(styles.textureMustHaveRepeatWrapMode);
            if (texture != null && texture.mipmapCount <= 1) EditorGUILayout.HelpBox(styles.textureMustHaveMips);
        }

        public static void TilingSettingsUI(TerrainLayer terrainLayer)
        {
            GUILayout.Label(styles.tilingSettings, EditorStyles.boldLabel);
            terrainLayer.tileSize = EditorGUILayout.Vector2Field(styles.tilingSize, terrainLayer.tileSize);
            terrainLayer.tileOffset = EditorGUILayout.Vector2Field(styles.tilingOffset, terrainLayer.tileOffset);
        }

        public static void TilingSettingsUI(SerializedProperty tileSize, SerializedProperty tileOffset)
        {
            GUILayout.Label(styles.tilingSettings, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(tileSize, styles.tilingSize);
            EditorGUILayout.PropertyField(tileOffset, styles.tilingOffset);
        }
    }
}
