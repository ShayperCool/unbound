using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;

/// <summary>A `.probecompute` file is compute shader source an importer turns into a ComputeShader
/// with <see cref="ShaderUtil.CreateComputeShaderAsset"/>, as Shader Graph and the unified ray
/// tracing importer do.</summary>
[ScriptedImporter(1, "probecompute")]
public class ProbeComputeImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var source = File.ReadAllText(ctx.assetPath);
        var shader = ShaderUtil.CreateComputeShaderAsset(ctx, "#define PROBE_SCALE 2\n" + source);
        ctx.AddObjectToAsset("ComputeShader", shader);
        ctx.SetMainObject(shader);
    }
}
