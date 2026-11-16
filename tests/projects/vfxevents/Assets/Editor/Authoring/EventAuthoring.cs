using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.VFX;
using UnityEditor.VFX.Block;
using UnityEditor.VFX.URP;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

/// <summary>
/// Authors the probe's graphs, objects and scene in the Editor, through the graph package's
/// own models (this assembly carries the name of a friend assembly of the graph package and
/// of the render pipeline's editor assembly). The files it writes are kept under Authored/
/// (tests/authored_project.py) and replayed by the engine.
/// </summary>
public static class EventAuthoring
{
    public const string GraphFolder = "Assets/Graphs";
    const string ObjectFolder = "Assets/Objects";
    public const string Scene = "Assets/Scenes/Probe.unity";

    public static void Author()
    {
        Directory.CreateDirectory(GraphFolder);
        AssetDatabase.Refresh();
        Events();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildScene();
    }

    // ----------------------------------------------------------------------------- graphs

    /// <summary>A system of opaque unlit quads that draws itself; returns its contexts.</summary>
    static (VFXBasicSpawner Spawn, VFXBasicInitialize Init, VFXBasicUpdate Update, VFXPlanarPrimitiveOutput Output) Quads(VFXGraph graph, uint capacity, float size, Vector3 color)
    {
        var system = System<VFXPlanarPrimitiveOutput>(graph, capacity);
        system.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Set(system.Init, "size", size);
        SetVector(system.Init, "color", color);
        return system;
    }

    /// <summary>A system spawned by a GPU event of a block: an initialize, an update and an
    /// output of the kind asked for, behind a GPU event context.</summary>
    static (VFXBasicInitialize Init, VFXBasicUpdate Update, TOutput Output) Child<TOutput>(VFXGraph graph, TriggerEvent from, uint capacity) where TOutput : VFXContext
    {
        var gpuEvent = Make<VFXBasicGPUEvent>();
        var init = Make<VFXBasicInitialize>();
        var update = Make<VFXBasicUpdate>();
        var output = Make<TOutput>();
        graph.AddChild(gpuEvent);
        graph.AddChild(init);
        graph.AddChild(update);
        graph.AddChild(output);
        Link(from.outputSlots[0], gpuEvent.inputSlots[0]);
        gpuEvent.LinkTo(init);
        init.LinkTo(update);
        init.GetData().SetSettingValue("capacity", capacity);
        update.LinkTo(output);
        return (init, update, output);
    }

    static TriggerEvent Trigger(VFXContext context, TriggerEvent.Mode mode, object amount)
    {
        var block = Make<TriggerEvent>();
        block.SetSettingValue("mode", mode);
        context.AddChild(block);
        block.inputSlots[0].value = amount;
        return block;
    }

    /// <summary>A block that takes an attribute from what spawned the particle.</summary>
    static void Inherit(VFXContext context, string attribute)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        block.SetSettingValue("Source", SetAttribute.ValueSource.Source);
        context.AddChild(block);
    }

    static void Events()
    {
        // Six a second rising and living half a second; each that dies spawns four sparks
        // where it was.
        var die = NewGraph("EventOnDie");
        var dying = Quads(die, 16, 0.25f, new Vector3(1f, 1f, 1f));
        ConstantRate(dying.Spawn, 6f);
        SetRandom(dying.Init, "position", new Vector3(-1.2f, -1.2f, 0f), new Vector3(1.2f, -0.8f, 0f));
        SetVector(dying.Init, "velocity", new Vector3(0f, 3f, 0f));
        Set(dying.Init, "lifetime", 0.5f);
        var onDie = Trigger(dying.Update, TriggerEvent.Mode.OnDie, 4u);
        var sparks = Child<VFXPlanarPrimitiveOutput>(die, onDie, 64);
        sparks.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(sparks.Init, "position");
        SetRandom(sparks.Init, "velocity", new Vector3(-1.5f, -1.5f, 0f), new Vector3(1.5f, 1.5f, 0f));
        Set(sparks.Init, "lifetime", 0.4f);
        Set(sparks.Init, "size", 0.1f);
        SetVector(sparks.Init, "color", new Vector3(1f, 0.5f, 0.2f));
        Save(die);

        // Four that move right and each spawn one a step: more than the spawned system has
        // room for.
        var always = NewGraph("EventAlways");
        var movers = Quads(always, 8, 0.2f, new Vector3(0.4f, 0.8f, 1f));
        Burst(movers.Spawn, 4f);
        SetRandom(movers.Init, "position", new Vector3(-1.3f, -1.2f, 0f), new Vector3(-1.1f, 1.2f, 0f));
        SetVector(movers.Init, "velocity", new Vector3(1.5f, 0f, 0f));
        var each = Trigger(movers.Update, TriggerEvent.Mode.Always, 1u);
        var dots = Child<VFXPlanarPrimitiveOutput>(always, each, 32);
        dots.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(dots.Init, "position");
        Set(dots.Init, "lifetime", 0.3f);
        Set(dots.Init, "size", 0.08f);
        SetVector(dots.Init, "color", new Vector3(1f, 0.9f, 0.3f));
        Save(always);

        // Three that move and spawn twenty a second each, which stay where they were spawned.
        var rate = NewGraph("EventRate");
        var leaders = Quads(rate, 4, 0.2f, new Vector3(0.6f, 1f, 0.6f));
        Burst(leaders.Spawn, 3f);
        SetRandom(leaders.Init, "position", new Vector3(-1.2f, -1.2f, 0f), new Vector3(-0.8f, 1.2f, 0f));
        SetRandom(leaders.Init, "velocity", new Vector3(1f, -0.5f, 0f), new Vector3(1.5f, 0.5f, 0f));
        var overTime = Trigger(leaders.Update, TriggerEvent.Mode.OverTime, 20f);
        var crumbs = Child<VFXPlanarPrimitiveOutput>(rate, overTime, 64);
        crumbs.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(crumbs.Init, "position");
        Set(crumbs.Init, "lifetime", 0.5f);
        Set(crumbs.Init, "size", 0.06f);
        SetVector(crumbs.Init, "color", new Vector3(1f, 0.4f, 0.8f));
        Save(rate);

        // One that moves fast and spawns one for each fifth of a unit it travels.
        var distance = NewGraph("EventDistance");
        var runner = Quads(distance, 4, 0.2f, new Vector3(0.6f, 1f, 0.6f));
        Burst(runner.Spawn, 2f);
        SetRandom(runner.Init, "position", new Vector3(-1.3f, -1f, 0f), new Vector3(-1.1f, 1f, 0f));
        SetRandom(runner.Init, "velocity", new Vector3(1f, -0.3f, 0f), new Vector3(2f, 0.3f, 0f));
        var overDistance = Trigger(runner.Update, TriggerEvent.Mode.OverDistance, 5f);
        var marks = Child<VFXPlanarPrimitiveOutput>(distance, overDistance, 64);
        marks.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(marks.Init, "position");
        Set(marks.Init, "lifetime", 0.8f);
        Set(marks.Init, "size", 0.06f);
        SetVector(marks.Init, "color", new Vector3(0.3f, 0.9f, 1f));
        Save(distance);

        // The trigger in the initialize context: each particle spawns three as it is born,
        // which take its colour as well as its place.
        var born = NewGraph("EventAtBirth");
        var parents = Quads(born, 16, 0.25f, new Vector3(1f, 1f, 1f));
        ConstantRate(parents.Spawn, 5f);
        SetRandom(parents.Init, "position", new Vector3(-1.2f, -1.2f, 0f), new Vector3(1.2f, 1.2f, 0f));
        SetRandom(parents.Init, "color", new Vector3(0.2f, 0.2f, 0.2f), new Vector3(1f, 1f, 1f));
        Set(parents.Init, "lifetime", 0.6f);
        var atBirth = Trigger(parents.Init, TriggerEvent.Mode.Always, 3u);
        var born3 = Child<VFXPlanarPrimitiveOutput>(born, atBirth, 64);
        born3.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(born3.Init, "position");
        Inherit(born3.Init, "color");
        SetRandom(born3.Init, "velocity", new Vector3(-1f, -1f, 0f), new Vector3(1f, 1f, 0f));
        Set(born3.Init, "lifetime", 0.5f);
        Set(born3.Init, "size", 0.08f);
        Save(born);

        // Two triggers in one system: on death into one system, each step into another.
        var both = NewGraph("EventTwo");
        var source = Quads(both, 8, 0.2f, new Vector3(1f, 1f, 1f));
        ConstantRate(source.Spawn, 4f);
        SetRandom(source.Init, "position", new Vector3(-1.2f, -1.2f, 0f), new Vector3(1.2f, -0.8f, 0f));
        SetVector(source.Init, "velocity", new Vector3(0f, 2.5f, 0f));
        Set(source.Init, "lifetime", 0.6f);
        var whenDead = Trigger(source.Update, TriggerEvent.Mode.OnDie, 6u);
        var eachStep = Trigger(source.Update, TriggerEvent.Mode.Always, 1u);
        var bursts = Child<VFXPlanarPrimitiveOutput>(both, whenDead, 64);
        bursts.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(bursts.Init, "position");
        SetRandom(bursts.Init, "velocity", new Vector3(-1.5f, -1.5f, 0f), new Vector3(1.5f, 1.5f, 0f));
        Set(bursts.Init, "lifetime", 0.4f);
        Set(bursts.Init, "size", 0.1f);
        SetVector(bursts.Init, "color", new Vector3(1f, 0.5f, 0.2f));
        var smoke = Child<VFXPlanarPrimitiveOutput>(both, eachStep, 64);
        smoke.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(smoke.Init, "position");
        Set(smoke.Init, "lifetime", 0.25f);
        Set(smoke.Init, "size", 0.05f);
        SetVector(smoke.Init, "color", new Vector3(0.5f, 0.6f, 1f));
        Save(both);

        // A chain: what an event spawned spawns in turn when it dies.
        var chain = NewGraph("EventChain");
        var first = Quads(chain, 8, 0.25f, new Vector3(1f, 1f, 1f));
        ConstantRate(first.Spawn, 3f);
        SetRandom(first.Init, "position", new Vector3(-1.2f, -1.2f, 0f), new Vector3(1.2f, -0.8f, 0f));
        SetVector(first.Init, "velocity", new Vector3(0f, 3f, 0f));
        Set(first.Init, "lifetime", 0.4f);
        var firstDies = Trigger(first.Update, TriggerEvent.Mode.OnDie, 3u);
        var second = Child<VFXPlanarPrimitiveOutput>(chain, firstDies, 32);
        second.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(second.Init, "position");
        SetRandom(second.Init, "velocity", new Vector3(-2f, -0.5f, 0f), new Vector3(2f, 1.5f, 0f));
        Set(second.Init, "lifetime", 0.3f);
        Set(second.Init, "size", 0.14f);
        SetVector(second.Init, "color", new Vector3(1f, 0.6f, 0.2f));
        var secondDies = Trigger(second.Update, TriggerEvent.Mode.OnDie, 2u);
        var third = Child<VFXPlanarPrimitiveOutput>(chain, secondDies, 64);
        third.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(third.Init, "position");
        SetRandom(third.Init, "velocity", new Vector3(-1f, -1f, 0f), new Vector3(1f, 1f, 0f));
        Set(third.Init, "lifetime", 0.3f);
        Set(third.Init, "size", 0.07f);
        SetVector(third.Init, "color", new Vector3(0.4f, 1f, 0.5f));
        Save(chain);

        // What the events spawn never dies: a system that fills up, and one with room left.
        foreach (var (name, capacity) in new[] { ("EventKeep", 24u), ("EventKeepRoom", 96u) })
        {
            var keep = NewGraph(name);
            var droppers = Quads(keep, 4, 0.2f, new Vector3(0.6f, 1f, 0.6f));
            Burst(droppers.Spawn, 2f);
            SetRandom(droppers.Init, "position", new Vector3(-1.3f, -1f, 0f), new Vector3(-1.1f, 1f, 0f));
            SetRandom(droppers.Init, "velocity", new Vector3(1f, -0.3f, 0f), new Vector3(1.5f, 0.3f, 0f));
            var dropping = Trigger(droppers.Update, TriggerEvent.Mode.OverTime, 10f);
            var kept = Child<VFXPlanarPrimitiveOutput>(keep, dropping, capacity);
            kept.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
            Inherit(kept.Init, "position");
            Set(kept.Init, "size", 0.07f);
            SetVector(kept.Init, "color", new Vector3(1f, 0.8f, 0.3f));
            Save(keep);
        }
    }

    // ---------------------------------------------------------------------------- helpers

    static T Make<T>() where T : VFXModel => ScriptableObject.CreateInstance<T>();

    static VFXGraph NewGraph(string name)
    {
        var asset = VisualEffectAssetEditorUtility.CreateNewAsset(GraphFolder + "/" + name + ".vfx");
        return asset.GetResource().GetOrCreateGraph();
    }

    static void Save(VFXGraph graph)
    {
        var resource = graph.GetResource();
        resource.WriteAssetWithSubAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(resource), ImportAssetOptions.ForceUpdate);
    }

    /// <summary>A spawner, an initialize, an update and an output of the kind asked for,
    /// linked; returns them.</summary>
    static (VFXBasicSpawner Spawn, VFXBasicInitialize Init, VFXBasicUpdate Update, TOutput Output) System<TOutput>(VFXGraph graph, uint capacity) where TOutput : VFXContext
    {
        var spawn = Make<VFXBasicSpawner>();
        var init = Make<VFXBasicInitialize>();
        var update = Make<VFXBasicUpdate>();
        var output = Make<TOutput>();
        graph.AddChild(spawn);
        graph.AddChild(init);
        graph.AddChild(update);
        graph.AddChild(output);
        spawn.LinkTo(init);
        init.LinkTo(update);
        update.LinkTo(output);
        init.GetData().SetSettingValue("capacity", capacity);
        return (spawn, init, update, output);
    }

    /// <summary>An input of a model by its name; the names it has are said when it has
    /// none of that name.</summary>
    static VFXSlot Input(VFXModel model, string name)
    {
        var slots = ((IVFXSlotContainer)model).inputSlots;
        var slot = slots.FirstOrDefault(s => s.name == name);
        if (slot == null) throw new InvalidOperationException(model.GetType().Name + " has no input '" + name + "': " + string.Join(", ", slots.Select(s => s.name)));
        return slot;
    }

    static VFXParameter Parameter(VFXGraph graph, Type type, string name, object value)
    {
        var parameter = VFXLibrary.GetParameters().First(d => d.modelType == type).CreateInstance();
        parameter.SetSettingValue("m_ExposedName", name);
        parameter.SetSettingValue("m_Exposed", true);
        parameter.value = value;
        graph.AddChild(parameter);
        return parameter;
    }

    static void Link(VFXSlot from, VFXSlot to)
    {
        if (!from.Link(to)) throw new InvalidOperationException("cannot link " + from.property.type.Name + " '" + from.name + "' to " + to.property.type.Name + " '" + to.name + "'");
    }

    static void ConstantRate(VFXBasicSpawner spawn, float rate)
    {
        var block = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(block);
        block.inputSlots[0].value = rate;
    }

    /// <summary>Moves what an output draws towards the camera (the systems are in the
    /// effect's space, the cameras look along its z).</summary>
    static void Nearer(VFXContext output, float by)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", "position");
        block.SetSettingValue("Composition", AttributeCompositionMode.Add);
        output.AddChild(block);
        block.inputSlots[0].value = Spaced(block.inputSlots[0], new Vector3(0f, 0f, -by));
    }

    /// <summary>One burst at the start.</summary>
    static void Burst(VFXBasicSpawner spawn, float count)
    {
        var block = Make<VFXSpawnerBurst>();
        spawn.AddChild(block);
        block.SetSettingValue("repeat", VFXSpawnerBurst.RepeatMode.Single);
        Input(block, "Count").value = count;
        Input(block, "Delay").value = 0f;
    }

    static SetAttribute Set(VFXContext context, string attribute, object value)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        context.AddChild(block);
        block.inputSlots[0].value = value;
        return block;
    }

    /// <summary>A vector as the type a slot holds it in (the package's own spaced types).</summary>
    static object Spaced(VFXSlot slot, Vector3 value)
    {
        var type = slot.property.type;
        if (type == typeof(Position)) return (Position)value;
        if (type == typeof(Vector)) return (Vector)value;
        if (type == typeof(DirectionType)) return (DirectionType)value;
        return value;
    }

    static SetAttribute SetVector(VFXContext context, string attribute, Vector3 value)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        context.AddChild(block);
        block.inputSlots[0].value = Spaced(block.inputSlots[0], value);
        return block;
    }

    /// <summary>A block that draws an attribute for each particle, component by component,
    /// between two vectors.</summary>
    static SetAttribute SetRandom(VFXContext context, string attribute, Vector3 from, Vector3 to)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        block.SetSettingValue("Random", RandomMode.PerComponent);
        context.AddChild(block);
        block.inputSlots[0].value = Spaced(block.inputSlots[0], from);
        block.inputSlots[1].value = Spaced(block.inputSlots[1], to);
        return block;
    }

    // ------------------------------------------------------------------------------ scene

    /// <summary>The probe scene: a camera, one flat ambient colour and no reflection, so
    /// that what lights the particles is the lights the probe makes.</summary>
    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera") { tag = "MainCamera" };
        camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -10f);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.15f, 0.2f, 0.25f, 1f);
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0f;
        EditorSceneManager.SaveScene(scene, Scene);
    }
}
