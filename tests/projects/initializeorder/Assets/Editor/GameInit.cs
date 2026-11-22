using System.IO;
using System.Linq;
using System.Text;
using Probe.InitOrder;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The project's editor initializer, as a project's toolbar extension finds its settings asset:
/// in its static constructor it searches assets by type, loads a settings asset that references a
/// sprite and forces a sprite's import (and one each from [InitializeOnLoadMethod] and
/// [DidReloadScripts]), logging what ran in between. In Play mode it then writes
/// the order of the domain load's initializers and the events around its own.
/// </summary>
[InitializeOnLoad]
public static class GameInit
{
    const int Frames = 60;
    static int s_Updates;

    static GameInit()
    {
        PackageInit.Log("game.ctor packageReady=" + PackageInit.Ready);
        PackageInit.Log("game.findAssets n=" + AssetDatabase.FindAssets("t:Holder").Length);
        var holder = AssetDatabase.LoadAssetAtPath<Holder>("Assets/Data/Holder.asset");
        PackageInit.Log("game.load sprite=" + (holder == null ? "<no asset>" : holder.sprite == null ? "<null>" : holder.sprite.name));
        AssetDatabase.ImportAsset("Assets/Sprites/Forced.png", ImportAssetOptions.ForceUpdate);
        PackageInit.Log("game.importReturned");
        AssemblyReloadEvents.afterAssemblyReload += () => PackageInit.Log("afterAssemblyReload");
        EditorApplication.update += Update;
    }

    [InitializeOnLoadMethod]
    static void LoadMethod()
    {
        AssetDatabase.ImportAsset("Assets/Sprites/Method.png", ImportAssetOptions.ForceUpdate);
        PackageInit.Log("game.loadMethodImportReturned");
    }

    [UnityEditor.Callbacks.DidReloadScripts]
    static void Reloaded()
    {
        AssetDatabase.ImportAsset("Assets/Sprites/Reload.png", ImportAssetOptions.ForceUpdate);
        PackageInit.Log("game.didReloadImportReturned");
    }

    static void Update()
    {
        if (!EditorApplication.isPlaying) return;
        if (++s_Updates == 1) PackageInit.Log("firstUpdate");
        if (s_Updates < Frames) return;
        EditorApplication.update -= Update;
        PackageInit.Log("update" + Frames);
        var directory = System.Environment.GetEnvironmentVariable("INITIALIZE_ORDER_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var events = File.ReadAllLines(Path.Combine(directory, "events.log"));
        int Index(string prefix) => System.Array.FindIndex(events, e => e.StartsWith(prefix, System.StringComparison.Ordinal));
        var rows = new StringBuilder();
        void Put(string key, string value) => rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
        // Each initializer once, in order (the game's assembly uses the package's, which the
        // asmdef assemblies use; the Aa/Zz "Unused" ones no assembly uses).
        Put("order.initializers", string.Join(",", events.Where(e => e.Contains(".ctor")).Select(e => e.Split(' ')[0])));
        // The game initializer's events and every postprocessor call, in order: while the domain
        // loads, and from afterAssemblyReload on; the postprocessing of the sprite the settings
        // asset references (loaded, never imported, in the load) has a row of its own.
        int start = Index("game.ctor"), loaded = Index("afterAssemblyReload");
        bool OfLoaded(string e) => e.StartsWith("postprocessSprites Loaded.png", System.StringComparison.Ordinal);
        Put("game.duringLoad", string.Join(",", events.Skip(start).Take(loaded - start).Where(e => !OfLoaded(e))));
        Put("game.afterLoad", string.Join(",", events.Skip(loaded).Where(e => !OfLoaded(e))));
        Put("loaded.postprocess", string.Join(",", events.Select((e, i) => (e, i)).Where(x => OfLoaded(x.e)).Select(x => x.i < loaded ? "duringLoad" : "afterLoad")));
        rows.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), rows.ToString());
        EditorApplication.Exit(0);
    }
}
