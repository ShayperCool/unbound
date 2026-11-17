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
public static class DirectAuthoring
{
    public const string GraphFolder = "Assets/Graphs";
    public const string Scene = "Assets/Scenes/Probe.unity";

    public static void Author()
    {
        Directory.CreateDirectory(GraphFolder);
        AssetDatabase.Refresh();
        Direct();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildScene();
    }

    // ----------------------------------------------------------------------------- graphs

    /// <summary>A system of opaque unlit quads whose Initialize context is fed by an event
    /// context, without a spawn context between: an initialize, an update and an output.
    /// A particle takes its position and colour from the event that spawned it.</summary>
    static (VFXBasicInitialize Init, VFXBasicUpdate Update) Wired(VFXGraph graph, string eventName, uint capacity)
    {
        var wire = Make<VFXBasicEvent>();
        wire.SetSettingValue("eventName", eventName);
        var init = Make<VFXBasicInitialize>();
        var update = Make<VFXBasicUpdate>();
        var output = Make<VFXPlanarPrimitiveOutput>();
        graph.AddChild(wire);
        graph.AddChild(init);
        graph.AddChild(update);
        graph.AddChild(output);
        wire.LinkTo(init);
        init.LinkTo(update);
        update.LinkTo(output);
        init.GetData().SetSettingValue("capacity", capacity);
        output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(init, "position");
        Inherit(init, "color");
        SetRandom(init, "velocity", new Vector3(-1f, -1f, 0f), new Vector3(1f, 1f, 0f));
        Set(init, "size", 0.2f);
        return (init, update);
    }

    /// <summary>A block that takes an attribute from what spawned the particle.</summary>
    static void Inherit(VFXContext context, string attribute)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        block.SetSettingValue("Source", SetAttribute.ValueSource.Source);
        context.AddChild(block);
    }

    static void Direct()
    {
        // An event `Hit` wired to the Initialize context; the particles live half a second.
        var hit = NewGraph("DirectHit");
        Set(Wired(hit, "Hit", 32).Init, "lifetime", 0.5f);
        Save(hit);

        // The same without a lifetime: the system fills up.
        var keep = NewGraph("DirectKeep");
        Wired(keep, "Hit", 8);
        Save(keep);

        // The effect's own start event wired to the Initialize context.
        var start = NewGraph("DirectOnPlay");
        Wired(start, VisualEffectAsset.PlayEventName, 8);
        Save(start);

        // An Initialize context fed by a spawn context (five a second) and by the event too.
        var both = NewGraph("DirectAndSpawner");
        var fed = Wired(both, "Hit", 32);
        var spawn = Make<VFXBasicSpawner>();
        both.AddChild(spawn);
        spawn.LinkTo(fed.Init);
        ConstantRate(spawn, 5f);
        Save(both);

        // One event wired to the Initialize contexts of two systems.
        var two = NewGraph("DirectTwo");
        var first = Wired(two, "Hit", 8);
        var second = Make<VFXBasicInitialize>();
        var secondUpdate = Make<VFXBasicUpdate>();
        var secondOutput = Make<VFXPlanarPrimitiveOutput>();
        two.AddChild(second);
        two.AddChild(secondUpdate);
        two.AddChild(secondOutput);
        first.Init.inputFlowSlot[0].link[0].context.LinkTo(second);
        second.LinkTo(secondUpdate);
        secondUpdate.LinkTo(secondOutput);
        second.GetData().SetSettingValue("capacity", 8u);
        secondOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Inherit(second, "position");
        SetVector(second, "velocity", new Vector3(0f, -1f, 0f));
        Set(second, "size", 0.1f);
        SetVector(second, "color", new Vector3(1f, 0.3f, 0.3f));
        Save(two);
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
