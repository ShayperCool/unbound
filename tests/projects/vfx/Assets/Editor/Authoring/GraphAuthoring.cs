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
    const string Folder = "Assets/Graphs";
    public const string Scene = "Assets/Scenes/Probe.unity";

    public static void Author()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        Empty();
        Basic();
        Exposed();
        Exposed2();
        Events();
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

    /// <summary>A spawner, an initialize, an update and a quad output, linked; returns them.</summary>
    static (VFXBasicSpawner Spawn, VFXBasicInitialize Init, VFXBasicUpdate Update, VFXPlanarPrimitiveOutput Output) System(VFXGraph graph, uint capacity)
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
        return (spawn, init, update, output);
    }

    static SetAttribute Set(VFXContext context, string attribute, object value)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        context.AddChild(block);
        block.inputSlots[0].value = value;
        return block;
    }

    static VFXParameter Parameter(VFXGraph graph, Type type, string name, object value = null, bool exposed = true)
    {
        var parameter = VFXLibrary.GetParameters().First(d => d.modelType == type).CreateInstance();
        parameter.SetSettingValue("m_ExposedName", name);
        parameter.SetSettingValue("m_Exposed", exposed);
        if (value != null) parameter.value = value;
        graph.AddChild(parameter);
        return parameter;
    }

    /// <summary>A graph without contexts: it compiles to nothing.</summary>
    static void Empty() => Save(NewGraph("Empty"));

    /// <summary>One system at a constant rate with a lifetime and a velocity.</summary>
    static void Basic()
    {
        var graph = NewGraph("Basic");
        var (spawn, init, _, _) = System(graph, 32);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        rate.inputSlots[0].value = 8f;
        Set(init, "lifetime", 2f);
        Set(init, "velocity", (Vector)new Vector3(0f, 1f, 0f));
        Save(graph);
    }

    /// <summary>One system and an exposed property of every type the blackboard offers, some
    /// linked into the system, with names that sort differently by culture and by code point.</summary>
    static void Exposed()
    {
        var graph = NewGraph("Exposed");
        var (spawn, init, _, _) = System(graph, 16);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        var rateParameter = Parameter(graph, typeof(float), "Rate", 4f);
        rateParameter.outputSlots[0].Link(rate.inputSlots[0]);
        var lifetime = Set(init, "lifetime", 1f);
        Parameter(graph, typeof(float), "life time", 3f).outputSlots[0].Link(lifetime.inputSlots[0]);

        Parameter(graph, typeof(int), "Count", -7);
        Parameter(graph, typeof(uint), "uCount", 9u);
        Parameter(graph, typeof(bool), "Flag", true);
        Parameter(graph, typeof(Vector2), "Pair", new Vector2(1.5f, -2.5f));
        Parameter(graph, typeof(Vector3), "Triple", new Vector3(1f, 2f, 3f));
        Parameter(graph, typeof(Vector4), "Quad", new Vector4(1f, 2f, 3f, 4f));
        Parameter(graph, typeof(Color), "Tint", new Color(0.25f, 0.5f, 0.75f, 1f));
        Parameter(graph, typeof(Position), "Where", (Position)new Vector3(1f, 2f, 3f));
        var world = Parameter(graph, typeof(Position), "WhereWorld", (Position)new Vector3(4f, 5f, 6f));
        world.outputSlots[0].space = VFXSpace.World;
        Parameter(graph, typeof(DirectionType), "Heading", (DirectionType)new Vector3(0f, 0f, 1f));
        Parameter(graph, typeof(Vector), "Offset", (Vector)new Vector3(7f, 8f, 9f));
        Parameter(graph, typeof(AnimationCurve), "Curve", AnimationCurve.Linear(0f, 0f, 1f, 2f));
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        Parameter(graph, typeof(Gradient), "Ramp", gradient);
        Parameter(graph, typeof(Texture2D), "Image");
        Parameter(graph, typeof(Texture3D), "Volume");
        Parameter(graph, typeof(Cubemap), "Sky");
        Parameter(graph, typeof(Texture2DArray), "Layers");
        Parameter(graph, typeof(CubemapArray), "Skies");
        Parameter(graph, typeof(Mesh), "Shape");
        Parameter(graph, typeof(SkinnedMeshRenderer), "Skin");
        Parameter(graph, typeof(GraphicsBuffer), "Data");
        Parameter(graph, typeof(TSphere), "Ball");
        Parameter(graph, typeof(AABox), "Box");
        Parameter(graph, typeof(UnityEditor.VFX.Transform), "Frame");
        // Names whose order differs between an ordinal and a culture comparison.
        Parameter(graph, typeof(float), "_under", 1f);
        Parameter(graph, typeof(float), "alpha", 2f);
        Parameter(graph, typeof(float), "Beta", 3f);
        Parameter(graph, typeof(float), "beta2", 4f);
        Parameter(graph, typeof(float), "Zeta", 5f);
        // Not exposed: no property of the asset.
        Parameter(graph, typeof(float), "Hidden", 6f, exposed: false);
        Save(graph);
    }

    /// <summary>A second graph sharing property names with Exposed: "Rate" with the same type,
    /// "Count" with another, and one of its own.</summary>
    static void Exposed2()
    {
        var graph = NewGraph("Exposed2");
        var (spawn, init, _, _) = System(graph, 16);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        Parameter(graph, typeof(float), "Rate", 6f).outputSlots[0].Link(rate.inputSlots[0]);
        Set(init, "lifetime", 1f);
        Parameter(graph, typeof(float), "Count", 1.5f);
        Parameter(graph, typeof(float), "Extra", 2.5f);
        Save(graph);
    }

    /// <summary>Custom events starting and stopping a spawner, and one starting a second system.</summary>
    static void Events()
    {
        var graph = NewGraph("Events");
        var (spawn, init, _, _) = System(graph, 8);
        var burst = Make<VFXSpawnerBurst>();
        spawn.AddChild(burst);
        burst.inputSlots[0].value = 4f;
        Set(init, "lifetime", 1f);
        var fire = Make<VFXBasicEvent>();
        fire.SetSettingValue("eventName", "Fire");
        graph.AddChild(fire);
        fire.LinkTo(spawn, 0, 0);
        var cease = Make<VFXBasicEvent>();
        cease.SetSettingValue("eventName", "Cease");
        graph.AddChild(cease);
        cease.LinkTo(spawn, 0, 1);

        var (spawn2, init2, _, _) = System(graph, 4);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn2.AddChild(rate);
        rate.inputSlots[0].value = 2f;
        Set(init2, "lifetime", 1f);
        Set(init2, "color", new Vector3(1f, 0f, 0f));
        Save(graph);
    }

    /// <summary>The probe scene: effects saved with their serialized state, one with overrides.</summary>
    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera") { tag = "MainCamera" };
        camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -10f);

        var plain = new GameObject("Plain").AddComponent<VisualEffect>();
        plain.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Folder + "/Basic.vfx");

        var overridden = new GameObject("Overridden").AddComponent<VisualEffect>();
        overridden.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Folder + "/Exposed.vfx");
        overridden.SetFloat("Rate", 12f);
        overridden.SetInt("Count", 5);
        overridden.SetUInt("uCount", 77u);
        overridden.SetBool("Flag", false);
        overridden.SetVector2("Pair", new Vector2(9f, 8f));
        overridden.SetVector3("Triple", new Vector3(7f, 6f, 5f));
        overridden.SetVector4("Quad", new Vector4(4f, 3f, 2f, 1f));
        overridden.SetVector4("Tint", new Vector4(1f, 0f, 0f, 0.5f));
        overridden.SetVector3("WhereWorld", new Vector3(-1f, -2f, -3f));
        overridden.SetAnimationCurve("Curve", AnimationCurve.Constant(0f, 1f, 5f));
        overridden.startSeed = 1234;
        overridden.resetSeedOnPlay = false;
        overridden.initialEventName = "Custom";
        overridden.transform.position = new Vector3(3f, 0f, 0f);

        var events = new GameObject("Events").AddComponent<VisualEffect>();
        events.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Folder + "/Events.vfx");
        events.transform.position = new Vector3(-3f, 0f, 0f);

        var none = new GameObject("NoAsset").AddComponent<VisualEffect>();
        none.transform.position = new Vector3(0f, 3f, 0f);

        EditorSceneManager.SaveScene(scene, Scene);
    }
}
