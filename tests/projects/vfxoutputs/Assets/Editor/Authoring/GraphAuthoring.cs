using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.VFX;
using UnityEditor.VFX.Block;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Authors the probe's graphs and scene in the Editor, through the graph package's own models
/// (this assembly carries the name of one of the package's friend assemblies). The files it
/// writes are kept under Authored/ (tests/authored_project.py) and replayed by the engine.
/// </summary>
public static class GraphAuthoring
{
    public const string Folder = "Assets/OutputGraphs";
    public const string Scene = "Assets/Scenes/Probe.unity";
    const string WhitePath = Folder + "/White.png";

    public static void Author()
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(Path.GetDirectoryName(Scene));
        // A white image: quads of one flat colour (the package's default texture fades out).
        var white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        white.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
        File.WriteAllBytes(WhitePath, white.EncodeToPNG());
        AssetDatabase.Refresh();
        Indirect();
        Sorted();
        Prioritized();
        Primitives();
        Alike();
        Moving();
        GloballySorted();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildScene();
    }

    static T Make<T>() where T : VFXModel => ScriptableObject.CreateInstance<T>();

    static VFXGraph NewGraph(string name)
    {
        var asset = VisualEffectAssetEditorUtility.CreateNewAsset(Folder + "/" + name + ".vfx");
        return asset.GetResource().GetOrCreateGraph();
    }

    static void Save(VFXGraph graph)
    {
        var resource = graph.GetResource();
        resource.WriteAssetWithSubAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(resource), ImportAssetOptions.ForceUpdate);
    }

    static void Link(VFXSlot from, VFXSlot to)
    {
        if (!from.Link(to)) throw new InvalidOperationException("cannot link " + from.property.type.Name + " '" + from.name + "' to " + to.property.type.Name + " '" + to.name + "'");
    }

    static VFXSlot Input(VFXModel model, string name) => ((IVFXSlotContainer)model).inputSlots.First(s => s.name == name);

    /// <summary>A spawner, an initialize, an update and a quad output, linked; returns them.</summary>
    static (VFXBasicSpawner Spawn, VFXBasicInitialize Init, VFXPlanarPrimitiveOutput Output) System(VFXGraph graph, uint capacity)
    {
        var spawn = Make<VFXBasicSpawner>();
        var init = Make<VFXBasicInitialize>();
        var update = Make<VFXBasicUpdate>();
        var output = Make<VFXPlanarPrimitiveOutput>();
        graph.AddChild(spawn);
        graph.AddChild(init);
        graph.AddChild(update);
        graph.AddChild(output);
        spawn.LinkTo(init);
        init.LinkTo(update);
        update.LinkTo(output);
        init.GetData().SetSettingValue("capacity", capacity);
        return (spawn, init, output);
    }

    static void ConstantRate(VFXBasicSpawner spawn, float rate)
    {
        var block = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(block);
        block.inputSlots[0].value = rate;
    }

    static void Burst(VFXBasicSpawner spawn, float count)
    {
        var block = Make<VFXSpawnerBurst>();
        spawn.AddChild(block);
        block.SetSettingValue("repeat", VFXSpawnerBurst.RepeatMode.Single);
        block.SetSettingValue("spawnMode", VFXSpawnerBurst.RandomMode.Constant);
        block.SetSettingValue("delayMode", VFXSpawnerBurst.RandomMode.Constant);
        Input(block, "Count").value = count;
        Input(block, "Delay").value = 0f;
    }

    static void Set(VFXContext context, string attribute, object value)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        context.AddChild(block);
        block.inputSlots[0].value = value;
    }

    /// <summary>A vector as the slot takes it: a position and a vector have types of their own
    /// (a plain vector given to one of them is not taken).</summary>
    static object Spaced(VFXSlot slot, Vector3 value)
    {
        var type = slot.property.type;
        if (type == typeof(Position)) return (Position)value;
        if (type == typeof(Vector)) return (Vector)value;
        if (type == typeof(DirectionType)) return (DirectionType)value;
        return value;
    }

    static void SetVector(VFXContext context, string attribute, Vector3 value)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        context.AddChild(block);
        block.inputSlots[0].value = Spaced(block.inputSlots[0], value);
    }

    /// <summary>An attribute drawn for each particle, component by component, between two vectors.</summary>
    static void SetRandom(VFXContext context, string attribute, Vector3 from, Vector3 to)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        block.SetSettingValue("Random", RandomMode.PerComponent);
        context.AddChild(block);
        block.inputSlots[0].value = Spaced(block.inputSlots[0], from);
        block.inputSlots[1].value = Spaced(block.inputSlots[1], to);
    }

    /// <summary>A number drawn for each particle between two values.</summary>
    static void SetRandom(VFXContext context, string attribute, float from, float to)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        block.SetSettingValue("Random", RandomMode.Uniform);
        context.AddChild(block);
        block.inputSlots[0].value = from;
        block.inputSlots[1].value = to;
    }

    /// <summary>How an output finds the particles it draws (README.md, "Indirect outputs").</summary>
    enum Route { UpdateList, ComputeCulled, FrustumCulled }

    static void Routed(VFXPlanarPrimitiveOutput output, Route route, VFXAbstractRenderedOutput.BlendMode blend)
    {
        output.SetSettingValue("blendMode", blend);
        output.SetSettingValue("sort", VFXAbstractParticleOutput.SortActivationMode.Off);
        if (route == Route.UpdateList) output.SetSettingValue("indirectDraw", true);
        if (route == Route.ComputeCulled) output.SetSettingValue("computeCulling", true);
        if (route == Route.FrustumCulled) output.SetSettingValue("frustumCulling", true);
    }

    /// <summary>The graphs of the indirect outputs (README.md, "Indirect outputs"): unsorted
    /// quads drawn from a list of the particles to draw, made by the update kernel, by a
    /// culling pass once per step, or by a culling pass for each camera.</summary>
    static void Indirect()
    {
        foreach (Route route in Enum.GetValues(typeof(Route)))
        {
            string name = route.ToString();

            // Forty opaque quads at once, each with a place, a depth and a colour of its own.
            var still = NewGraph(name + "Still");
            var (stillSpawn, stillInit, stillOutput) = System(still, 64);
            Routed(stillOutput, route, VFXAbstractRenderedOutput.BlendMode.Opaque);
            Burst(stillSpawn, 40f);
            SetRandom(stillInit, "position", new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
            Set(stillInit, "size", 0.4f);
            SetRandom(stillInit, "color", new Vector3(0.2f, 0.2f, 0.2f), new Vector3(1f, 1f, 1f));
            Save(still);

            // Forty a second living between a fifth of a second and a second and a half: the
            // particles that died lie between those alive. The same again for an effect no
            // camera sees at first (an asset of its own: effects of one asset are batched).
            foreach (var variant in new[] { "Dying", "Away" })
            {
                var dying = NewGraph(name + variant);
                var (dyingSpawn, dyingInit, dyingOutput) = System(dying, 64);
                Routed(dyingOutput, route, VFXAbstractRenderedOutput.BlendMode.Opaque);
                ConstantRate(dyingSpawn, 40f);
                SetRandom(dyingInit, "position", new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
                SetRandom(dyingInit, "lifetime", 0.2f, 1.5f);
                Set(dyingInit, "size", 0.3f);
                SetRandom(dyingInit, "color", new Vector3(0.2f, 0.2f, 0.2f), new Vector3(1f, 1f, 1f));
                Save(dying);
            }

            // More quads than one draw's indices reach, added to what is behind.
            var many = NewGraph(name + "Many");
            var (manySpawn, manyInit, manyOutput) = System(many, 20000);
            Routed(manyOutput, route, VFXAbstractRenderedOutput.BlendMode.Additive);
            Burst(manySpawn, 20000f);
            SetRandom(manyInit, "position", new Vector3(-1.4f, -1.4f, 0f), new Vector3(1.4f, 1.4f, 0f));
            Set(manyInit, "size", 0.05f);
            SetVector(manyInit, "color", new Vector3(0.3f, 0.6f, 1f));
            Save(many);

            // Thirty flat quads of one colour at once over each other, added to what is behind:
            // the more of them overlap, the brighter (in any order), so a particle drawn twice
            // shows.
            var glow = NewGraph(name + "Glow");
            var (glowSpawn, glowInit, glowOutput) = System(glow, 32);
            Routed(glowOutput, route, VFXAbstractRenderedOutput.BlendMode.Additive);
            Input(glowOutput, "mainTexture").value = AssetDatabase.LoadAssetAtPath<Texture2D>(WhitePath);
            Burst(glowSpawn, 30f);
            SetRandom(glowInit, "position", new Vector3(-0.6f, -0.6f, -1f), new Vector3(0.6f, 0.6f, 1f));
            Set(glowInit, "size", 0.7f);
            SetVector(glowInit, "color", new Vector3(0.2f, 0.12f, 0.08f));
            Save(glow);

            // Still's particles again, for an effect made while the others play.
            var late = NewGraph(name + "Late");
            var (lateSpawn, lateInit, lateOutput) = System(late, 64);
            Routed(lateOutput, route, VFXAbstractRenderedOutput.BlendMode.Opaque);
            Burst(lateSpawn, 40f);
            SetRandom(lateInit, "position", new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
            Set(lateInit, "size", 0.4f);
            SetRandom(lateInit, "color", new Vector3(0.2f, 0.2f, 0.2f), new Vector3(1f, 1f, 1f));
            Save(late);
        }
    }

    /// <summary>Alpha-blended quads over each other (sorted by default), each of a colour of
    /// its own: the order they are drawn in shows.</summary>
    static VFXPlanarPrimitiveOutput Overlapping(VFXGraph graph, uint capacity, float count, float size, float spread)
    {
        var (spawn, init, output) = System(graph, capacity);
        output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
        Burst(spawn, count);
        SetRandom(init, "position", new Vector3(-spread, -spread, -1f), new Vector3(spread, spread, 1f));
        Set(init, "size", size);
        Set(init, "alpha", 0.6f);
        SetRandom(init, "color", new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
        return output;
    }

    /// <summary>The graphs of the sorted outputs (README.md, "Sorted outputs").</summary>
    static void Sorted()
    {
        void Graph(string name, VFXSortingUtility.SortCriteria mode, bool reverse = false)
        {
            var graph = NewGraph(name);
            var output = Overlapping(graph, 32, 30f, 0.7f, 0.6f);
            output.SetSettingValue("sortMode", mode);
            if (reverse) output.SetSettingValue("revertSorting", true);
            if (mode == VFXSortingUtility.SortCriteria.Custom) Input(output, "sortKey").value = 1f;
            Save(graph);
        }

        // A key of each particle's own, drawn at random (constant per particle): not the order
        // of the particles' indices.
        void RandomKey(VFXGraph graph, VFXPlanarPrimitiveOutput output)
        {
            output.SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.Custom);
            var random = Make<UnityEditor.VFX.Operator.Random>();
            graph.AddChild(random);
            Link(random.outputSlots[0], Input(output, "sortKey"));
        }
        Graph("SortDistance", VFXSortingUtility.SortCriteria.DistanceToCamera);
        Graph("SortDepth", VFXSortingUtility.SortCriteria.CameraDepth);
        Graph("SortReverse", VFXSortingUtility.SortCriteria.DistanceToCamera, true);
        // One key for all: the order of equal keys.
        Graph("SortCustom", VFXSortingUtility.SortCriteria.Custom);
        var keyed = NewGraph("SortKeyed");
        RandomKey(keyed, Overlapping(keyed, 32, 30f, 0.7f, 0.6f));
        Save(keyed);
        // Sorted at each step, more than two merges: 20000 small quads by a key of their own.
        var keyedMany = NewGraph("SortKeyedMany");
        RandomKey(keyedMany, Overlapping(keyedMany, 20000, 20000f, 0.08f, 1.4f));
        Save(keyedMany);

        // Thirty a second living between a third of a second and a second and a half: dead
        // particles' places are taken again, so the youngest are not the last places.
        var youngest = NewGraph("SortYoungest");
        var (spawn, init, output) = System(youngest, 64);
        output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
        output.SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.YoungestInFront);
        ConstantRate(spawn, 30f);
        SetRandom(init, "position", new Vector3(-0.6f, -0.6f, -1f), new Vector3(0.6f, 0.6f, 1f));
        SetRandom(init, "lifetime", 0.3f, 1.5f);
        Set(init, "size", 0.7f);
        Set(init, "alpha", 0.6f);
        SetRandom(init, "color", new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
        Save(youngest);

        // More particles than one pass of the sort takes.
        var many = NewGraph("SortMany");
        Overlapping(many, 6000, 6000f, 0.12f, 1.4f);
        Save(many);
    }

    /// <summary>Outputs drawn one over the other by their priority (README.md, "Output order"):
    /// two systems of one effect, the first with two outputs, each output of a colour of its own,
    /// over the same particles (none is sorted: each draws its particles in their order).</summary>
    static void Prioritized()
    {
        Prioritized("Priority", new[] { new[] { 2, 0 }, new[] { 1 } });
        // Equal priorities: the order of the systems, then of each system's outputs, whatever
        // the order the outputs were added to the graph in (one of each system in turn).
        Prioritized("PriorityTies", new[] { new[] { 0, 0 }, new[] { 0, 0 } });
    }

    static readonly Vector3[] k_OutputColors = { new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(1f, 1f, 0f) };

    static void Prioritized(string name, int[][] priorities)
    {
        var graph = NewGraph(name);
        VFXPlanarPrimitiveOutput Output(VFXBasicUpdate update, int priority, Vector3 color, string label)
        {
            var output = Make<VFXPlanarPrimitiveOutput>();
            // A label of its own: the outputs' shaders are named after it.
            output.label = label;
            graph.AddChild(output);
            update.LinkTo(output);
            output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
            output.SetSettingValue("sort", VFXAbstractParticleOutput.SortActivationMode.Off);
            output.SetSettingValue("vfxSystemSortPriority", priority);
            SetVector(output, "color", color);
            return output;
        }
        var updates = new VFXBasicUpdate[priorities.Length];
        for (int s = 0; s < priorities.Length; s++)
        {
            var spawn = Make<VFXBasicSpawner>();
            var init = Make<VFXBasicInitialize>();
            var update = Make<VFXBasicUpdate>();
            graph.AddChild(spawn);
            graph.AddChild(init);
            graph.AddChild(update);
            spawn.LinkTo(init);
            init.LinkTo(update);
            init.GetData().SetSettingValue("capacity", 16u);
            Burst(spawn, 12f);
            SetRandom(init, "position", new Vector3(-0.8f, -0.8f, -1f), new Vector3(0.8f, 0.8f, 1f));
            Set(init, "size", 0.5f);
            Set(init, "alpha", 0.5f);
            updates[s] = update;
        }
        // The outputs added one of each system in turn, each of a colour of its own.
        int color = 0;
        for (int o = 0; o < 2; o++)
            for (int s = 0; s < priorities.Length; s++)
                if (o < priorities[s].Length)
                {
                    Output(updates[s], priorities[s][o], k_OutputColors[color], "Output " + s + "." + o);
                    color++;
                }
        Save(graph);
    }

    /// <summary>The graphs of the other planar primitives (README.md, "Primitives"): triangles
    /// and octagons, drawn directly or from the update kernel's list, and more than one draw's
    /// indices reach.</summary>
    static void Primitives()
    {
        foreach (var primitive in new[] { VFXPrimitiveType.Triangle, VFXPrimitiveType.Octagon })
        {
            string name = primitive.ToString();
            foreach (var listed in new[] { false, true })
            {
                // Forty opaque primitives at once, each with a place, a depth, a colour and an
                // angle of its own; octagons cropped to a third of their corners.
                var still = NewGraph(name + (listed ? "List" : "Still"));
                var (spawn, init, output) = System(still, 64);
                output.SetSettingValue("primitiveType", primitive);
                output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
                if (listed) output.SetSettingValue("indirectDraw", true);
                if (primitive == VFXPrimitiveType.Octagon) Input(output, "cropFactor").value = 0.3f;
                Burst(spawn, 40f);
                SetRandom(init, "position", new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
                Set(init, "size", 0.4f);
                SetRandom(init, "angleZ", 0f, 90f);
                SetRandom(init, "color", new Vector3(0.2f, 0.2f, 0.2f), new Vector3(1f, 1f, 1f));
                Save(still);
            }

            // More than one draw's 16-bit indices reach (octagons: 8192, triangles: 21845),
            // added to what is behind.
            var many = NewGraph(name + "Many");
            var (manySpawn, manyInit, manyOutput) = System(many, 30000);
            manyOutput.SetSettingValue("primitiveType", primitive);
            manyOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Additive);
            Burst(manySpawn, 30000f);
            SetRandom(manyInit, "position", new Vector3(-1.4f, -1.4f, 0f), new Vector3(1.4f, 1.4f, 0f));
            Set(manyInit, "size", 0.05f);
            SetVector(manyInit, "color", new Vector3(0.3f, 0.6f, 1f));
            Save(many);
        }
    }

    /// <summary>Graphs whose generated shaders share a name (README.md, "Alike"): one file name
    /// in two folders, the one opaque and the other additive, and one system with an opaque and
    /// an additive output of one name (no label).</summary>
    static void Alike()
    {
        foreach (var (folder, blend, color) in new[]
        {
            ("TwinA", VFXAbstractRenderedOutput.BlendMode.Opaque, new Vector3(1f, 0.4f, 0.2f)),
            ("TwinB", VFXAbstractRenderedOutput.BlendMode.Additive, new Vector3(0.2f, 0.4f, 1f)),
        })
        {
            AssetDatabase.CreateFolder(Folder, folder);
            var twin = NewGraph(folder + "/Twin");
            var (spawn, init, output) = System(twin, 64);
            output.SetSettingValue("blendMode", blend);
            Burst(spawn, 40f);
            SetRandom(init, "position", new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
            Set(init, "size", 0.4f);
            SetVector(init, "color", color);
            Save(twin);
        }

        var pair = NewGraph("AlikeOutputs");
        var (pairSpawn, pairInit, opaque) = System(pair, 64);
        opaque.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        var additive = Make<VFXPlanarPrimitiveOutput>();
        pair.AddChild(additive);
        pair.children.OfType<VFXBasicUpdate>().First().LinkTo(additive);
        additive.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Additive);
        Burst(pairSpawn, 40f);
        SetRandom(pairInit, "position", new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        Set(pairInit, "size", 0.4f);
        SetRandom(pairInit, "color", new Vector3(0.2f, 0.2f, 0.2f), new Vector3(1f, 1f, 1f));
        Save(pair);
    }

    /// <summary>Quads moving sideways (README.md, "Moving"): an opaque output that writes motion
    /// vectors, and the same graph without them.</summary>
    static void Moving()
    {
        foreach (var (name, vectors) in new[] { ("MovingVectors", true), ("MovingPlain", false) })
        {
            var graph = NewGraph(name);
            var (spawn, init, output) = System(graph, 64);
            output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
            output.SetSettingValue("generateMotionVector", vectors);
            Burst(spawn, 20f);
            SetRandom(init, "position", new Vector3(-1.4f, -1f, 0f), new Vector3(-0.8f, 1f, 0f));
            SetVector(init, "velocity", new Vector3(6f, 0f, 0f));
            Set(init, "size", 0.25f);
            SetRandom(init, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
            Save(graph);
        }
    }

    /// <summary>Graphs whose outputs share their sort (README.md, "Shared sorts"): Overlapping's
    /// thirty alpha-blended quads drawn by a second output too, smaller and white, both sorted
    /// alike, which the compiler sorts once for the system (a global sort); with a third output
    /// sorted otherwise, which sorts its own list.</summary>
    static void GloballySorted()
    {
        VFXPlanarPrimitiveOutput Second(VFXGraph graph, VFXPlanarPrimitiveOutput first, string label, float size, Vector3 color)
        {
            var output = Make<VFXPlanarPrimitiveOutput>();
            output.label = label;
            graph.AddChild(output);
            ((VFXContext)first.inputContexts.First()).LinkTo(output);
            output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
            Set(output, "size", size);
            SetVector(output, "color", color);
            return output;
        }

        var distance = NewGraph("GlobalDistance");
        var near = Overlapping(distance, 32, 30f, 0.7f, 0.6f);
        near.label = "Large";
        near.SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.DistanceToCamera);
        Second(distance, near, "Small", 0.35f, Vector3.one).SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.DistanceToCamera);
        Save(distance);

        // One key of each particle's own for both outputs: sorted at each step, not per camera.
        var keyed = NewGraph("GlobalKeyed");
        var large = Overlapping(keyed, 32, 30f, 0.7f, 0.6f);
        large.label = "Large";
        var small = Second(keyed, large, "Small", 0.35f, Vector3.one);
        var random = Make<UnityEditor.VFX.Operator.Random>();
        keyed.AddChild(random);
        foreach (var output in new[] { large, small })
        {
            output.SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.Custom);
            Link(random.outputSlots[0], Input(output, "sortKey"));
        }
        Save(keyed);

        // Youngest in front, as SortYoungest's particles (dead particles' places taken again):
        // sorted at each step, not per camera, by an attribute.
        var youngest = NewGraph("GlobalYoungest");
        var (spawn, init, older) = System(youngest, 64);
        older.label = "Large";
        older.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
        ConstantRate(spawn, 30f);
        SetRandom(init, "position", new Vector3(-0.6f, -0.6f, -1f), new Vector3(0.6f, 0.6f, 1f));
        SetRandom(init, "lifetime", 0.3f, 1.5f);
        Set(init, "size", 0.7f);
        Set(init, "alpha", 0.6f);
        SetRandom(init, "color", new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
        older.SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.YoungestInFront);
        Second(youngest, older, "Small", 0.35f, Vector3.one).SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.YoungestInFront);
        Save(youngest);

        // Two outputs by distance share a sort; a third, by depth, sorts its own list.
        var mixed = NewGraph("GlobalMixed");
        var first = Overlapping(mixed, 32, 30f, 0.7f, 0.6f);
        first.label = "Large";
        first.SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.DistanceToCamera);
        Second(mixed, first, "Small", 0.35f, Vector3.one).SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.DistanceToCamera);
        Second(mixed, first, "Tiny", 0.15f, new Vector3(0f, 0f, 0f)).SetSettingValue("sortMode", VFXSortingUtility.SortCriteria.CameraDepth);
        Save(mixed);
    }

    /// <summary>The probe scene: a camera that sees none of the stages (the probe builds them).</summary>
    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera") { tag = "MainCamera" };
        camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -10f);
        EditorSceneManager.SaveScene(scene, Scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
    }
}
