using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.VFX;
using UnityEditor.VFX.Block;
using UnityEditor.VFX.Operator;
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
    /// <summary>The simulation controls' graphs, apart from the graphs the import rows list.</summary>
    const string SimulationFolder = "Assets/SimGraphs";
    const string ObjectFolder = "Assets/Objects";
    public const string Scene = "Assets/Scenes/Probe.unity";

    public static void Author()
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(SimulationFolder);
        Directory.CreateDirectory(ObjectFolder);
        AssetDatabase.Refresh();
        Objects();
        Empty();
        Basic();
        Exposed();
        Exposed2();
        Events();
        Attributes();
        Attributes2();
        Simulated();
        Expressions();
        Chained();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildScene();
    }

    /// <summary>Two textures and two meshes for object-typed properties: defaults and overrides.</summary>
    static void Objects()
    {
        foreach (var (name, color) in new[] { ("ImageA", Color.red), ("ImageB", Color.green) })
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { color, color, color, color });
            File.WriteAllBytes(ObjectFolder + "/" + name + ".png", texture.EncodeToPNG());
        }
        foreach (var name in new[] { "ShapeA", "ShapeB" })
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.right },
                triangles = new[] { 0, 1, 2 },
            };
            AssetDatabase.CreateAsset(mesh, ObjectFolder + "/" + name + ".asset");
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static T Object<T>(string name) where T : UnityEngine.Object =>
        AssetDatabase.LoadAssetAtPath<T>(ObjectFolder + "/" + name + (typeof(T) == typeof(Mesh) ? ".asset" : ".png"));

    /// <summary>A block that initializes an attribute from the spawn event's value of it: the
    /// attribute becomes part of the graph's event attributes.</summary>
    static void Inherit(VFXContext context, string attribute)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        block.SetSettingValue("Source", SetAttribute.ValueSource.Source);
        context.AddChild(block);
    }

    static T Make<T>() where T : VFXModel => ScriptableObject.CreateInstance<T>();

    static VFXGraph NewGraph(string name, string folder = Folder)
    {
        var asset = VisualEffectAssetEditorUtility.CreateNewAsset(folder + "/" + name + ".vfx");
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
        Parameter(graph, typeof(Texture2D), "Image", Object<Texture2D>("ImageA"));
        Parameter(graph, typeof(Texture3D), "Volume");
        Parameter(graph, typeof(Cubemap), "Sky");
        Parameter(graph, typeof(Texture2DArray), "Layers");
        Parameter(graph, typeof(CubemapArray), "Skies");
        Parameter(graph, typeof(Mesh), "Shape", Object<Mesh>("ShapeA"));
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

    /// <summary>A system whose particles take several attributes of different types from the
    /// spawn event: floats, vectors, a bool and an unsigned integer.</summary>
    static void Attributes()
    {
        var graph = NewGraph("Attributes");
        var (spawn, init, _, _) = System(graph, 8);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        rate.inputSlots[0].value = 2f;
        foreach (var attribute in new[] { "lifetime", "velocity", "color", "alive", "seed" }) Inherit(init, attribute);
        Save(graph);
    }

    /// <summary>Some of the same event attributes as Attributes in another order, and one of its own.</summary>
    static void Attributes2()
    {
        var graph = NewGraph("Attributes2");
        var (spawn, init, _, _) = System(graph, 8);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        rate.inputSlots[0].value = 2f;
        foreach (var attribute in new[] { "color", "size", "lifetime" }) Inherit(init, attribute);
        Save(graph);
    }

    static VFXSlot Input(VFXModel model, string name) => ((IVFXSlotContainer)model).inputSlots.First(s => s.name == name);

    /// <summary>A system whose particles never die (no lifetime): its alive count is the
    /// particles spawned so far, up to its capacity.</summary>
    static (VFXGraph Graph, VFXBasicSpawner Spawn, VFXBasicInitialize Init) Counting(string name, uint capacity)
    {
        var graph = NewGraph(name, SimulationFolder);
        var (spawn, init, _, _) = System(graph, capacity);
        return (graph, spawn, init);
    }

    static void ConstantRate(VFXBasicSpawner spawn, float rate)
    {
        var block = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(block);
        block.inputSlots[0].value = rate;
    }

    static void Burst(VFXBasicSpawner spawn, bool periodic, object count, object delay)
    {
        var block = Make<VFXSpawnerBurst>();
        spawn.AddChild(block);
        block.SetSettingValue("repeat", periodic ? VFXSpawnerBurst.RepeatMode.Periodic : VFXSpawnerBurst.RepeatMode.Single);
        block.SetSettingValue("spawnMode", count is Vector2 ? VFXSpawnerBurst.RandomMode.Random : VFXSpawnerBurst.RandomMode.Constant);
        block.SetSettingValue("delayMode", delay is Vector2 ? VFXSpawnerBurst.RandomMode.Random : VFXSpawnerBurst.RandomMode.Constant);
        Input(block, "Count").value = count;
        Input(block, "Delay").value = delay;
    }

    /// <summary>The graphs the simulation controls run (README.md, "Simulation").</summary>
    static void Simulated()
    {
        // A constant rate that is no whole number per step, into a capacity it fills; the same
        // with each update mode, with the simulation not culled, and with a prewarm.
        foreach (var (name, mode, culling, prewarm) in new[]
        {
            ("SimRate", VFXUpdateMode.FixedDeltaTime, VFXCullingFlags.CullDefault, 0u),
            ("SimRateDelta", VFXUpdateMode.DeltaTime, VFXCullingFlags.CullDefault, 0u),
            ("SimRateExact", VFXUpdateMode.ExactFixedTimeStep, VFXCullingFlags.CullDefault, 0u),
            ("SimRateNoCull", VFXUpdateMode.FixedDeltaTime, VFXCullingFlags.CullNone, 0u),
            ("SimRateBoundsOnly", VFXUpdateMode.FixedDeltaTime, VFXCullingFlags.CullBoundsUpdate, 0u),
            ("SimRatePrewarm", VFXUpdateMode.FixedDeltaTime, VFXCullingFlags.CullDefault, 5u),
        })
        {
            var (graph, spawn, _) = Counting(name, 20);
            ConstantRate(spawn, 7.3f);
            var resource = graph.GetResource();
            resource.updateMode = mode;
            resource.cullingFlags = culling;
            resource.preWarmStepCount = prewarm;
            resource.preWarmDeltaTime = 0.1f;
            Save(graph);
        }

        // One burst after a delay and a periodic one, of fixed counts.
        {
            var (graph, spawn, _) = Counting("SimBurst", 64);
            Burst(spawn, false, 3f, 0.1f);
            Burst(spawn, true, 2f, 0.25f);
            Save(graph);
        }

        // Counts, delays, periods and rates drawn from ranges: the effect's random sequence.
        {
            var (graph, spawn, _) = Counting("SimRandom", 256);
            Burst(spawn, false, new Vector2(2f, 6f), new Vector2(0.05f, 0.3f));
            Burst(spawn, true, new Vector2(1f, 3f), new Vector2(0.1f, 0.2f));
            var variable = Make<VFXSpawnerVariableRate>();
            spawn.AddChild(variable);
            Input(variable, "Rate").value = new Vector2(5f, 15f);
            Input(variable, "Period").value = new Vector2(0.2f, 0.4f);
            Save(graph);
        }

        // A loop of a set duration run twice, with a delay before and after it.
        {
            var (graph, spawn, _) = Counting("SimLoop", 128);
            spawn.SetSettingValue("loopDuration", VFXBasicSpawner.LoopMode.Constant);
            spawn.SetSettingValue("loopCount", VFXBasicSpawner.LoopMode.Constant);
            spawn.SetSettingValue("delayBeforeLoop", VFXBasicSpawner.DelayMode.Constant);
            spawn.SetSettingValue("delayAfterLoop", VFXBasicSpawner.DelayMode.Constant);
            Input(spawn, "LoopDuration").value = 0.2f;
            Input(spawn, "LoopCount").value = 2;
            Input(spawn, "DelayBeforeLoop").value = 0.1f;
            Input(spawn, "DelayAfterLoop").value = 0.15f;
            ConstantRate(spawn, 30f);
            Burst(spawn, false, 5f, 0.05f);
            Save(graph);
        }

        // Loops of drawn durations and delays, without end.
        {
            var (graph, spawn, _) = Counting("SimLoopRandom", 256);
            spawn.SetSettingValue("loopDuration", VFXBasicSpawner.LoopMode.Random);
            spawn.SetSettingValue("delayBeforeLoop", VFXBasicSpawner.DelayMode.Random);
            spawn.SetSettingValue("delayAfterLoop", VFXBasicSpawner.DelayMode.Random);
            Input(spawn, "LoopDuration").value = new Vector2(0.1f, 0.3f);
            Input(spawn, "DelayBeforeLoop").value = new Vector2(0.05f, 0.1f);
            Input(spawn, "DelayAfterLoop").value = new Vector2(0.05f, 0.15f);
            ConstantRate(spawn, 20f);
            Save(graph);
        }

        // Particles that die: ten at once, living a short time or a long one.
        foreach (var (name, lifetime) in new[] { ("SimKill", 0.6f), ("SimKeep", 100f) })
        {
            var (graph, spawn, init) = Counting(name, 16);
            Burst(spawn, false, 10f, 0f);
            Set(init, "lifetime", lifetime);
            Save(graph);
        }
    }

    static void Link(VFXSlot from, VFXSlot to)
    {
        if (!from.Link(to)) throw new InvalidOperationException("cannot link " + from.property.type.Name + " '" + from.name + "' to " + to.property.type.Name + " '" + to.name + "'");
    }

    /// <summary>A graph whose spawn context has the probe's block, with operators over exposed
    /// properties (so nothing folds into a constant) connected to the block's inputs: what the
    /// block reads is what the effect's expressions evaluate to. Also a block that sets event
    /// attributes, and a particle lifetime taken from a texture.</summary>
    static void Expressions()
    {
        var (graph, spawn, init) = Counting("SimExpressions", 8);
        ConstantRate(spawn, 30f);
        var block = Make<VFXSpawnerCustomWrapper>();
        block.SetSettingValue("m_customType", new SerializableType(typeof(ProbeInputs)));
        spawn.AddChild(block);
        int[] next = new int[8];
        // An input a value cannot be connected to (no conversion between the types) keeps its default.
        void To(char kind, int index, VFXSlot value) => value.Link(Input(block, kind + (next[index]++).ToString("00")));
        void Floats(params VFXSlot[] values) { foreach (var value in values) To('f', 0, value); }
        void Ints(params VFXSlot[] values) { foreach (var value in values) To('i', 1, value); }
        void Uints(params VFXSlot[] values) { foreach (var value in values) To('u', 2, value); }
        void Bools(params VFXSlot[] values) { foreach (var value in values) To('b', 3, value); }
        void Vectors(params VFXSlot[] values) { foreach (var value in values) To('v', 4, value); }
        void Quads(params VFXSlot[] values) { foreach (var value in values) To('q', 5, value); }
        void GameTimes(params VFXSlot[] values) { foreach (var value in values) To('g', 6, value); }
        void Matrices(params VFXSlot[] values) { foreach (var value in values) To('m', 7, value); }

        VFXSlot Exposed(Type type, string name, object value) => Parameter(graph, type, name, value).outputSlots[0];
        VFXSlot A = Exposed(typeof(float), "A", 2.5f), B = Exposed(typeof(float), "B", -2.5f), C = Exposed(typeof(float), "C", 0.75f), Z = Exposed(typeof(float), "Z", 0f);
        VFXSlot I = Exposed(typeof(int), "I", -9), J = Exposed(typeof(int), "J", 0), K = Exposed(typeof(int), "K", 4);
        VFXSlot U = Exposed(typeof(uint), "U", 4294967280u), V = Exposed(typeof(uint), "V", 33u), W = Exposed(typeof(uint), "W", 0u);
        VFXSlot P = Exposed(typeof(bool), "P", true), Q = Exposed(typeof(bool), "Q", false);
        VFXSlot T = Exposed(typeof(Vector3), "T", new Vector3(1f, 2f, 3f)), S = Exposed(typeof(Vector3), "S", new Vector3(0.5f, -4f, 8f));
        VFXSlot R = Exposed(typeof(Vector4), "R", new Vector4(1f, 2f, 3f, 1f));
        var frame = Exposed(typeof(UnityEditor.VFX.Transform), "Frame", new UnityEditor.VFX.Transform { position = new Vector3(10f, 20f, 30f), angles = new Vector3(30f, 45f, 60f), scale = new Vector3(2f, 3f, 4f) });

        VFXSlot Uniform<O>(Type type, params VFXSlot[] inputs) where O : VFXOperatorNumericUniform
        {
            var op = Make<O>();
            graph.AddChild(op);
            op.SetOperandType(type);
            for (int k = 0; k < inputs.Length; k++) Link(inputs[k], op.inputSlots[k]);
            return op.outputSlots[0];
        }
        VFXSlot Cascaded<O>(Type type, VFXSlot a, VFXSlot b) where O : VFXOperatorNumericCascadedUnified
        {
            var op = Make<O>();
            graph.AddChild(op);
            op.SetOperandType(0, type);
            op.SetOperandType(1, type);
            Link(a, op.inputSlots[0]);
            Link(b, op.inputSlots[1]);
            return op.outputSlots[0];
        }
        VFXSlot Plain<O>(params VFXSlot[] inputs) where O : VFXOperator
        {
            var op = Make<O>();
            graph.AddChild(op);
            for (int k = 0; k < inputs.Length; k++) if (inputs[k] != null) Link(inputs[k], op.inputSlots[k]);
            return op.outputSlots[0];
        }
        VFXSlot Compare(Type type, VFXCondition condition, VFXSlot a, VFXSlot b)
        {
            var op = Make<Condition>();
            graph.AddChild(op);
            op.SetOperandType(type);
            op.SetSettingValue("condition", condition);
            Link(a, op.inputSlots[0]);
            Link(b, op.inputSlots[1]);
            return op.outputSlots[0];
        }
        VFXSlot Choose(Type type, VFXSlot predicate, VFXSlot a, VFXSlot b)
        {
            var op = Make<Branch>();
            graph.AddChild(op);
            op.SetOperandType(type);
            Link(predicate, op.inputSlots[0]);
            Link(a, op.inputSlots[1]);
            Link(b, op.inputSlots[2]);
            return op.outputSlots[0];
        }

        Type f = typeof(float), i = typeof(int), u = typeof(uint), v3 = typeof(Vector3);
        // Functions of one float, and of two.
        Floats(Uniform<Sine>(f, A), Uniform<Cosine>(f, A), Uniform<Tangent>(f, C), Uniform<Asin>(f, C), Uniform<Acos>(f, C), Uniform<Atan>(f, B),
            Uniform<Absolute>(f, B), Uniform<Sign>(f, B), Uniform<Sign>(f, Z), Uniform<Saturate>(f, A), Uniform<Saturate>(f, B), Uniform<Ceiling>(f, B),
            Uniform<Round>(f, A), Uniform<Round>(f, B), Uniform<Fractional>(f, B), Uniform<Floor>(f, B),
            Cascaded<Add>(f, A, C), Cascaded<Subtract>(f, A, C), Cascaded<Multiply>(f, A, B), Cascaded<Divide>(f, A, C), Cascaded<Divide>(f, A, Z),
            Cascaded<Minimum>(f, C, B), Cascaded<Maximum>(f, C, B), Cascaded<Power>(f, A, C), Uniform<Atan2>(f, A, B), Uniform<Modulo>(f, A, C), Uniform<Log>(f, A));
        // Values of other types converted where they meet a float input, and a branch each way.
        Floats(I, U, P, Choose(f, P, A, C), Choose(f, Q, A, C));
        // A number drawn once for the effect's seed, and one drawn at each evaluation.
        foreach (bool constant in new[] { true, false })
        {
            var random = Make<UnityEditor.VFX.Operator.Random>();
            graph.AddChild(random);
            random.SetSettingValue("seed", VFXSeedMode.PerVFXComponent);
            random.SetSettingValue("constant", constant);
            Link(C, random.inputSlots[0]);
            Link(A, random.inputSlots[1]);
            Floats(random.outputSlots[0]);
        }
        // Whole numbers.
        Ints(Cascaded<Add>(i, I, K), Cascaded<Subtract>(i, I, K), Cascaded<Multiply>(i, I, K), Cascaded<Divide>(i, I, K), Cascaded<Divide>(i, I, J),
            Cascaded<Minimum>(i, I, K), Cascaded<Maximum>(i, I, K), Uniform<Absolute>(i, I), Uniform<Sign>(i, I), B, U);
        Uints(Cascaded<Add>(u, U, V), Cascaded<Subtract>(u, W, V), Cascaded<Multiply>(u, U, V), Cascaded<Divide>(u, U, V), Cascaded<Divide>(u, U, W),
            Cascaded<Minimum>(u, U, V), Cascaded<Maximum>(u, U, V),
            Plain<BitwiseAnd>(U, V), Plain<BitwiseOr>(U, V), Plain<BitwiseXor>(U, V), Plain<BitwiseLeftShift>(U, V), Plain<BitwiseRightShift>(U, V), Plain<BitwiseComplement>(V),
            B, I, A);
        // Comparisons of each kind and type, and logic.
        Bools(Compare(f, VFXCondition.Equal, A, A), Compare(f, VFXCondition.NotEqual, A, C), Compare(f, VFXCondition.Less, A, C), Compare(f, VFXCondition.LessOrEqual, A, A),
            Compare(f, VFXCondition.Greater, A, C), Compare(f, VFXCondition.GreaterOrEqual, C, A), Compare(i, VFXCondition.Less, I, K), Compare(u, VFXCondition.Greater, U, V),
            Compare(f, VFXCondition.Less, Z, A), Compare(f, VFXCondition.Equal, Z, Z),
            Plain<LogicalAnd>(P, Q), Plain<LogicalOr>(P, Q), Plain<LogicalNot>(P), Plain<LogicalNand>(P, Q), Plain<LogicalNor>(P, Q), I);
        // Vectors, component by component, and through the frame's matrix and its inverse.
        var inverse = Plain<InverseTRSMatrix>(frame);
        Vectors(Cascaded<Add>(v3, T, S), Cascaded<Multiply>(v3, T, S), Uniform<Absolute>(v3, S),
            Plain<TransformPosition>(frame, T), Plain<TransformVector>(frame, T), Plain<TransformDirection>(frame, T));
        VFXSlot transposed = Plain<TransposeMatrix>(inverse), product = Plain<TransformMatrix>(frame, inverse);
        Quads(Plain<TransformVector4>(frame, R), Plain<TransformVector4>(inverse, R), Plain<TransformVector4>(transposed, R), Plain<TransformVector4>(product, R));
        Matrices(frame, inverse, transposed, product);
        // The effect's and the game's time, and the spawn context's own state.
        var time = Make<VFXDynamicBuiltInParameter>();
        graph.AddChild(time);
        time.SetSettingValue("m_BuiltInParameters", VFXDynamicBuiltInParameter.BuiltInFlag.VfxDeltaTime | VFXDynamicBuiltInParameter.BuiltInFlag.VfxUnscaledDeltaTime
            | VFXDynamicBuiltInParameter.BuiltInFlag.VfxTotalTime | VFXDynamicBuiltInParameter.BuiltInFlag.VfxPlayRate | VFXDynamicBuiltInParameter.BuiltInFlag.VfxManagerFixedTimeStep
            | VFXDynamicBuiltInParameter.BuiltInFlag.VfxManagerMaxDeltaTime);
        foreach (var output in time.outputSlots) Floats(output);
        var game = Make<VFXDynamicBuiltInParameter>();
        graph.AddChild(game);
        game.SetSettingValue("m_BuiltInParameters", VFXDynamicBuiltInParameter.BuiltInFlag.GameDeltaTime | VFXDynamicBuiltInParameter.BuiltInFlag.GameUnscaledDeltaTime
            | VFXDynamicBuiltInParameter.BuiltInFlag.GameSmoothDeltaTime | VFXDynamicBuiltInParameter.BuiltInFlag.GameTotalTime | VFXDynamicBuiltInParameter.BuiltInFlag.GameUnscaledTotalTime
            | VFXDynamicBuiltInParameter.BuiltInFlag.GameTotalTimeSinceSceneLoad | VFXDynamicBuiltInParameter.BuiltInFlag.GameTimeScale);
        foreach (var output in game.outputSlots) GameTimes(output);
        var seed = Make<VFXDynamicBuiltInParameter>();
        graph.AddChild(seed);
        seed.SetSettingValue("m_BuiltInParameters", VFXDynamicBuiltInParameter.BuiltInFlag.SystemSeed);
        Uints(seed.outputSlots[0]);
        var state = Make<SpawnState>();
        graph.AddChild(state);
        Bools(state.outputSlots.First(s => s.name == "NewLoop"));
        Uints(state.outputSlots.First(s => s.name == "LoopState"));
        // The manager's update count: the last unsigned input (the probe records how it grows).
        var frameIndex = Make<VFXDynamicBuiltInParameter>();
        graph.AddChild(frameIndex);
        frameIndex.SetSettingValue("m_BuiltInParameters", VFXDynamicBuiltInParameter.BuiltInFlag.VfxFrameIndex);
        Link(frameIndex.outputSlots[0], Input(block, "u19"));
        Ints(state.outputSlots.First(s => s.name == "LoopIndex"), state.outputSlots.First(s => s.name == "LoopCount"));
        Floats(state.outputSlots.First(s => s.name == "SpawnCount"), state.outputSlots.First(s => s.name == "SpawnDeltaTime"), state.outputSlots.First(s => s.name == "SpawnTotalTime"),
            state.outputSlots.First(s => s.name == "LoopDuration"), state.outputSlots.First(s => s.name == "DelayBeforeLoop"), state.outputSlots.First(s => s.name == "DelayAfterLoop"));

        // A block that sets two event attributes; the particles take them from the event.
        foreach (var (attribute, value) in new[] { ("lifetime", Cascaded<Add>(f, A, C)), ("color", T) })
        {
            var set = Make<VFXSpawnerSetAttribute>();
            set.SetSettingValue("attribute", attribute);
            spawn.AddChild(set);
            Link(value, set.inputSlots[0]);
        }
        Inherit(init, "color");
        Save(graph);

        // Ten particles whose lifetime is a hundred times the red of a texture: with the
        // default image (red) they live, with a green one they die at once.
        var (textured, texturedSpawn, texturedInit) = Counting("SimTexture", 16);
        Burst(texturedSpawn, false, 10f, 0f);
        var image = Parameter(textured, typeof(Texture2D), "Image", Object<Texture2D>("ImageA"));
        var sample = Make<SampleTexture2D>();
        textured.AddChild(sample);
        Link(image.outputSlots[0], sample.inputSlots[0]);
        var hundredfold = Make<Multiply>();
        textured.AddChild(hundredfold);
        Link(sample.outputSlots[0][0], hundredfold.inputSlots[0]);
        hundredfold.inputSlots[1].value = 100f;
        var lifetime = Set(texturedInit, "lifetime", 1f);
        Link(hundredfold.outputSlots[0], lifetime.inputSlots[0]);
        Save(textured);
    }

    /// <summary>Spawn contexts wired to each other: the first's burst starts the second, a
    /// third's burst stops it.</summary>
    static void Chained()
    {
        var graph = NewGraph("SimChain", SimulationFolder);
        var (second, init, _, _) = System(graph, 64);
        ConstantRate(second, 30f);
        var first = Make<VFXBasicSpawner>();
        graph.AddChild(first);
        Burst(first, false, 2f, 0.1f);
        first.LinkTo(second, 0, 0);
        var third = Make<VFXBasicSpawner>();
        graph.AddChild(third);
        Burst(third, false, 1f, 0.3f);
        third.LinkTo(second, 0, 1);
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
        var ramp = new Gradient();
        ramp.SetKeys(new[] { new GradientColorKey(Color.yellow, 0.25f), new GradientColorKey(Color.cyan, 0.75f) },
            new[] { new GradientAlphaKey(0.25f, 0.125f), new GradientAlphaKey(0.75f, 0.5f) });
        overridden.SetGradient("Ramp", ramp);
        overridden.SetTexture("Image", Object<Texture2D>("ImageB"));
        overridden.SetMesh("Shape", Object<Mesh>("ShapeB"));
        overridden.startSeed = 1234;
        overridden.resetSeedOnPlay = false;
        overridden.initialEventName = "Custom";
        overridden.transform.position = new Vector3(3f, 0f, 0f);

        var events = new GameObject("Events").AddComponent<VisualEffect>();
        events.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Folder + "/Events.vfx");
        events.transform.position = new Vector3(-3f, 0f, 0f);

        var expressions = new GameObject("Expressions").AddComponent<VisualEffect>();
        expressions.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(SimulationFolder + "/SimExpressions.vfx");
        expressions.startSeed = 7;
        expressions.resetSeedOnPlay = false;
        expressions.SetFloat("C", 0.5f);

        var none = new GameObject("NoAsset").AddComponent<VisualEffect>();
        none.transform.position = new Vector3(0f, 3f, 0f);

        EditorSceneManager.SaveScene(scene, Scene);
    }
}
