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
public static class StripAuthoring
{
    public const string GraphFolder = "Assets/Graphs";
    public const string ObjectFolder = "Assets/Objects";
    public const string Scene = "Assets/Scenes/Probe.unity";

    public static void Author()
    {
        Directory.CreateDirectory(GraphFolder);
        Directory.CreateDirectory(ObjectFolder);
        AssetDatabase.Refresh();
        Objects();
        Strips();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Prefabs();
        BuildScene();
    }

    // ---------------------------------------------------------------------------- objects

    /// <summary>An image whose four quarters differ: how a strip maps it shows.</summary>
    static void Objects()
    {
        var quarters = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                quarters.SetPixel(x, y, y < 4 ? (x < 4 ? Color.red : Color.green) : (x < 4 ? Color.blue : Color.yellow));
        File.WriteAllBytes(ObjectFolder + "/Quarters.png", quarters.EncodeToPNG());
        AssetDatabase.Refresh();
        var importer = (TextureImporter)AssetImporter.GetAtPath(ObjectFolder + "/Quarters.png");
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    static Texture2D Quarters => AssetDatabase.LoadAssetAtPath<Texture2D>(ObjectFolder + "/Quarters.png");

    /// <summary>An effect of the instanced graph without strips whose component does not
    /// allow instancing (a setting the component only has serialized).</summary>
    static void Prefabs()
    {
        var go = new GameObject("DyingApart");
        var effect = go.AddComponent<VisualEffect>();
        effect.resetSeedOnPlay = false;
        effect.startSeed = 11;
        effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(GraphFolder + "/Dying.vfx");
        var serialized = new SerializedObject(effect);
        var allow = serialized.FindProperty("m_AllowInstancing");
        if (allow == null) throw new InvalidOperationException("VisualEffect has no m_AllowInstancing");
        allow.boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(go, ObjectFolder + "/DyingApart.prefab");
        UnityEngine.Object.DestroyImmediate(go);
    }

    // ----------------------------------------------------------------------------- graphs

    /// <summary>Makes a system's particles strips: as many strips, of as many particles each.
    /// Set before an output of strips is linked (the output asks for such data).</summary>
    static void AsStrips(VFXContext init, uint strips, uint perStrip)
    {
        var data = init.GetData();
        data.SetSettingValue("dataType", VFXDataParticle.DataType.ParticleStrip);
        data.SetSettingValue("stripCapacity", strips);
        data.SetSettingValue("particlePerStripCount", perStrip);
    }

    /// <summary>A system of strips fed by a spawner: an initialize and an update, without an
    /// output yet.</summary>
    static (VFXBasicSpawner Spawn, VFXBasicInitialize Init, VFXBasicUpdate Update) Strips(VFXGraph graph, uint strips, uint perStrip)
    {
        var spawn = Make<VFXBasicSpawner>();
        var init = Make<VFXBasicInitialize>();
        var update = Make<VFXBasicUpdate>();
        graph.AddChild(spawn);
        graph.AddChild(init);
        graph.AddChild(update);
        spawn.LinkTo(init);
        init.LinkTo(update);
        AsStrips(init, strips, perStrip);
        return (spawn, init, update);
    }

    /// <summary>An output of a system of strips, after its update: opaque, and turned to the
    /// camera when it is one of quads along the strip.</summary>
    static TOutput Output<TOutput>(VFXGraph graph, VFXContext update, bool facing = true) where TOutput : VFXContext
    {
        var output = Make<TOutput>();
        graph.AddChild(output);
        update.LinkTo(output);
        if (output is VFXAbstractRenderedOutput) output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        if (facing)
        {
            var orient = Make<Orient>();
            orient.SetSettingValue("mode", Orient.Mode.FaceCameraPosition);
            output.AddChild(orient);
        }
        return output;
    }

    /// <summary>Particles at random places of the view that live a while.</summary>
    static void Scatter(VFXContext init, float size, Vector3 color, float lifetime)
    {
        SetRandom(init, "position", new Vector3(-1.2f, -1.2f, 0f), new Vector3(1.2f, 1.2f, 0f));
        if (lifetime > 0f) Set(init, "lifetime", lifetime);
        Set(init, "size", size);
        SetVector(init, "color", color);
    }

    /// <summary>A system spawned by a GPU event of a block, of strips: an initialize and an
    /// update behind a GPU event context. Each particle goes into the strip numbered as the
    /// particle that spawned it.</summary>
    static (VFXBasicInitialize Init, VFXBasicUpdate Update) Trails(VFXGraph graph, TriggerEvent from, uint strips, uint perStrip)
    {
        var gpuEvent = Make<VFXBasicGPUEvent>();
        var init = Make<VFXBasicInitialize>();
        var update = Make<VFXBasicUpdate>();
        graph.AddChild(gpuEvent);
        graph.AddChild(init);
        graph.AddChild(update);
        Link(from.outputSlots[0], gpuEvent.inputSlots[0]);
        gpuEvent.LinkTo(init);
        init.LinkTo(update);
        AsStrips(init, strips, perStrip);
        return (init, update);
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

    /// <summary>Particles that move across the view, each spawning into a system of strips.</summary>
    /// <summary>Particles in room for four that move, each triggering <paramref name="rate"/>
    /// GPU events a second: three at once that never die, or, given a
    /// <paramref name="lifetime"/>, <paramref name="spawnRate"/> a second that live that
    /// long.</summary>
    static TriggerEvent Heads(VFXGraph graph, float rate, float spawnRate = 0f, float lifetime = 0f)
    {
        var heads = System<VFXPlanarPrimitiveOutput>(graph, 4);
        heads.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Set(heads.Init, "size", 0.2f);
        SetVector(heads.Init, "color", new Vector3(0.6f, 1f, 0.6f));
        if (lifetime > 0f)
        {
            ConstantRate(heads.Spawn, spawnRate);
            Set(heads.Init, "lifetime", lifetime);
        }
        else Burst(heads.Spawn, 3f);
        SetRandom(heads.Init, "position", new Vector3(-1.2f, -1.2f, 0f), new Vector3(-0.8f, 1.2f, 0f));
        SetRandom(heads.Init, "velocity", new Vector3(1f, -0.5f, 0f), new Vector3(1.5f, 0.5f, 0f));
        return Trigger(heads.Update, TriggerEvent.Mode.OverTime, rate);
    }

    static void Strips()
    {
        // One strip of sixteen: twenty particles a second at random places, each living three
        // fifths of a second, so that the strip's first particle moves on round its room.
        var single = NewGraph("StripSingle");
        var one = Strips(single, 1, 16);
        ConstantRate(one.Spawn, 20f);
        Scatter(one.Init, 0.2f, new Vector3(0.9f, 0.9f, 0.3f), 0.6f);
        Output<VFXQuadStripOutput>(single, one.Update);
        Save(single);

        // The same graph as an asset whose effects are not instanced.
        var alone = NewGraph("StripAlone");
        var lone = Strips(alone, 1, 16);
        ConstantRate(lone.Spawn, 20f);
        Scatter(lone.Init, 0.2f, new Vector3(0.9f, 0.9f, 0.3f), 0.6f);
        Output<VFXQuadStripOutput>(alone, lone.Update);
        var settings = new SerializedObject(alone.GetResource());
        settings.FindProperty("m_Infos.m_InstancingMode").intValue = -1;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Save(alone);

        // No strips: quads that live three fifths of a second, of a graph that is instanced;
        // and the same graph again, for effects that wake one frame after the other.
        foreach (var name in new[] { "Dying", "Waking" })
        {
            var dying = NewGraph(name);
            var mortal = System<VFXPlanarPrimitiveOutput>(dying, 16);
            mortal.Output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
            ConstantRate(mortal.Spawn, 20f);
            Scatter(mortal.Init, 0.2f, new Vector3(0.9f, 0.9f, 0.3f), 0.6f);
            Save(dying);
        }

        // A strip blended over what is behind it, half transparent, each particle of a colour
        // and at a depth of its own: where the strip crosses itself the order its segments
        // are drawn in shows.
        var alpha = NewGraph("StripAlpha");
        var sheer = Strips(alpha, 1, 16);
        ConstantRate(sheer.Spawn, 20f);
        SetRandom(sheer.Init, "position", new Vector3(-1.2f, -1.2f, -2f), new Vector3(1.2f, 1.2f, 2f));
        Set(sheer.Init, "lifetime", 0.6f);
        Set(sheer.Init, "size", 0.3f);
        SetRandom(sheer.Init, "color", new Vector3(0.1f, 0.1f, 0.1f), new Vector3(1f, 1f, 1f));
        Set(sheer.Init, "alpha", 0.5f);
        Output<VFXQuadStripOutput>(alpha, sheer.Update).SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
        Save(alpha);

        // Two strips of six whose particles never die, all spawned into the first: it fills
        // while the system still has room.
        var keepTwo = NewGraph("StripKeepTwo");
        var keptTwo = Strips(keepTwo, 2, 6);
        ConstantRate(keptTwo.Spawn, 10f);
        Scatter(keptTwo.Init, 0.15f, new Vector3(0.4f, 0.9f, 1f), 0f);
        Output<VFXQuadStripOutput>(keepTwo, keptTwo.Update);
        Save(keepTwo);

        // One strip of twelve whose particles never die: it fills, and takes no more.
        var keep = NewGraph("StripKeep");
        var kept = Strips(keep, 1, 12);
        ConstantRate(kept.Spawn, 10f);
        Scatter(kept.Init, 0.15f, new Vector3(0.4f, 0.9f, 1f), 0f);
        Output<VFXQuadStripOutput>(keep, kept.Update);
        Save(keep);

        // Three particles that move, each drawing a strip behind it: twenty particles a
        // second, in the strip numbered as the particle that spawned them.
        var trails = NewGraph("StripTrails");
        var tails = Trails(trails, Heads(trails, 20f), 4, 16);
        Inherit(tails.Init, "position");
        Set(tails.Init, "lifetime", 0.5f);
        Set(tails.Init, "size", 0.1f);
        SetVector(tails.Init, "color", new Vector3(1f, 0.4f, 0.8f));
        Output<VFXQuadStripOutput>(trails, tails.Update);
        Save(trails);

        // The same whose strips' particles never die: each strip fills and stops growing.
        var trailsKeep = NewGraph("StripTrailsKeep");
        var stay = Trails(trailsKeep, Heads(trailsKeep, 20f), 4, 12);
        Inherit(stay.Init, "position");
        Set(stay.Init, "size", 0.1f);
        SetVector(stay.Init, "color", new Vector3(1f, 0.8f, 0.3f));
        Output<VFXQuadStripOutput>(trailsKeep, stay.Update);
        Save(trailsKeep);

        // Heads that die (two fifths of a second, six a second into room for four), each drawing
        // a strip behind it: a dead head is kept, not drawn, until the strip it spawned is empty
        // (the `stripAlive` attribute); its place is free for a new head after that. The
        // strips' particles die after half a second, and in `StripTrailsAliveKeep` never.
        foreach (var (name, dying) in new[] { ("StripTrailsAlive", true), ("StripTrailsAliveKeep", false) })
        {
            var graph = NewGraph(name);
            var tail = Trails(graph, Heads(graph, 20f, 6f, 0.4f), 4, 12);
            Inherit(tail.Init, "position");
            if (dying) Set(tail.Init, "lifetime", 0.5f);
            Set(tail.Init, "size", 0.1f);
            SetVector(tail.Init, "color", new Vector3(0.4f, 0.7f, 1f));
            Output<VFXQuadStripOutput>(graph, tail.Update);
            Save(graph);
        }

        // Four strips of eight; the strip a particle goes into is a property of the effect.
        var index = NewGraph("StripIndex");
        var chosen = Strips(index, 4, 8);
        ConstantRate(chosen.Spawn, 12f);
        Scatter(chosen.Init, 0.15f, new Vector3(0.9f, 0.5f, 0.3f), 1f);
        Link(Parameter(index, typeof(uint), "Strip", 0u).outputSlots[0], Input(chosen.Init, "stripIndex"));
        Output<VFXQuadStripOutput>(index, chosen.Update);
        Save(index);

        // A strip drawn twice: as a strip of quads, and each of its particles as a quad.
        var both = NewGraph("StripBoth");
        var twice = Strips(both, 1, 16);
        ConstantRate(twice.Spawn, 20f);
        Scatter(twice.Init, 0.2f, new Vector3(0.9f, 0.9f, 0.3f), 0.6f);
        Output<VFXQuadStripOutput>(both, twice.Update);
        var dots = Output<VFXPlanarPrimitiveOutput>(both, twice.Update, false);
        Set(dots, "size", 0.12f);
        SetVector(dots, "color", new Vector3(1f, 0.2f, 0.2f));
        Save(both);

        // A strip that widens along itself: each particle's size times its place in the strip.
        var taper = NewGraph("StripTaper");
        var tapered = Strips(taper, 1, 16);
        ConstantRate(tapered.Spawn, 20f);
        Scatter(tapered.Init, 0.03f, new Vector3(0.8f, 0.6f, 1f), 0.6f);
        var taperOutput = Output<VFXQuadStripOutput>(taper, tapered.Update);
        var place = Make<VFXAttributeParameter>();
        place.SetSettingValue("attribute", "particleIndexInStrip");
        taper.AddChild(place);
        var widen = Set(taperOutput, "size", 1f);
        widen.SetSettingValue("Composition", AttributeCompositionMode.Multiply);
        Link(place.outputSlots[0], widen.inputSlots[0]);
        Save(taper);

        // Strips that draw an image: stretched along the strip, once for each segment, and
        // stretched with its two directions swapped.
        foreach (var (name, tiling, swap) in new[]
        {
            ("StripImage", VFXAbstractParticleOutput.StripTilingMode.Stretch, false),
            ("StripImageSegments", VFXAbstractParticleOutput.StripTilingMode.RepeatPerSegment, false),
            ("StripImageSwapped", VFXAbstractParticleOutput.StripTilingMode.Stretch, true),
        })
        {
            var image = NewGraph(name);
            var drawn = Strips(image, 1, 8);
            ConstantRate(drawn.Spawn, 10f);
            Scatter(drawn.Init, 0.4f, new Vector3(1f, 1f, 1f), 0.6f);
            var imageOutput = Output<VFXQuadStripOutput>(image, drawn.Update);
            imageOutput.SetSettingValue("tilingMode", tiling);
            imageOutput.SetSettingValue("swapUV", swap);
            Input(imageOutput, "mainTexture").value = Quarters;
            Save(image);
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
