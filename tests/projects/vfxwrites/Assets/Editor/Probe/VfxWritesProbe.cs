using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// What the Editor writes into a Visual Effect Graph's file, and when (see README.md): the graph
/// is imported, written unchanged, its resource settings set one by one (each followed by
/// AssetDatabase.SaveAssets), written again, one of its blocks edited and written with its
/// sub-assets, imported again, and a new graph created. Rows go to values.jsonl in
/// $VFX_WRITES_OUT: each step's dirtiness and the file's changes since the step before
/// (removed and added lines).
/// </summary>
public static class VfxWritesProbe
{
    const string Graph = "Assets/Graphs/BoundsAuto.vfx";
    const string Created = "Assets/Graphs/Created.vfx";
    static readonly StringBuilder s_Out = new StringBuilder();
    static string s_Last;

    static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c == '\r') b.Append("\\r");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.ToString();
    }

    static void Row(string key, object value) => s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value?.ToString() ?? "<null>")).Append("\"}\n");

    /// <summary>The file's lines removed and added since the step before ("-" and "+", in file
    /// order), or "unchanged".</summary>
    static void Changes(string step, string path)
    {
        var text = File.Exists(path) ? File.ReadAllText(path) : null;
        if (text == null) { Row(step + "/file", "missing"); s_Last = null; return; }
        if (s_Last == null) { Row(step + "/file", "lines " + text.Split('\n').Length); s_Last = text; return; }
        if (text == s_Last) { Row(step + "/file", "unchanged"); return; }
        var before = s_Last.Split('\n');
        var after = text.Split('\n');
        var removed = before.Except(after).ToList();
        var added = after.Except(before).ToList();
        var b = new StringBuilder();
        foreach (var l in removed) b.Append("- ").Append(l).Append('\n');
        foreach (var l in added) b.Append("+ ").Append(l).Append('\n');
        b.Append("lines ").Append(before.Length).Append(" -> ").Append(after.Length);
        Row(step + "/file", b.ToString());
        s_Last = text;
    }

    /// <summary>The documents of <paramref name="after"/> whose header line (type and file id)
    /// <paramref name="before"/> does not have, whole and in file order.</summary>
    static string NewDocuments(string before, string after)
    {
        var headers = new HashSet<string>(before.Split('\n').Where(l => l.StartsWith("--- ", StringComparison.Ordinal)));
        var b = new StringBuilder();
        bool copying = false;
        foreach (var line in after.Split('\n'))
        {
            if (line.StartsWith("--- ", StringComparison.Ordinal)) copying = !headers.Contains(line);
            if (copying) b.Append(line).Append('\n');
        }
        return b.ToString();
    }

    static void Step(string step, VisualEffectResource resource, Action act)
    {
        VfxWritesSaveLog.Calls.Clear();
        act();
        Row(step + "/dirty", resource != null && EditorUtility.IsDirty(resource));
        if (VfxWritesSaveLog.Calls.Count > 0) Row(step + "/will save", string.Join(" | ", VfxWritesSaveLog.Calls));
        Changes(step, Graph);
    }

    public static void Record()
    {
        try
        {
            Changes("open", Graph);
            AssetDatabase.ImportAsset(Graph, ImportAssetOptions.ForceUpdate);
            var resource = VisualEffectResource.GetResourceAtPath(Graph);
            Row("import/dirty", EditorUtility.IsDirty(resource));
            Changes("import", Graph);
            Row("import/contents", string.Join(", ", resource.GetContents().Select(o => o == null ? "null" : o.GetType().Name).GroupBy(n => n).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " " + g.Count())));
            Step("write unchanged", resource, () => resource.WriteAsset());

            // Each resource setting, then a save of the dirty assets.
            Step("initialEventName", resource, () => resource.initialEventName = "ProbeEvent");
            Step("initialEventName save", resource, () => AssetDatabase.SaveAssets());
            Step("updateMode", resource, () => resource.updateMode = VFXUpdateMode.DeltaTime);
            Step("updateMode save", resource, () => AssetDatabase.SaveAssets());
            Step("preWarmDeltaTime", resource, () => resource.preWarmDeltaTime = 0.125f);
            Step("preWarmDeltaTime save", resource, () => AssetDatabase.SaveAssets());
            Step("preWarmStepCount", resource, () => resource.preWarmStepCount = 7u);
            Step("preWarmStepCount save", resource, () => AssetDatabase.SaveAssets());
            Step("cullingFlags", resource, () => resource.cullingFlags = VFXCullingFlags.CullNone);
            Step("cullingFlags save", resource, () => AssetDatabase.SaveAssets());
            Step("instancingMode", resource, () => resource.instancingMode = VFXInstancingMode.Disabled);
            Step("instancingMode save", resource, () => AssetDatabase.SaveAssets());
            Step("instancingCapacity", resource, () => resource.instancingCapacity = 33u);
            Step("instancingCapacity save", resource, () => AssetDatabase.SaveAssets());
            Step("write after saves", resource, () => resource.WriteAsset());

            // A block's input edited through the graph, then written with its sub-assets.
            var graph = resource.GetOrCreateGraph();
            var spawner = graph.children.OfType<VFXBasicSpawner>().First();
            var burst = spawner.children.First();
            var count = burst.inputSlots.First(s => s.name == "Count");
            Step("block edit", resource, () => count.value = 9f);
            Row("block edit/slot dirty", EditorUtility.IsDirty(count));
            Step("block edit write", resource, () => resource.WriteAssetWithSubAssets());
            Step("reimport", resource, () => AssetDatabase.ImportAsset(Graph, ImportAssetOptions.ForceUpdate));

            // A block added to the initialize context, written with its sub-assets (the graph
            // gains the block and its two slots), then removed and written again.
            resource = VisualEffectResource.GetResourceAtPath(Graph);
            graph = resource.GetOrCreateGraph();
            var init = graph.children.OfType<VFXBasicInitialize>().First();
            var added = ScriptableObject.CreateInstance<UnityEditor.VFX.Block.SetAttribute>();
            added.SetSettingValue("attribute", "lifetime");
            Step("block add", resource, () => init.AddChild(added));
            var beforeAdd = File.ReadAllText(Graph);
            Step("block add write", resource, () => resource.WriteAssetWithSubAssets());
            Row("block add write/new documents", NewDocuments(beforeAdd, File.ReadAllText(Graph)));
            Row("block add write/contents", resource.GetContents().Length);
            Step("block remove", resource, () => init.RemoveChild(added));
            Step("block remove write", resource, () => resource.WriteAssetWithSubAssets());
            Row("block remove write/contents", resource.GetContents().Length);

            // A new graph.
            VisualEffectAssetEditorUtility.CreateNewAsset(Created);
            s_Last = null;
            Changes("create", Created);
            var meta = File.Exists(Created + ".meta") ? File.ReadAllText(Created + ".meta") : null;
            Row("create/meta", meta == null ? "missing" : string.Join("\n", meta.Split('\n').Where(l => !l.StartsWith("guid: ", StringComparison.Ordinal))));
            Row("create/text", File.ReadAllText(Created));
            VfxWritesSaveLog.Calls.Clear();
            AssetDatabase.SaveAssets();
            if (VfxWritesSaveLog.Calls.Count > 0) Row("create save/will save", string.Join(" | ", VfxWritesSaveLog.Calls));
            Changes("create save", Created);

            // The new graph built: an operator added to it, written with its sub-assets (the
            // file held the package's skeleton).
            var created = VisualEffectResource.GetResourceAtPath(Created);
            created.GetOrCreateGraph().AddChild(ScriptableObject.CreateInstance<UnityEditor.VFX.Operator.Add>());
            created.WriteAssetWithSubAssets();
            Row("fresh write/text", File.ReadAllText(Created));

            // Subgraphs: an operator subgraph and a block subgraph created, the operator subgraph
            // built as the graph above and then a setting of it saved.
            const string op = "Assets/Graphs/Op.vfxoperator", blocks = "Assets/Graphs/Blocks.vfxblock";
            VisualEffectAssetEditorUtility.CreateNew<VisualEffectSubgraphOperator>(op);
            VisualEffectAssetEditorUtility.CreateNew<VisualEffectSubgraphBlock>(blocks);
            foreach (var path in new[] { op, blocks })
            {
                Row("subgraph create/" + Path.GetExtension(path) + "/text", File.ReadAllText(path));
                var subMeta = File.Exists(path + ".meta") ? File.ReadAllText(path + ".meta") : null;
                Row("subgraph create/" + Path.GetExtension(path) + "/meta", subMeta == null ? "missing" : string.Join("\n", subMeta.Split('\n').Where(l => !l.StartsWith("guid: ", StringComparison.Ordinal))));
            }
            var subgraph = VisualEffectResource.GetResourceAtPath(op);
            subgraph.GetOrCreateGraph().AddChild(ScriptableObject.CreateInstance<UnityEditor.VFX.Operator.Add>());
            subgraph.WriteAssetWithSubAssets();
            Row("subgraph write/text", File.ReadAllText(op));
            subgraph.initialEventName = "SubgraphEvent";
            Row("subgraph setting/dirty", EditorUtility.IsDirty(subgraph));
            VfxWritesSaveLog.Calls.Clear();
            AssetDatabase.SaveAssets();
            Row("subgraph setting save/will save", string.Join(" | ", VfxWritesSaveLog.Calls));
            Row("subgraph setting save/text", File.ReadAllText(op));

            // AssetDatabase.SaveAssetIfDirty of the graph's resource and of its asset (which is not
            // dirty itself), each after a setting; then a setting left unsaved for the Editor's
            // exit (run.py reads the file after the run: exit/initialEventName).
            s_Last = File.ReadAllText(Graph);
            Step("save if dirty resource", resource, () => { resource.initialEventName = "IfDirtyResource"; AssetDatabase.SaveAssetIfDirty(resource); });
            Step("save if dirty asset", resource, () => { resource.initialEventName = "IfDirtyAsset"; AssetDatabase.SaveAssetIfDirty(resource.asset); });

            // A model edited (a block's input set through the graph, which leaves the resource
            // clean), then AssetDatabase.SaveAssetIfDirty of the asset, the resource and the graph
            // model in turn, then SaveAssets; the model's dirty flag after each.
            graph = resource.GetOrCreateGraph();
            var countNow = graph.children.OfType<VFXBasicSpawner>().First().children.First().inputSlots.First(s => s.name == "Count");
            Step("model edit", resource, () => countNow.value = 4f);
            Row("model edit/graph dirty", EditorUtility.IsDirty(graph));
            Row("model edit/asset dirty", EditorUtility.IsDirty(resource.asset));
            Step("model save if dirty asset", resource, () => AssetDatabase.SaveAssetIfDirty(resource.asset));
            Row("model save if dirty asset/graph dirty", EditorUtility.IsDirty(graph));
            Step("model save if dirty resource", resource, () => AssetDatabase.SaveAssetIfDirty(resource));
            Row("model save if dirty resource/graph dirty", EditorUtility.IsDirty(graph));
            Step("model save if dirty graph", resource, () => AssetDatabase.SaveAssetIfDirty(graph));
            Row("model save if dirty graph/graph dirty", EditorUtility.IsDirty(graph));
            Step("model save assets", resource, () => AssetDatabase.SaveAssets());
            Row("model save assets/graph dirty", EditorUtility.IsDirty(graph));

            // SaveAssetIfDirty of an asset that is not a graph: which paths the processor gets.
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, "Assets/Probe.mat");
            material.renderQueue = 2100;
            Row("material save if dirty/dirty before", EditorUtility.IsDirty(material));
            VfxWritesSaveLog.Calls.Clear();
            AssetDatabase.SaveAssetIfDirty(material);
            Row("material save if dirty/will save", string.Join(" | ", VfxWritesSaveLog.Calls));
            Row("material save if dirty/dirty", EditorUtility.IsDirty(material));

            // The created graph's model edited (an operator added) and left for the exit; run.py
            // counts the file's documents after the run (exit/created documents).
            var createdGraph = VisualEffectResource.GetResourceAtPath(Created).GetOrCreateGraph();
            createdGraph.AddChild(ScriptableObject.CreateInstance<UnityEditor.VFX.Operator.Add>());
            Row("exit/created graph dirty before", EditorUtility.IsDirty(createdGraph));
            Row("exit/created resource dirty before", EditorUtility.IsDirty(VisualEffectResource.GetResourceAtPath(Created)));
            Row("exit/created documents before", File.ReadAllText(Created).Split('\n').Count(l => l.StartsWith("--- ", StringComparison.Ordinal)));
            resource.initialEventName = "AtExit";
            Row("exit/dirty before", EditorUtility.IsDirty(resource));
            s_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        }
        catch (Exception e)
        {
            Row("failed", e.GetType().Name + ": " + e.Message);
            Debug.LogError("vfxwrites probe failed: " + e);
        }
        var dir = Environment.GetEnvironmentVariable("VFX_WRITES_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), s_Out.ToString());
    }

    /// <summary>The Editor's entry (batch mode, -executeMethod): records, then quits.</summary>
    public static void RecordAndExit()
    {
        Record();
        EditorApplication.Exit(0);
    }
}
