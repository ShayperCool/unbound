using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Probe.InitOrder
{
    /// <summary>A package's editor initializer, as packages set up the state their postprocessors use.</summary>
    [InitializeOnLoad]
    public static class PackageInit
    {
        public static bool Ready;

        static PackageInit()
        {
            Ready = true;
            Log("package.ctor");
        }

        /// <summary>Appends an event of the Play mode domain load to the probe's log.</summary>
        public static void Log(string line)
        {
            var directory = Environment.GetEnvironmentVariable("INITIALIZE_ORDER_OUT");
            if (string.IsNullOrEmpty(directory) || !EditorApplication.isPlayingOrWillChangePlaymode) return;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "events.log"), line + "\n");
        }
    }

    /// <summary>A package's sprite postprocessor that needs its initializer to have run.</summary>
    public class PackageSpritePostprocessor : AssetPostprocessor
    {
        void OnPostprocessSprites(Texture2D texture, Sprite[] sprites) =>
            PackageInit.Log("postprocessSprites " + Path.GetFileName(assetPath) + " packageReady=" + PackageInit.Ready);

        // The imported paths sorted: their order in the call is the Editor's own.
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload)
        {
            var names = Array.ConvertAll(imported, Path.GetFileName);
            Array.Sort(names, StringComparer.Ordinal);
            PackageInit.Log("postprocessAllAssets imported=" + string.Join(";", names) + " reload=" + didDomainReload);
        }
    }
}
