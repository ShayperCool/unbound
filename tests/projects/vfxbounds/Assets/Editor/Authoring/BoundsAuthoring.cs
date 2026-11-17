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
public static class BoundsAuthoring
{
    public const string GraphFolder = "Assets/Graphs";
    public const string Scene = "Assets/Scenes/Probe.unity";

    public static void Author()
    {
        Directory.CreateDirectory(GraphFolder);
        AssetDatabase.Refresh();
        Bounded();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildScene();
    }

    // ----------------------------------------------------------------------------- graphs

    /// <summary>A system of opaque unlit quads whose bounds are set as asked: by the graph,
    /// or computed from its particles while it runs.</summary>
    static (VFXGraph Graph, VFXBasicSpawner Spawn, VFXBasicInitialize Init, VFXBasicUpdate Update) Bounded(string name, BoundsSettingMode mode, uint capacity = 16)
    {
        var graph = NewGraph(name);
        var (spawn, init, update, output) = System<VFXPlanarPrimitiveOutput>(graph, capacity);
        output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        init.GetData().SetSettingValue("boundsMode", mode);
        SetVector(init, "color", new Vector3(0.9f, 0.8f, 0.3f));
        return (graph, spawn, init, update);
    }

    /// <summary>Six particles of different sizes and turns that fly apart.</summary>
    static void Spreading(VFXBasicSpawner spawn, VFXBasicInitialize init)
    {
        Burst(spawn, 6f);
        SetRandom(init, "position", new Vector3(-0.4f, -0.4f, 0f), new Vector3(0.4f, 0.4f, 0f));
        SetRandom(init, "velocity", new Vector3(-0.8f, -0.8f, 0f), new Vector3(0.8f, 0.8f, 0f));
        SetRandom(init, "angle", new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 90f));
        var size = Make<SetAttribute>();
        size.SetSettingValue("attribute", "size");
        size.SetSettingValue("Random", RandomMode.Uniform);
        init.AddChild(size);
        size.inputSlots[0].value = 0.15f;
        size.inputSlots[1].value = 0.4f;
    }

    static void Bounded()
    {
        // Bounds computed from the particles: a graph for each of three effects, and one that
        // two effects share.
        foreach (var name in new[] { "BoundsAuto", "BoundsReinit", "BoundsMoved", "BoundsShared" })
        {
            var auto = Bounded(name, BoundsSettingMode.Automatic);
            Spreading(auto.Spawn, auto.Init);
            Save(auto.Graph);
        }

        // The same with a padding, which is a property of the effect.
        var padded = Bounded("BoundsPadded", BoundsSettingMode.Automatic);
        Spreading(padded.Spawn, padded.Init);
        Link(Parameter(padded.Graph, typeof(Vector3), "Padding", new Vector3(0.5f, 0.25f, 0.125f)).outputSlots[0], Input(padded.Init, "boundsPadding"));
        Save(padded.Graph);

        // The same in world space: a graph for each of two effects.
        foreach (var name in new[] { "BoundsWorld", "BoundsWorldTurned" })
        {
            var world = Bounded(name, BoundsSettingMode.Automatic);
            world.Init.space = VFXSpace.World;
            Spreading(world.Spawn, world.Init);
            Save(world.Graph);
        }

        // Particles that are born all the time, move right and die: the bounds follow the
        // ones alive.
        var dying = Bounded("BoundsDying", BoundsSettingMode.Automatic);
        ConstantRate(dying.Spawn, 10f);
        SetRandom(dying.Init, "position", new Vector3(-1.2f, -1f, 0f), new Vector3(-1f, 1f, 0f));
        SetVector(dying.Init, "velocity", new Vector3(2f, 0f, 0f));
        Set(dying.Init, "lifetime", 0.5f);
        Set(dying.Init, "size", 0.2f);
        Save(dying.Graph);

        // Four particles that fly out of view to the right: with computed bounds, and with
        // bounds the graph gives.
        foreach (var (name, mode) in new[] { ("BoundsAway", BoundsSettingMode.Automatic), ("ManualAway", BoundsSettingMode.Manual) })
        {
            var away = Bounded(name, mode);
            Burst(away.Spawn, 4f);
            SetRandom(away.Init, "position", new Vector3(-0.4f, -0.4f, 0f), new Vector3(0.4f, 0.4f, 0f));
            SetVector(away.Init, "velocity", new Vector3(4f, 0f, 0f));
            Set(away.Init, "size", 0.3f);
            Save(away.Graph);
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
