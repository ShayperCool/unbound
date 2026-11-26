using Unity.Collections;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>Imports a .gen file as a 4x4 texture made with TextureGenerator, its postprocessors run;
/// notes what loading the asset gives from OnImportAsset.</summary>
[ScriptedImporter(1, "gen")]
public class GenImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        SelfLoadPostprocessor.Note("import " + ctx.assetPath + " | " + SelfLoadPostprocessor.Loads(ctx.assetPath));
        var settings = new TextureGenerationSettings(TextureImporterType.Default);
        settings.assetPath = ctx.assetPath;
        settings.enablePostProcessor = true;
        settings.sourceTextureInformation = new SourceTextureInformation { width = 4, height = 4, containsAlpha = true, hdr = false };
        using var pixels = new NativeArray<Color32>(16, Allocator.Temp);
        var output = TextureGenerator.GenerateTexture(settings, pixels);
        ctx.AddObjectToAsset("texture", output.texture);
        ctx.SetMainObject(output.texture);
    }
}
