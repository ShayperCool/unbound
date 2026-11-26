using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The SpeedTree 8 wind's rules (tests/projects/speedtree8wind measures them in the
    /// Editor): the calm constants, the phases a step behind, the branch anchor, the directional
    /// adherences, the leaf twitch and the previous frame's constants.</summary>
    public class SpeedTree8WindTests
    {
        const float Dt = 1f / 30f;

        static float[] Flat(float value) => Enumerable.Repeat(value, 10).ToArray();

        /// <summary>Flat curves: every part's value is the same at any strength; the oscillation of
        /// part k is k + 1.</summary>
        static SpeedTreeWindConfig8 Config(bool directional1 = false, bool directional2 = false, float leafOscillation = 6f)
        {
            SpeedTreeWindConfig8.Branch Branch(float n) => new SpeedTreeWindConfig8.Branch
            {
                Distance = Flat(n), DirectionAdherence = Flat(n + 0.5f), Whip = Flat(n + 0.25f),
                Turbulence = n + 1f, Twitch = n + 2f, TwitchFreqScale = n + 3f,
            };
            SpeedTreeWindConfig8.Leaf Leaf(float n) => new SpeedTreeWindConfig8.Leaf
            {
                RippleDistance = Flat(n), TumbleFlip = Flat(n + 1f), TumbleTwist = Flat(n + 2f),
                TumbleDirectionAdherence = Flat(n + 3f), TwitchThrow = Flat(n + 4f), TwitchSharpness = 0.5f, LeewardScalar = n + 5f,
            };
            var options = new bool[32];
            options[3] = directional1;
            options[9] = directional2;
            var oscillation = Enumerable.Range(0, 10).Select(k => Flat(k + 1f)).ToArray();
            oscillation[5] = Flat(leafOscillation);
            return new SpeedTreeWindConfig8
            {
                StrengthResponse = 3f, DirectionResponse = 1.5f, AnchorOffset = 0f, AnchorDistanceScale = 0.5f,
                Oscillation = oscillation, GlobalHeight = 4f, GlobalHeightExponent = 1.25f,
                GlobalDistance = Flat(0.75f), GlobalDirectionAdherence = Flat(0.125f),
                BranchLevels = new[] { Branch(10f), Branch(20f) }, LeafGroups = new[] { Leaf(30f), Leaf(40f) },
                FrondRippleDistance = Flat(0.5f), FrondRippleTile = 7f, FrondRippleLightingScalar = 8f,
                BranchWindAnchor = new Vector3(0f, 3f, 0f), MaxBranchLevel1Length = 2f, Options = options,
            };
        }

        static Vector4 Value(SpeedTree8WindState wind, int index) => (Vector4)wind.Values[index];

        [Fact]
        public void ACalmWindKeepsZeroPhasesAndTheAssetsValues()
        {
            var wind = new SpeedTree8WindState();
            var config = Config();
            for (int frame = 1; frame <= 3; frame++) wind.Update(frame * Dt, Vector3.zero, 0f, config);
            Assert.False(wind.Blowing);
            Assert.Equal(Vector4.zero, Value(wind, 0));
            Assert.Equal(new Vector4(0f, 0.75f, 0.25f, 1.25f), Value(wind, 1));
            Assert.Equal(new Vector4(0f, 10f, 0f, 20f), Value(wind, 2));
            Assert.Equal(new Vector4(12f, 13f, 22f, 23f), Value(wind, 3));
            Assert.Equal(new Vector4(10.25f, 20.25f, 0f, 0f), Value(wind, 4));
            Assert.Equal(new Vector4(11f, 21f, 0f, 0f), Value(wind, 7));
            Assert.Equal(new Vector4(0f, 0.5f, 7f, 8f), Value(wind, 14));
        }

        [Fact]
        public void ThePhasesAdvanceByTheOscillationAStepBehind()
        {
            var wind = new SpeedTree8WindState();
            var config = Config();
            wind.Update(Dt, Vector3.forward, 1f, config);
            // The first windy update starts the clock (no step); each update draws the phases
            // from before its own step.
            wind.Update(2 * Dt, Vector3.forward, 1f, config);
            Assert.Equal(0f, Value(wind, 1).x);
            for (int frame = 3; frame <= 5; frame++)
            {
                wind.Update(frame * Dt, Vector3.forward, 1f, config);
                float steps = (frame - 2) * Dt;
                Assert.Equal(1f * steps, Value(wind, 1).x, 5);
                Assert.Equal(2f * steps, Value(wind, 2).x, 5);
                Assert.Equal(3f * steps, Value(wind, 2).z, 5);
                Assert.Equal(6f * steps, Value(wind, 10).z, 5);
                Assert.Equal(10f * steps, Value(wind, 14).x, 5);
            }
        }

        [Fact]
        public void TheAnchorMovesAlongTheWindByTheFirstLevelsReach()
        {
            var wind = new SpeedTree8WindState();
            var config = Config();
            // (The wind turns from +x: it has arrived along +z long after.)
            for (int frame = 1; frame <= 3000; frame++) wind.Update(frame * Dt, Vector3.forward, 1f, config);
            // (0, 3, 0) + 2 * 0.5 * (0, 0, 1).
            float length = Mathf.Sqrt(10f);
            var anchor = Value(wind, 5);
            Assert.Equal(0f, anchor.x);
            Assert.Equal(3f / length, anchor.y, 6);
            Assert.Equal(1f / length, anchor.z, 6);
            Assert.Equal(length, anchor.w, 6);

            // A calm wind has no direction to move it: the asset's anchor alone.
            var calm = new SpeedTree8WindState();
            calm.Update(Dt, Vector3.zero, 0f, config);
            Assert.Equal(new Vector4(0f, 1f, 0f, 3f), Value(calm, 5));
        }

        [Fact]
        public void ADirectionalLevelsAdherenceIsScaledByTheFirstLevelsLength()
        {
            var plain = new SpeedTree8WindState();
            plain.Update(Dt, Vector3.zero, 0f, Config());
            Assert.Equal(new Vector4(0.125f, 10.5f, 20.5f, 0f), Value(plain, 6));
            var directional = new SpeedTree8WindState();
            directional.Update(Dt, Vector3.zero, 0f, Config(directional1: true, directional2: true));
            Assert.Equal(new Vector4(0.125f, 21f, 41f, 0f), Value(directional, 6));
        }

        [Fact]
        public void TheLeafTwitchSharpnessIsOverItsOscillation()
        {
            var wind = new SpeedTree8WindState();
            wind.Update(Dt, Vector3.zero, 0f, Config(leafOscillation: 2f));
            Assert.Equal(new Vector4(34f, 1f / 2f * 0.5f * 10f, 0f, 0f), Value(wind, 10));
            // Leaf group 2's oscillation (part 8) is 9.
            Assert.Equal(1f / 9f * 0.5f * 10f, Value(wind, 13).y);
            var still = new SpeedTree8WindState();
            still.Update(Dt, Vector3.zero, 0f, Config(leafOscillation: 0f));
            Assert.Equal(0f, Value(still, 10).y);
        }

        /// <summary>The (constant, component) of each of the ten phases.</summary>
        static readonly (int Index, int Component)[] PhaseSlots =
            { (1, 0), (2, 0), (2, 2), (8, 0), (9, 0), (10, 2), (11, 0), (12, 0), (13, 2), (14, 0) };

        [Fact]
        public void TheHistoryHoldsThePreviousFrameWithThePhasesAfterTheStep()
        {
            var wind = new SpeedTree8WindState();
            var config = Config();
            var previous = new Vector4[SpeedTree8WindState.HistoryOffset];
            var history = new Vector4[SpeedTree8WindState.HistoryOffset];
            for (int frame = 1; frame <= 6; frame++)
            {
                wind.Update(frame * Dt, Vector3.right, 0.7f, config);
                for (int i = 0; i < previous.Length; i++)
                {
                    var current = Value(wind, i);
                    if (frame > 2)
                    {
                        // The previous update's history phases are this update's drawn phases.
                        foreach (var (index, component) in PhaseSlots)
                            if (index == i) Assert.Equal(history[i][component], current[component]);
                    }
                    history[i] = Value(wind, SpeedTree8WindState.HistoryOffset + i);
                    if (frame > 1)
                    {
                        var expected = previous[i];
                        foreach (var (index, component) in PhaseSlots)
                            if (index == i) expected[component] = history[i][component];
                        Assert.Equal(expected, history[i]);
                    }
                    previous[i] = current;
                }
            }
            Assert.True(wind.Blowing);
            Assert.NotEqual(0f, Value(wind, 1).x);
        }

        /// <summary>A calm after a wind keeps its state: its frames draw the last windy update's
        /// constants, and a history at strength 0 (the last windy update's history had the
        /// strength before it); when a wind comes back, its first step is over the time since the
        /// start (the calm's timers start over).</summary>
        [Fact]
        public void ACalmKeepsTheLastWindyConstantsAndTheReturningWindStepsFromTheStart()
        {
            var wind = new SpeedTree8WindState();
            var config = Config();
            int frame = 1;
            wind.Update(frame * Dt, Vector3.right, 0.5f, config);
            for (frame = 2; frame <= 4; frame++) wind.Update(frame * Dt, Vector3.right, 0.5f, config);
            // The wind goes; its direction has arrived, so its strength comes to rest last.
            for (; wind.Blowing; frame++) wind.Update(frame * Dt, Vector3.zero, 0f, config);
            var lastWindy = Enumerable.Range(0, SpeedTree8WindState.ConstantCount).Select(i => Value(wind, i)).ToArray();
            Assert.Equal(0f, lastWindy[0].w);
            Assert.True(lastWindy[SpeedTree8WindState.HistoryOffset].w > 0f);
            // The first calm update: the same current constants and phases, the history at strength 0.
            wind.Update(frame++ * Dt, Vector3.zero, 0f, config);
            var settled = Enumerable.Range(0, SpeedTree8WindState.ConstantCount).Select(i => Value(wind, i)).ToArray();
            for (int i = 0; i < SpeedTree8WindState.HistoryOffset; i++) Assert.Equal(lastWindy[i], settled[i]);
            Assert.Equal(0f, settled[SpeedTree8WindState.HistoryOffset].w);
            Assert.Equal(lastWindy[SpeedTree8WindState.HistoryOffset + 14].x, settled[SpeedTree8WindState.HistoryOffset + 14].x);
            // The direction is kept.
            Assert.True(settled[0].x > 0.999f, settled[0].ToString());
            Assert.Equal(0f, settled[0].w);
            // The history keeps the phases after the last step, the current ones those before it.
            float lastStep = settled[SpeedTree8WindState.HistoryOffset + 14].x - settled[14].x;
            Assert.Equal(10f * Dt, lastStep, 4);
            for (int calm = 0; calm < 5; calm++, frame++)
            {
                wind.Update(frame * Dt, Vector3.zero, 0f, config);
                for (int i = 0; i < settled.Length; i++) Assert.Equal(settled[i], Value(wind, i));
            }
            float time = frame * Dt;
            wind.Update(time, Vector3.forward, 0.5f, config);
            // The first windy update draws the kept phases and steps them over the whole time.
            Assert.Equal(settled[SpeedTree8WindState.HistoryOffset + 14].x, Value(wind, 14).x);
            Assert.Equal(settled[SpeedTree8WindState.HistoryOffset + 14].x + 10f * time,
                Value(wind, SpeedTree8WindState.HistoryOffset + 14).x, 3);
        }

        /// <summary>A SpeedTree 8 wind starts along +x: a wind from the first update turns from
        /// +x toward the scene's over the direction response; a first windless update rests it
        /// (tests/projects/speedtree8wind `first`).</summary>
        [Fact]
        public void ASpeedTree8WindStartsAlongX()
        {
            var config = Config();
            var windy = new SpeedTree8WindState();
            windy.Update(Dt, Vector3.forward, 0.8f, config);
            var first = Value(windy, 0);
            Assert.Equal(1f, first.x);
            Assert.Equal(Vector4.zero.z, first.z);
            windy.Update(2 * Dt, Vector3.forward, 0.8f, config);
            var second = Value(windy, 0);
            Assert.InRange(second.x, 0.99f, 1f - 1e-7f);
            Assert.True(second.z > 0f);
            var calm = new SpeedTree8WindState();
            calm.Update(Dt, Vector3.zero, 0f, config);
            Assert.Equal(Vector4.zero, Value(calm, 0));
        }

        /// <summary>_ST_WindAnimation, which the wind never writes: a tree drawn with wind binds
        /// it (zero, where the Editor's buffer holds no repeatable value) instead of letting the
        /// global value through; before that the draw leaves it to the global value.</summary>
        [Fact]
        public void ATreeDrawnWithWindBindsTheAnimationVector()
        {
            var go = new GameObject("Tree8");
            var asset = new SpeedTreeWindAsset(Config());
            var zone = new GameObject("Sun").AddComponent<WindZone>();
            zone.gameObject.SetActive(false);
            float time = (float)Time.s_Time;
            int animation = Shader.PropertyToID("_ST_WindAnimation");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
                var renderer = go.AddComponent<MeshRenderer>();
                go.AddComponent<Tree>().windAsset = asset;
                for (int frame = 1; frame <= 3; frame++)
                {
                    Time.s_Time = frame * Dt;
                    SpeedTreeWindSystem.Update();
                }
                using (Unbound.Runtime.DrawPropertyPool.Begin())
                {
                    Assert.False(SpeedTreeWindSystem.Apply(renderer, new Dictionary<int, object>()).ContainsKey(animation));
                    zone.gameObject.SetActive(true);
                    Time.s_Time = 4 * Dt;
                    SpeedTreeWindSystem.Update();
                    var drawn = SpeedTreeWindSystem.Apply(renderer, new Dictionary<int, object>());
                    Assert.Equal(Vector4.zero, (Vector4)drawn[animation]);
                    Assert.Equal(Vector4.zero, (Vector4)drawn[Shader.PropertyToID("_ST_WindAnimationHistory")]);
                }
            }
            finally
            {
                Time.s_Time = time;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(zone.gameObject);
                Object.DestroyImmediate(asset);
                SpeedTreeWindSystem.Update();
            }
        }

        /// <summary>A spherical zone that reaches a SpeedTree 8 tree gives it a local wind of its
        /// own after its draw (as a SpeedTree 9 tree's), which eases toward the zone's wind; out of
        /// every zone's reach its phases return to the asset's wind's and the tree takes the
        /// asset's wind again (Unity 6000.0.71f1, tests/projects/speedtree8wind FieldNear); one
        /// out of reach gives none.</summary>
        [Fact]
        public void ASphericalZoneReachingASpeedTree8TreeGivesItALocalWind()
        {
            var go = new GameObject("Tree8");
            var asset = new SpeedTreeWindAsset(Config());
            var zone = new GameObject("Sphere").AddComponent<WindZone>();
            var directional = new GameObject("Sun").AddComponent<WindZone>();
            float time = (float)Time.s_Time;
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
                var renderer = go.AddComponent<MeshRenderer>();
                var tree = go.AddComponent<Tree>();
                tree.windAsset = asset;
                directional.windMain = 0.5f;
                zone.mode = WindZoneMode.Spherical;
                zone.radius = 5f;
                zone.windMain = 1f;
                zone.transform.position = new Vector3(100f, 0f, 0f);
                int frame = 0;
                void Frame()
                {
                    Time.s_Time = ++frame * Dt;
                    SpeedTreeWindSystem.Update();
                }
                for (int i = 0; i < 30; i++) Frame();
                using (Unbound.Runtime.DrawPropertyPool.Begin())
                {
                    SpeedTreeWindSystem.Apply(renderer, new Dictionary<int, object>());
                    Assert.Null(tree.m_LocalWind);
                    zone.transform.position = new Vector3(-3f, 0.25f, 0f);
                    Frame();
                    var drawn = SpeedTreeWindSystem.Apply(renderer, new Dictionary<int, object>());
                    // The draw that starts it still takes the asset's wind.
                    Assert.Equal(Value(asset.Wind8, 0), (Vector4)drawn[Shader.PropertyToID("_ST_WindVector")]);
                    Assert.NotNull(tree.m_LocalWind);
                    for (int i = 0; i < 30; i++) Frame();
                    drawn = SpeedTreeWindSystem.Apply(renderer, new Dictionary<int, object>());
                    Assert.NotEqual(Value(asset.Wind8, 0), (Vector4)drawn[Shader.PropertyToID("_ST_WindVector")]);
                    // Out of reach: the local wind returns to the asset's and is given up.
                    zone.transform.position = new Vector3(100f, 0f, 0f);
                    for (int i = 0; i < 3000 && tree.m_LocalWind != null; i++) Frame();
                    Assert.Null(tree.m_LocalWind);
                    drawn = SpeedTreeWindSystem.Apply(renderer, new Dictionary<int, object>());
                    Assert.Equal(Value(asset.Wind8, 0), (Vector4)drawn[Shader.PropertyToID("_ST_WindVector")]);
                }
            }
            finally
            {
                Time.s_Time = time;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(zone.gameObject);
                Object.DestroyImmediate(directional.gameObject);
                Object.DestroyImmediate(asset);
                SpeedTreeWindSystem.Update();
            }
        }

        /// <summary>A spherical zone reaching a tree while its asset's wind rests (after it blew)
        /// starts the tree's wind from rest: along +x with its phases at zero, not from the asset's
        /// direction and phases (tests/projects/speedtree8wind profile zones, FieldCalm f522).</summary>
        [Fact]
        public void ALocalWindStartedWhileTheAssetsWindRestsStartsFromRest()
        {
            var go = new GameObject("Tree8");
            var asset = new SpeedTreeWindAsset(Config());
            var zone = new GameObject("Sphere").AddComponent<WindZone>();
            var directional = new GameObject("Sun").AddComponent<WindZone>();
            float time = (float)Time.s_Time;
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
                var renderer = go.AddComponent<MeshRenderer>();
                var tree = go.AddComponent<Tree>();
                tree.windAsset = asset;
                directional.windMain = 0.6f;
                directional.transform.rotation = Quaternion.Euler(0f, 120f, 0f);
                zone.mode = WindZoneMode.Spherical;
                zone.radius = 5f;
                zone.windMain = 1.2f;
                zone.transform.position = new Vector3(100f, 0f, 0f);
                int frame = 0;
                void Frame()
                {
                    Time.s_Time = ++frame * Dt;
                    SpeedTreeWindSystem.Update();
                }
                for (int i = 0; i < 30; i++) Frame();
                directional.gameObject.SetActive(false);
                for (int i = 0; i < 3000 && Value(asset.Wind8, 0).w != 0f; i++) Frame();
                for (int i = 0; i < 30; i++) Frame();
                var rest = Value(asset.Wind8, 0);
                Assert.Equal(0f, rest.w);
                Assert.NotEqual(1f, rest.x);
                Assert.NotEqual(0f, Value(asset.Wind8, 1).x);
                using (Unbound.Runtime.DrawPropertyPool.Begin())
                {
                    zone.transform.position = new Vector3(-3f, 0.25f, 0f);
                    Frame();
                    var drawn = SpeedTreeWindSystem.Apply(renderer, new Dictionary<int, object>());
                    Assert.NotNull(tree.m_LocalWind);
                    Assert.Equal(rest, (Vector4)drawn[Shader.PropertyToID("_ST_WindVector")]);
                    Frame();
                    drawn = SpeedTreeWindSystem.Apply(renderer, new Dictionary<int, object>());
                    Assert.Equal(new Vector4(1f, 0f, 0f, 0f), (Vector4)drawn[Shader.PropertyToID("_ST_WindVector")]);
                    Assert.Equal(0f, ((Vector4)drawn[Shader.PropertyToID("_ST_WindGlobal")]).x);
                }
            }
            finally
            {
                Time.s_Time = time;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(zone.gameObject);
                Object.DestroyImmediate(directional.gameObject);
                Object.DestroyImmediate(asset);
                SpeedTreeWindSystem.Update();
            }
        }

        sealed class Warnings : ILogHandler
        {
            internal readonly List<string> Messages = new List<string>();
            public void LogException(System.Exception exception, Object context) => Messages.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add(logType + ": " + string.Format(format, args));
        }

        const int Stride = 80;

        static unsafe Vector4 Param(uint* buffer, int instance, int p) => *(Vector4*)(buffer + Stride * instance + 4 * (p + 2));

        /// <summary>The GPU Resident Drawer's parameters of a SpeedTree 8 tree: its wind's 15
        /// constants (the history for a renderer with per-object motion vectors, zeros for one
        /// without), a zero 16th; unmeasured, said once.</summary>
        [Fact]
        public unsafe void TheDrawerBufferGetsASpeedTree8TreesWindAndSaysItIsUnmeasured()
        {
            var objects = new[] { new GameObject("ObjectMotion"), new GameObject("CameraMotion") };
            var asset = new SpeedTreeWindAsset(Config());
            var zone = new GameObject("Wind").AddComponent<WindZone>();
            var previous = Debug.unityLogger.logHandler;
            var warnings = new Warnings();
            float time = (float)Time.s_Time;
            var data = new uint[Stride * 2];
            fixed (uint* buffer = data)
            {
                var iterator = new UnityEngine.Rendering.SpeedTreeWindParamsBufferIterator
                {
                    bufferPtr = (System.IntPtr)buffer, uintStride = Stride, elementsCount = 2,
                };
                for (int p = 0; p < 16; p++) iterator.uintParamOffsets[p] = 4 * (p + 2);
                try
                {
                    Debug.unityLogger.logHandler = warnings;
                    SpeedTreeWindSystem.s_SpeedTree8DrawerReported = false;
                    Unbound.Runtime.Stubs.Forget("SpeedTree 8 wind in the GPU Resident Drawer");
                    var ids = new int[2];
                    for (int i = 0; i < 2; i++)
                    {
                        var renderer = objects[i].AddComponent<MeshRenderer>();
                        renderer.motionVectorGenerationMode = i == 0 ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera;
                        objects[i].AddComponent<Tree>().windAsset = asset;
                        ids[i] = renderer.GetInstanceID();
                    }
                    for (int frame = 1; frame <= 3; frame++)
                    {
                        Time.s_Time = frame * Dt;
                        SpeedTreeWindSystem.Update();
                    }
                    for (int i = 0; i < data.Length; i++) data[i] = 7;
                    SpeedTreeWindSystem.WriteBuffer(ids, in iterator, false);
                    for (int i = 0; i < 2; i++)
                    {
                        for (int p = 0; p < SpeedTree8WindState.HistoryOffset; p++) Assert.Equal(Value(asset.Wind8, p), Param(buffer, i, p));
                        Assert.Equal(Vector4.zero, Param(buffer, i, 15));
                    }
                    Assert.NotEqual(Vector4.zero, Param(buffer, 0, 0));
                    SpeedTreeWindSystem.WriteBuffer(ids, in iterator, true);
                    for (int p = 0; p < SpeedTree8WindState.HistoryOffset; p++)
                    {
                        Assert.Equal(Value(asset.Wind8, SpeedTree8WindState.HistoryOffset + p), Param(buffer, 0, p));
                        Assert.Equal(Vector4.zero, Param(buffer, 1, p));
                    }
                    Assert.Single(warnings.Messages, m => m.Contains("SpeedTree 8 wind in the GPU Resident Drawer") && m.Contains("unmeasured"));
                }
                finally
                {
                    Time.s_Time = time;
                    Debug.unityLogger.logHandler = previous;
                    foreach (var o in objects) Object.DestroyImmediate(o);
                    Object.DestroyImmediate(zone.gameObject);
                    Object.DestroyImmediate(asset);
                    SpeedTreeWindSystem.Update();
                }
            }
        }
    }
}
