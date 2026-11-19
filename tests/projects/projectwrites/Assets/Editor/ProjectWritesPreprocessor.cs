using UnityEditor;

/// <summary>Changes the settings of textures named Pre* while they are imported, as a game's
/// postprocessor does; the probe records what the Editor keeps of that in their .meta files.</summary>
public class ProjectWritesPreprocessor : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!System.IO.Path.GetFileName(assetPath).StartsWith("Pre")) return;
        var importer = (TextureImporter)assetImporter;
        importer.maxTextureSize = 64;
        importer.isReadable = true;
    }
}
