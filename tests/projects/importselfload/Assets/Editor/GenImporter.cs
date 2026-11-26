using Unity.Collections;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .gen file as a square texture made with TextureGenerator, its postprocessors
/// run, 4 pixels wide more at each import of the file (counted in imports.txt, which outlives the
/// domain); notes what loading the asset gives from OnImportAsset (not for an asset in Assets/Quiet/).</summary>
[ScriptedImporter(1, "gen")]
public class GenImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        int size = 4 * (1 + SelfLoadPostprocessor.Count("import " + ctx.assetPath + " | "));
        SelfLoadPostprocessor.Note("import " + ctx.assetPath + " | " + SelfLoadPostprocessor.Observe(ctx.assetPath));
        var settings = new TextureGenerationSettings(TextureImporterType.Default);
        settings.assetPath = ctx.assetPath;
        settings.enablePostProcessor = true;
        settings.sourceTextureInformation = new SourceTextureInformation { width = size, height = size, containsAlpha = true, hdr = false };
        using var pixels = new NativeArray<Color32>(size * size, Allocator.Temp);
        var output = TextureGenerator.GenerateTexture(settings, pixels);
        ctx.AddObjectToAsset("texture", output.texture);
        ctx.SetMainObject(output.texture);
    }
}
