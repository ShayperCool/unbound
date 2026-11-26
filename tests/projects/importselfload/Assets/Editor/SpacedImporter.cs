using UnityEditor.AssetImporters;
using UnityEngine;

namespace ProbeImporters
{
    /// <summary>A second importer of the .ovr2 override extension, in a namespace: imports .spc
    /// files, and .ovr2 files given it as their override, as a TextAsset named "spaced".</summary>
    [ScriptedImporter(1, new[] { "spc" }, new[] { "ovr2" })]
    public class SpacedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = new TextAsset("spaced") { name = "spaced" };
            ctx.AddObjectToAsset("text", text);
            ctx.SetMainObject(text);
        }
    }
}
