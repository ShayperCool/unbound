using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Visual Effect runtime on compiled graphs made by hand (no graph package, no
    /// GPU): spawner states, the expression evaluator, the frame's steps, and effects whose
    /// particle systems only count (no kernels, particles that never die).</summary>
    public partial class VisualEffectSimulationTests
    {
        const float Frame = 1f / 30f;

        static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

        internal static VFXExpressionDesc Value(VFXValueType type) =>
            new VFXExpressionDesc { op = VFXExpressionOperation.Value, data = new[] { -1, -1, -1, (int)type } };

        internal static VFXExpressionDesc Op(VFXExpressionOperation op, int a = -1, int b = -1, int c = -1, int d = -1) =>
            new VFXExpressionDesc { op = op, data = new[] { a, b, c, d } };

        internal static VFXExpressionValueContainerDesc Constant<T>(int expression, T value) =>
            new VFXExpressionValueContainerDesc<T> { expressionIndex = (uint)expression, value = value };

        internal static VisualEffectCompiledData Data(VFXExpressionDesc[] expressions, VFXExpressionValueContainerDesc[] values,
            VFXEditorSystemDesc[] systems = null, VFXEventDesc[] events = null, int spawners = 0, VFXExposedMapping[] exposed = null,
            VFXExpressionDesc[] perSpawnEvent = null, uint attributeWords = 1)
        {
            var resource = new VisualEffectResource();
            // The event attributes (one per spawner after the asset's own): a spawn count of one,
            // then as many words of zero as asked.
            var buffers = new VFXCPUBufferDesc[spawners + 1];
            for (int i = 0; i < buffers.Length; i++)
            {
                var initial = new VFXCPUBufferData();
                initial.PushFloat(1f);
                for (uint k = 1; k < attributeWords; k++) initial.PushUInt(0u);
                buffers[i] = new VFXCPUBufferDesc
                {
                    capacity = 1, stride = attributeWords, initialData = initial,
                    layout = new[] { new VFXLayoutElementDesc { name = "spawnCount", type = VFXValueType.Float, offset = new VFXLayoutOffset { structure = 1 } } },
                };
            }
            resource.SetRuntimeData(new VFXExpressionSheet
            {
                expressions = expressions, values = values, exposed = exposed ?? Array.Empty<VFXExposedMapping>(),
                expressionsPerSpawnEventAttribute = perSpawnEvent,
            },
                systems ?? Array.Empty<VFXEditorSystemDesc>(), events ?? Array.Empty<VFXEventDesc>(), null, buffers, null, null,
                ShadowCastingMode.Off, MotionVectorGenerationMode.Camera, VFXInstancingDisabledReason.None, 7);
            var data = resource.m_Data;
            data.Seal();
            return data;
        }

        static uint[] Evaluate(VisualEffectCompiledData data, VisualEffectState state = null)
        {
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, state ?? new VisualEffectState(), null);
            var result = new uint[data.Operations.Length];
            for (int i = 0; i < result.Length; i++) result[i] = data.ValueOffsets[i] < 0 ? 0 : values.GetWord(i);
            return result;
        }

        // ---------------------------------------------------------------------- spawner state

        static string Stage(VFXSpawnerState s) => $"{s.loopState} {s.loopIndex}{(s.NewLoop ? " new" : "")}";

        [Fact]
        public void ALoopDelaysLoopsDelaysAndStartsOverUntilItsCount()
        {
            var settings = new VFXSpawnerState.Settings { LoopCount = 2, DelayBeforeLoop = 0.05f, LoopDuration = 0.12f, DelayAfterLoop = 0.08f };
            var state = new VFXSpawnerState();
            state.ResetForSystem();
            state.OnPlay(settings);
            Assert.Equal("DelayingBeforeLoop 0 new", Stage(state));
            var stages = new List<string>();
            for (int step = 0; step < 20; step++)
            {
                state.BeginUpdate(Frame, settings);
                state.EndUpdate();
                stages.Add(Stage(state));
            }
            // 2 steps of delay (0.067 >= 0.05), 4 of loop (0.133 >= 0.12), 3 of delay (0.1 >= 0.08),
            // then the second loop, marked new for the step that follows its start.
            Assert.Equal(new[]
            {
                "DelayingBeforeLoop 0", "Looping 0", "Looping 0", "Looping 0", "Looping 0", "DelayingAfterLoop 0",
                "DelayingAfterLoop 0", "DelayingAfterLoop 0", "DelayingBeforeLoop 1 new", "DelayingBeforeLoop 1", "Looping 1",
                "Looping 1", "Looping 1", "Looping 1", "DelayingAfterLoop 1", "DelayingAfterLoop 1", "DelayingAfterLoop 1",
                "Finished 2", "Finished 2", "Finished 2",
            }, stages);
        }

        [Fact]
        public void StagesOfNoDurationArePassedAtOnceAndANegativeOneNeverEnds()
        {
            var state = new VFXSpawnerState();
            state.ResetForSystem();
            // No delay before: the loop is running as soon as it is played, for ever.
            var endless = new VFXSpawnerState.Settings { LoopCount = -1, LoopDuration = -1f };
            state.OnPlay(endless);
            Assert.Equal("Looping 0 new", Stage(state));
            for (int step = 0; step < 100; step++) { state.BeginUpdate(1f, endless); state.EndUpdate(); }
            Assert.Equal("Looping 0", Stage(state));
            Assert.Equal(100f, state.totalTime);

            // Nothing lasts: a new loop stays before itself rather than turn for ever.
            var nothing = new VFXSpawnerState.Settings { LoopCount = -1 };
            state.OnStop();
            Assert.Equal(VFXSpawnerLoopState.Finished, state.loopState);
            state.OnPlay(nothing);
            Assert.Equal("DelayingBeforeLoop 0 new", Stage(state));

            // One loop of no delays: over after the step its duration passes in.
            var once = new VFXSpawnerState.Settings { LoopCount = 1, LoopDuration = 0.05f };
            state.OnStop();
            state.OnPlay(once);
            Assert.Equal("Looping 0 new", Stage(state));
            state.BeginUpdate(Frame, once); state.EndUpdate();
            Assert.Equal("Looping 0", Stage(state));
            state.BeginUpdate(Frame, once); state.EndUpdate();
            Assert.Equal("Finished 1", Stage(state));
        }

        [Fact]
        public void AStateOfItsOwnLoopsWithoutEndAndItsSettersMoveItAsTheEditors()
        {
            // The values the Unity 6000.0.71f1 Editor answers (tests/projects/vfx, "spawner state").
            var state = new VFXSpawnerState();
            Assert.Equal((-1f, -1, VFXSpawnerLoopState.Finished, false), (state.loopDuration, state.loopCount, state.loopState, state.newLoop));
            state.loopDuration = 1f;
            state.playing = true;
            Assert.Equal((VFXSpawnerLoopState.Looping, true, false), (state.loopState, state.playing, state.newLoop));
            state.totalTime = 0.5f;
            // A delay of no time after the loop: the next loop starts at once, at time zero.
            state.loopState = VFXSpawnerLoopState.DelayingAfterLoop;
            Assert.Equal((VFXSpawnerLoopState.Looping, 1, 0f, true), (state.loopState, state.loopIndex, state.totalTime, state.newLoop));
            state.delayBeforeLoop = 0.25f;
            state.loopCount = 3;
            state.loopState = VFXSpawnerLoopState.DelayingBeforeLoop;
            state.totalTime = 0.125f;
            // The same stage again changes nothing (the time is kept).
            state.loopState = VFXSpawnerLoopState.DelayingBeforeLoop;
            Assert.Equal((VFXSpawnerLoopState.DelayingBeforeLoop, 0.125f, 1), (state.loopState, state.totalTime, state.loopIndex));
            state.delayAfterLoop = 0.5f;
            state.loopState = VFXSpawnerLoopState.DelayingAfterLoop;
            Assert.Equal((VFXSpawnerLoopState.DelayingAfterLoop, 0f), (state.loopState, state.totalTime));
            state.playing = false;
            Assert.Equal((VFXSpawnerLoopState.Finished, false, true), (state.loopState, state.playing, state.newLoop));
            // A state of its own has no event attributes to count particles in.
            Assert.Null(state.vfxEventAttribute);
            Assert.Equal(0f, state.spawnCount);
        }

        // -------------------------------------------------------------------------- evaluator

        [Fact]
        public void ArithmeticIsPerComponentByTheValuesType()
        {
            int f = (int)VFXValueType.Float, f3 = (int)VFXValueType.Float3, i = (int)VFXValueType.Int32, u = (int)VFXValueType.Uint32;
            var data = Data(new[]
            {
                Value(VFXValueType.Float), Value(VFXValueType.Float), Value(VFXValueType.Int32), Value(VFXValueType.Int32),         // 0..3
                Value(VFXValueType.Uint32), Value(VFXValueType.Uint32), Value(VFXValueType.Float3), Value(VFXValueType.Float3),   // 4..7
                Op(VFXExpressionOperation.Add, 0, 1, -1, f), Op(VFXExpressionOperation.Divide, 0, 1, -1, f),                     // 8, 9
                Op(VFXExpressionOperation.Divide, 2, 3, -1, i), Op(VFXExpressionOperation.Divide, 4, 5, -1, u),                 // 10, 11
                Op(VFXExpressionOperation.Mul, 6, 7, -1, f3), Op(VFXExpressionOperation.Min, 2, 3, -1, i),                       // 12, 13
                Op(VFXExpressionOperation.Subtract, 4, 5, -1, u), Op(VFXExpressionOperation.Abs, 2, -1, -1, i),                  // 14, 15
                Op(VFXExpressionOperation.Sign, 2, -1, -1, i), Op(VFXExpressionOperation.Pow, 2, 3, -1, i),                      // 16, 17
                Op(VFXExpressionOperation.Max, 4, 5, -1, u), Op(VFXExpressionOperation.Sin, 4, -1, -1, u),                       // 18, 19
            }, new[]
            {
                Constant(0, 7.5f), Constant(1, 0f), Constant(2, -9), Constant(3, 0), Constant(4, 5u), Constant(5, 0u),
                Constant(6, new Vector3(1f, 2f, 3f)), Constant(7, new Vector3(4f, 5f, 6f)),
            });
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            Assert.Equal(7.5f, values.GetFloat(8));
            Assert.Equal(float.PositiveInfinity, values.GetFloat(9));
            // A division by zero: the largest value of the dividend's sign, or of an unsigned one.
            Assert.Equal(unchecked((uint)int.MinValue), values.GetWord(10));
            Assert.Equal(uint.MaxValue, values.GetWord(11));
            Assert.Equal(new Vector4(4f, 10f, 18f, 0f), values.GetVector(12, 3));
            Assert.Equal(unchecked((uint)-9), values.GetWord(13));
            Assert.Equal(5u, values.GetWord(14));
            Assert.Equal(9u, values.GetWord(15));
            Assert.Equal(unchecked((uint)-1), values.GetWord(16));
            // Operations a type does not have leave the value as it was (never evaluated: every bit set).
            Assert.Equal(uint.MaxValue, values.GetWord(17));
            Assert.Equal(5u, values.GetWord(18));
            Assert.Equal(uint.MaxValue, values.GetWord(19));
        }

        [Fact]
        public void FloatFunctionsRoundSaturateAndCompareAsTheEditors()
        {
            int f = (int)VFXValueType.Float;
            VFXExpressionDesc Unary(VFXExpressionOperation op, int x) => Op(op, x, -1, -1, f);
            var data = Data(new[]
            {
                Value(VFXValueType.Float), Value(VFXValueType.Float), Value(VFXValueType.Float), Value(VFXValueType.Float),   // 0: 2.5, 1: -2.5, 2: NaN, 3: 0.75
                Unary(VFXExpressionOperation.Round, 0), Unary(VFXExpressionOperation.Round, 1), Unary(VFXExpressionOperation.Frac, 1),      // 4, 5, 6
                Unary(VFXExpressionOperation.Saturate, 0), Unary(VFXExpressionOperation.Saturate, 1), Unary(VFXExpressionOperation.Saturate, 2), // 7, 8, 9
                Unary(VFXExpressionOperation.Sign, 1), Unary(VFXExpressionOperation.Sign, 2),                                                // 10, 11
                Op(VFXExpressionOperation.Min, 3, 2, -1, f), Op(VFXExpressionOperation.Min, 2, 3, -1, f),                                    // 12, 13
                Op(VFXExpressionOperation.Max, 0, 1, -1, f), Unary(VFXExpressionOperation.Ceil, 1), Unary(VFXExpressionOperation.Floor, 1),  // 14, 15, 16
                Op(VFXExpressionOperation.Condition, 2, 2, f, 0), Op(VFXExpressionOperation.Condition, 2, 3, f, 1),                           // 17: NaN == NaN, 18: NaN != x
                Op(VFXExpressionOperation.Condition, 1, 0, f, 2), Op(VFXExpressionOperation.Condition, 2, 0, f, 2),                           // 19: -2.5 < 2.5, 20: NaN < x
                Op(VFXExpressionOperation.Condition, 0, 0, f, 5), Op(VFXExpressionOperation.Branch, 19, 0, 1, f),                             // 21: >=, 22: branch true
                Op(VFXExpressionOperation.Branch, 20, 0, 1, f),                                                                              // 23: branch false
            }, new[] { Constant(0, 2.5f), Constant(1, -2.5f), Constant(2, float.NaN), Constant(3, 0.75f) });
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            // Halves round to the even neighbour.
            Assert.Equal(2f, values.GetFloat(4));
            Assert.Equal(-2f, values.GetFloat(5));
            Assert.Equal(0.5f, values.GetFloat(6));
            Assert.Equal(1f, values.GetFloat(7));
            Assert.Equal(0f, values.GetFloat(8));
            Assert.True(float.IsNaN(values.GetFloat(9)));
            Assert.Equal(-1f, values.GetFloat(10));
            Assert.Equal(0f, values.GetFloat(11));
            // With a NaN on either side the first operand is the result.
            Assert.Equal(0.75f, values.GetFloat(12));
            Assert.True(float.IsNaN(values.GetFloat(13)));
            Assert.Equal(2.5f, values.GetFloat(14));
            Assert.Equal(-2f, values.GetFloat(15));
            Assert.Equal(-3f, values.GetFloat(16));
            Assert.Equal(new uint[] { 0, 1, 1, 0, 1 }, new[] { values.GetWord(17), values.GetWord(18), values.GetWord(19), values.GetWord(20), values.GetWord(21) });
            Assert.Equal(2.5f, values.GetFloat(22));
            Assert.Equal(-2.5f, values.GetFloat(23));
        }

        [Fact]
        public void CastsBitsAndFixedRandomsFollowTheirWords()
        {
            var data = Data(new[]
            {
                Value(VFXValueType.Float), Value(VFXValueType.Float), Value(VFXValueType.Uint32), Value(VFXValueType.Uint32), Value(VFXValueType.Boolean), // 0..4
                Op(VFXExpressionOperation.CastFloatToUint, 0), Op(VFXExpressionOperation.CastFloatToUint, 1), Op(VFXExpressionOperation.CastFloatToInt, 1), // 5, 6, 7
                Op(VFXExpressionOperation.CastUintToFloat, 2), Op(VFXExpressionOperation.CastIntToFloat, 2), Op(VFXExpressionOperation.CastUintToBool, 3),   // 8, 9, 10
                Op(VFXExpressionOperation.CastBoolToFloat, 4), Op(VFXExpressionOperation.BitwiseLeftShift, 2, 3), Op(VFXExpressionOperation.BitwiseRightShift, 2, 3), // 11, 12, 13
                Op(VFXExpressionOperation.BitwiseXor, 2, 3), Op(VFXExpressionOperation.BitwiseComplement, 3), Op(VFXExpressionOperation.GenerateFixedRandom, 3), // 14, 15, 16
                Op(VFXExpressionOperation.LogicalNot, 4), Op(VFXExpressionOperation.LogicalAnd, 4, 10), Op(VFXExpressionOperation.LogicalOr, 17, 10),           // 17, 18, 19
            }, new[] { Constant(0, 3.9f), Constant(1, -3.9f), Constant(2, 0xfffffff0u), Constant(3, 33u), Constant(4, true) });
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            Assert.Equal(3u, values.GetWord(5));
            // A negative float wraps as an unsigned value, and truncates toward zero as a signed one.
            Assert.Equal(unchecked((uint)-3), values.GetWord(6));
            Assert.Equal(unchecked((uint)-3), values.GetWord(7));
            Assert.Equal(4294967280f, values.GetFloat(8));
            Assert.Equal(-16f, values.GetFloat(9));
            Assert.Equal(1u, values.GetWord(10));
            Assert.Equal(1f, values.GetFloat(11));
            // Shift counts use their low five bits: 33 shifts by one.
            Assert.Equal(0xffffffe0u, values.GetWord(12));
            Assert.Equal(0x7ffffff8u, values.GetWord(13));
            Assert.Equal(0xfffffff0u ^ 33u, values.GetWord(14));
            Assert.Equal(~33u, values.GetWord(15));
            // The number a hash gives, in [0, 1): pinned (the Editor's draw from an effect's seed
            // is recorded by tests/projects/vfx, `sim/expressions/inputs/*/f32`).
            Assert.Equal(0x3e244738u, values.GetWord(16));
            Assert.Equal(new uint[] { 0, 1, 1 }, new[] { values.GetWord(17), values.GetWord(18), values.GetWord(19) });
        }

        [Fact]
        public void BuiltInValuesAndTransformsReadTheEffectsState()
        {
            int f3 = (int)VFXValueType.Float3;
            var data = Data(new[]
            {
                Op(VFXExpressionOperation.DeltaTime), Op(VFXExpressionOperation.UnscaledDeltaTime), Op(VFXExpressionOperation.TotalTime),  // 0, 1, 2
                Op(VFXExpressionOperation.PlayRate), Op(VFXExpressionOperation.SystemSeed), Op(VFXExpressionOperation.FrameIndex),         // 3, 4, 5
                Op(VFXExpressionOperation.LocalToWorld), Value(VFXValueType.Float3),                                                      // 6, 7
                Op(VFXExpressionOperation.TransformPos, 6, 7, -1, f3), Op(VFXExpressionOperation.TransformVec, 6, 7, -1, f3),             // 8, 9
                Op(VFXExpressionOperation.TransformDir, 6, 7, -1, f3), Op(VFXExpressionOperation.ExtractComponent, 8, -1, 1, 3),           // 10, 11
                Op(VFXExpressionOperation.Combine2f, 0, 2), Op(VFXExpressionOperation.SpawnerStateLoopIndex),                             // 12, 13
            }, new[] { Constant(7, new Vector3(1f, 2f, 3f)) });
            var state = new VisualEffectState
            {
                DeltaTime = 0.25f, UnscaledDeltaTime = 0.5f, TotalTime = 3f, PlayRate = 0.5f, Seed = 77, FrameIndex = 12,
                LocalToWorld = Matrix4x4.TRS(new Vector3(10f, 20f, 30f), Quaternion.identity, new Vector3(2f, 2f, 2f)),
            };
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, state, null);
            Assert.Equal((0.25f, 0.5f, 3f, 0.5f), (values.GetFloat(0), values.GetFloat(1), values.GetFloat(2), values.GetFloat(3)));
            Assert.Equal((77u, 12u), (values.GetWord(4), values.GetWord(5)));
            Assert.Equal(state.LocalToWorld, values.GetMatrix(6));
            Assert.Equal(new Vector4(12f, 24f, 36f, 0f), values.GetVector(8, 3));
            // A value nothing evaluated yet has every bit set.
            Assert.Equal(uint.MaxValue, new VisualEffectValues(data).GetWord(0));
            Assert.Equal(new Vector4(2f, 4f, 6f, 0f), values.GetVector(9, 3));
            var direction = (Vector3)values.GetVector(10, 3);
            Assert.Equal(1f, direction.magnitude, 5);
            Assert.Equal(24f, values.GetFloat(11));
            Assert.Equal(new Vector4(0.25f, 3f, 0f, 0f), values.GetVector(12, 2));
            // A spawner's own values are not evaluated outside one: the value stays (every bit set).
            Assert.Equal(uint.MaxValue, values.GetWord(13));
        }

        [Fact]
        public void AGraphUsingWhatTheRuntimeDoesNotDoYetSaysWhat()
        {
            var plain = Data(new[] { Value(VFXValueType.Float), Op(VFXExpressionOperation.Sin, 0, -1, -1, 1) }, new[] { Constant(0, 1f) });
            Assert.Null(VisualEffect.Unsupported(plain));
            var axis = Data(new[] { Value(VFXValueType.Matrix4x4), Value(VFXValueType.Int32), Op(VFXExpressionOperation.MatrixToAxis, 0, 1) },
                new[] { Constant(0, Matrix4x4.identity), Constant(1, 0) });
            Assert.Equal("the expression operation MatrixToAxis", VisualEffect.Unsupported(axis));
            // A bake has a place among the sheet's own expressions only.
            var baked = Data(new[] { Value(VFXValueType.Curve), Op(VFXExpressionOperation.BakeCurve, 0) }, new[] { Constant(0, new AnimationCurve()) });
            Assert.Null(VisualEffect.Unsupported(baked));
            Assert.False(baked.UnplacedBake);
            // One among the expressions of a spawn event has none: the graph is refused; the
            // sheet's own bake in the same graph keeps its place.
            var bakedPerEvent = Data(new[] { Value(VFXValueType.Curve), Op(VFXExpressionOperation.BakeCurve, 0) }, new[] { Constant(0, new AnimationCurve()) },
                perSpawnEvent: new[] { Op(VFXExpressionOperation.BakeCurve, 0) });
            Assert.True(bakedPerEvent.UnplacedBake);
            Assert.Equal("a curve or gradient baked among the expressions of a spawn event", VisualEffect.Unsupported(bakedPerEvent));
            Assert.Equal(new[] { -1, 0, -1 }, bakedPerEvent.BakePlaces);
            var rampPerEvent = Data(new[] { Value(VFXValueType.ColorGradient) }, new[] { Constant(0, new Gradient()) },
                perSpawnEvent: new[] { Op(VFXExpressionOperation.BakeGradient, 0) });
            Assert.Equal("a curve or gradient baked among the expressions of a spawn event", VisualEffect.Unsupported(rampPerEvent));
            var strips = Data(Array.Empty<VFXExpressionDesc>(), Array.Empty<VFXExpressionValueContainerDesc>(), new[]
            {
                new VFXEditorSystemDesc { name = "Trail", type = VFXSystemType.Particle, layer = 0, flags = VFXSystemFlag.SystemHasKill | VFXSystemFlag.SystemHasStrips, tasks = Array.Empty<VFXEditorTaskDesc>() },
            });
            // A strip system is run when it has what the graph compiler gives one.
            Assert.Equal("the strip system 'Trail' without its strips' buffer or sizes", VisualEffect.Unsupported(strips));
        }

        [Fact]
        public void TransformsOfAFrameMatchTheEditorsBits()
        {
            // The frame of tests/projects/vfx `sim/expressions`: its matrix, the transforms of
            // (1, 2, 3) and its inverse, bit for bit as recorded there (m00, v03, v04, q00, m01).
            int f3 = (int)VFXValueType.Float3, f4 = (int)VFXValueType.Float4;
            var data = Data(new[]
            {
                Value(VFXValueType.Float3), Value(VFXValueType.Float3), Value(VFXValueType.Float3), Value(VFXValueType.Float3), Value(VFXValueType.Float4), // 0..4
                Op(VFXExpressionOperation.TRSToMatrix, 0, 1, 2), Op(VFXExpressionOperation.TransformPos, 5, 3, -1, f3),                                  // 5, 6
                Op(VFXExpressionOperation.TransformVec, 5, 3, -1, f3), Op(VFXExpressionOperation.TransformVector4, 5, 4, -1, f4),                        // 7, 8
                Op(VFXExpressionOperation.InverseTRSMatrix, 5), Op(VFXExpressionOperation.TransformDir, 5, 3, -1, f3),                                   // 9, 10
            }, new[]
            {
                Constant(0, new Vector3(10f, 20f, 30f)), Constant(1, new Vector3(30f, 45f, 60f)), Constant(2, new Vector3(2f, 3f, 4f)),
                Constant(3, new Vector3(1f, 2f, 3f)), Constant(4, new Vector4(1f, 2f, 3f, 1f)),
            });
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            uint[] Words(int expression, int count)
            {
                var words = new uint[count];
                Array.Copy(values.Words, data.ValueOffsets[expression], words, 0, count);
                return words;
            }
            Assert.Equal(new uint[]
            {
                0x3fa8e4b2, 0x3fbfffff, 0xbdc20428, 0x00000000, 0xbfa744cc, 0x3fa646e2, 0x40178442, 0x00000000,
                0x401cc470, 0xbffffffe, 0x401cc470, 0x00000000, 0x41200000, 0x41a00000, 0x41f00000, 0x3f800000,
            }, Words(5, 16));
            Assert.Equal(new uint[] { 0x41806f5c, 0x4190c8dd, 0x4227f45b }, Words(6, 3));
            Assert.Equal(new uint[] { 0x40c1bd6e, 0xbff37234, 0x413fd16c }, Words(7, 3));
            Assert.Equal(new uint[] { 0x41806f5c, 0x4190c8dd, 0x4227f45b, 0x3f800000 }, Words(8, 4));
            Assert.Equal(new uint[]
            {
                0x3ea8e4b3, 0xbe14aeef, 0x3e1cc472, 0x00000000, 0x3ec00000, 0x3e13cd3c, 0xbe000000, 0x00000000,
                0xbcc20422, 0x3e86ae74, 0x3e1cc471, 0x00000000, 0xc121693a, 0xc115385a, 0xc067eb1b, 0x3f800000,
            }, Words(9, 16));
            // A direction comes out of unit length.
            var direction = (Vector3)values.GetVector(10, 3);
            Assert.Equal(1f, direction.magnitude, 6);

            // A frame of no depth has no inverse: a matrix of zeros.
            values.SetVector(2, new Vector3(1f, 1f, 0f), 3);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            Assert.All(Words(9, 16), word => Assert.Equal(0u, word));
        }

        [Fact]
        public void AKernelsTexturesAndBuffersAreItsTasksObjectValues()
        {
            var data = Data(new[] { Value(VFXValueType.Float), Value(VFXValueType.Texture2D), Value(VFXValueType.Buffer), Value(VFXValueType.Curve), Value(VFXValueType.Texture3D) },
                Array.Empty<VFXExpressionValueContainerDesc>());
            var task = new VFXEditorTaskDesc
            {
                type = VFXTaskType.Initialize,
                values = new[] { new VFXMapping("rate", 0), new VFXMapping("mainTexture", 1), new VFXMapping("points", 2), new VFXMapping("curve", 3), new VFXMapping("field", 4), new VFXMapping("gone", -1) },
            };
            // Numbers reach a kernel through the graph values, curves as baked textures: the
            // textures and the buffer are bound by name.
            Assert.Equal(new[] { (Shader.PropertyToID("mainTexture"), 1), (Shader.PropertyToID("points"), 2), (Shader.PropertyToID("field"), 4) }, VFXParticleSystem.ObjectValues(data, task));
            Assert.Empty(VFXParticleSystem.ObjectValues(data, new VFXEditorTaskDesc { type = VFXTaskType.Update }));
        }

        // ------------------------------------------------------------------- the frame's steps

        [Fact]
        public void WhatWasBoundToAKernelCanBeTakenBack()
        {
            // A kernel object whose value goes away (a property without a texture, a destroyed
            // one) is unbound: the kernel reads what it reads when nothing was ever bound.
            var shader = ComputeShader.FromFile("Tests/VfxUnbind.compute", "VfxUnbind", "#pragma kernel CSMain\n#pragma kernel CSOther\n");
            var texture = new Texture2D(2, 2);
            int main = shader.FindKernel("CSMain"), other = shader.FindKernel("CSOther"), name = Shader.PropertyToID("Image");
            try
            {
                shader.SetTexture(main, name, texture);
                shader.SetTexture(other, name, texture);
                Assert.Contains(shader.Properties(main, null), p => p.Name == "Image");
                shader.Unbind(main, name);
                Assert.DoesNotContain(shader.Properties(main, null), p => p.Name == "Image");
                // The other kernel keeps its own, and taking back what is not bound is nothing.
                Assert.Contains(shader.Properties(other, null), p => p.Name == "Image");
                shader.Unbind(main, name);
                Assert.DoesNotContain(shader.Properties(main, null), p => p.Name == "Image");
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void CurvesAndGradientsAreBakedAtTheirPlacesWithWhatSamplesThem()
        {
            var steady = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.5f, 0.45f), new Keyframe(1f, 0.1f));
            var looping = new AnimationCurve(new Keyframe(2f, 1f), new Keyframe(4f, 3f)) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
            var large = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 131008f));
            var bright = new Gradient { mode = GradientMode.Fixed };
            bright.SetKeys(new[] { new GradientColorKey(new Color(131008f, 2f, 1f), 0.5f), new GradientColorKey(new Color(1f, 2f, 4f), 1f) },
                new[] { new GradientAlphaKey(0.25f, 0f), new GradientAlphaKey(0.25f, 1f) });
            // Three curves and a gradient as values, the first curve exposed; each baked.
            var data = Data(new[]
            {
                Value(VFXValueType.Curve), Value(VFXValueType.Curve), Value(VFXValueType.Curve), Value(VFXValueType.ColorGradient),
                Op(VFXExpressionOperation.BakeCurve, 0), Op(VFXExpressionOperation.BakeCurve, 1), Op(VFXExpressionOperation.BakeCurve, 2),
                Op(VFXExpressionOperation.BakeGradient, 3),
            }, new[] { Constant(0, steady), Constant(1, looping), Constant(2, large), Constant(3, bright) },
                exposed: new[] { new VFXExposedMapping { mapping = new VFXMapping("Size", 0) } });
            // The curves every effect shares come first, in order; the exposed one after them.
            Assert.Equal(new[] { -1, -1, -1, -1, 2, 0, 1, 0 }, data.BakePlaces);
            Assert.Equal((3, 1), (data.BakedCurves, data.BakedGradients));

            var state = new VisualEffectState { Baked = VFXBakedTexture.For(data) };
            var values = new VisualEffectValues(data);
            try
            {
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, state, null);
                state.Baked.Apply();
                var texture = state.Baked.Texture;
                // Three curves in one row, the gradient in the next: 128 half-float texels a row.
                Assert.Equal((128, 2, TextureFormat.RGBAHalf, TextureWrapMode.Repeat), (texture.width, texture.height, texture.format, texture.wrapMode));
                uint[] Words(int expression, int count)
                {
                    var words = new uint[count];
                    for (int i = 0; i < count; i++) words[i] = values.Words[data.ValueOffsets[expression] + i];
                    return words;
                }
                float row0 = 0.25f, row1 = 0.75f;
                // The exposed curve, place 2: keys from 0 to 1 (one over the range, minus the
                // start over it, as two half floats), no scale, its row, its channel with
                // "clamps before and after".
                Assert.Equal(new uint[] { 0x80003c00, Bits(1f), Bits(row0), 2 | 4 | 8 }, Words(4, 4));
                Assert.Equal(0.05f, texture.GetPixel(0, 0).b, 4);
                Assert.Equal(0.1f, texture.GetPixel(127, 0).b, 4);
                Assert.Equal(steady.Evaluate(64f / 127f), texture.GetPixel(64, 0).b, 3);
                // The looping curve, place 0: keys from 2 to 4; sampled at texel centres.
                Assert.Equal(new uint[] { (uint)Mathf.FloatToHalf(0.5f) | (uint)Mathf.FloatToHalf(-1f) << 16, Bits(1f), Bits(row0), 0 }, Words(5, 4));
                Assert.Equal(looping.Evaluate(2f + 0.5f * 2f / 128f), texture.GetPixel(0, 0).r, 3);
                Assert.Equal(looping.Evaluate(2f + 127.5f * 2f / 128f), texture.GetPixel(127, 0).r, 3);
                // The large curve, place 1: stored scaled to the largest half float, the scale beside it.
                Assert.Equal(new uint[] { 0x80003c00, Bits(131008f / 65504f), Bits(row0), 1 | 4 | 8 }, Words(6, 4));
                Assert.Equal(65504f, texture.GetPixel(127, 0).g);
                Assert.Equal(0f, texture.GetPixel(0, 0).g);
                // The gradient: its row, fixed between keys, its colours scaled and its alpha not.
                Assert.Equal(new uint[] { Bits(row1), Bits(1f), Bits(131008f / 65504f) }, Words(7, 3));
                var first = texture.GetPixel(0, 1);
                Assert.Equal((65504f, 1f, 0.5f), (first.r, first.g, first.b));
                Assert.Equal(0.25f, first.a, 3);
                Assert.Equal(2f, texture.GetPixel(127, 1).b, 3);

                // Another curve at the exposed place is baked; the same object again is not.
                var pixelsBefore = texture.GetPixel(0, 0);
                values.SetObject(0, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 0.02f)));
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, state, null);
                state.Baked.Apply();
                Assert.Same(texture, state.Baked.Texture);
                Assert.Equal(0.3f, texture.GetPixel(0, 0).b, 3);
                Assert.Equal(pixelsBefore.r, texture.GetPixel(0, 0).r);
                // A curve of one key: no range to divide by, every texel its value. One
                // without keys, and none at all: zeros over one second from zero.
                values.SetObject(0, new AnimationCurve(new Keyframe(0.5f, 0.25f)));
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, state, null);
                state.Baked.Apply();
                Assert.Equal(new uint[] { 0xfc007c00, Bits(1f), Bits(row0), 2 | 4 | 8 }, Words(4, 4));
                Assert.Equal((0.25f, 0.25f, 0.25f), (texture.GetPixel(0, 0).b, texture.GetPixel(64, 0).b, texture.GetPixel(127, 0).b));
                foreach (var none in new[] { new AnimationCurve(), null })
                {
                    values.SetObject(0, none);
                    VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, state, null);
                    state.Baked.Apply();
                    Assert.Equal(new uint[] { 0x80003c00, Bits(1f), Bits(row0), 2 | 4 | 8 }, Words(4, 4));
                    Assert.Equal((0f, 0f), (texture.GetPixel(0, 0).b, texture.GetPixel(127, 0).b));
                }
                // Keys that start before zero and end after one: the range and the start go
                // into the words, the samples span the keys.
                var wide = new AnimationCurve(new Keyframe(-0.5f, 1f), new Keyframe(1.5f, 3f));
                values.SetObject(0, wide);
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, state, null);
                state.Baked.Apply();
                Assert.Equal((uint)Mathf.FloatToHalf(0.5f) | (uint)Mathf.FloatToHalf(0.25f) << 16, Words(4, 4)[0]);
                Assert.Equal((1f, 3f), (texture.GetPixel(0, 0).b, texture.GetPixel(127, 0).b));
                // A curve that loops after its last key and not before its first. Where the
                // last sample's time reaches the last key, it reads the first key's value, as
                // any time on that key does; with keys where that time falls just short of
                // the last key, the value there.
                foreach (var (from, to, last) in new[] { (0f, 1f, 0.05f), (0.3f, 1.4f, 0.5f) })
                {
                    values.SetObject(0, new AnimationCurve(new Keyframe(from, 0.05f), new Keyframe(to, 0.5f)) { postWrapMode = WrapMode.Loop });
                    VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, state, null);
                    state.Baked.Apply();
                    Assert.Equal(2u | 8u, Words(4, 4)[3]);
                    Assert.Equal((0.05f, last), (MathF.Round(texture.GetPixel(0, 0).b, 3), MathF.Round(texture.GetPixel(127, 0).b, 3)));
                    Assert.Equal(0.5f, texture.GetPixel(126, 0).b, 2);
                }
                // No gradient: a row of nothing, not fixed, no scale.
                values.SetObject(3, null);
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, state, null);
                state.Baked.Apply();
                Assert.Equal(new uint[] { Bits(row1), Bits(0f), Bits(1f) }, Words(7, 3));
                Assert.Equal(default, texture.GetPixel(64, 1));
                // A graph that bakes nothing has no texture.
                Assert.Null(VFXBakedTexture.For(Data(new[] { Value(VFXValueType.Float) }, new[] { Constant(0, 1f) })));
            }
            finally { state.Baked.Release(); }
        }

        [Fact]
        public void CurvesAndGradientsAreSampledWhereExpressionsAreEvaluated()
        {
            // Keys whose cubic rounds another way than AnimationCurve.Evaluate: the operator's.
            var curve = new AnimationCurve(new Keyframe(-1f, 3f), new Keyframe(0f, -1f), new Keyframe(2f, 5f));
            Assert.NotEqual(Bits(curve.Evaluate(0.6f)), Bits(curve.EvaluateCubic(0.6f)));
            var ramp = new Gradient();
            ramp.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) }, new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0.5f, 1f) });
            var data = Data(new[]
            {
                Value(VFXValueType.Curve), Value(VFXValueType.ColorGradient), Value(VFXValueType.Float),
                Op(VFXExpressionOperation.SampleCurve, 0, 2), Op(VFXExpressionOperation.SampleGradient, 1, 2),
            }, new[] { Constant(0, curve), Constant(1, ramp), Constant(2, 0.6f) });
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            Assert.Equal(Bits(curve.EvaluateCubic(0.6f)), values.GetWord(3));
            var color = ramp.Evaluate(0.6f);
            int at = data.ValueOffsets[4];
            Assert.Equal(new[] { Bits(color.r), Bits(color.g), Bits(color.b), Bits(color.a) }, new[] { values.Words[at], values.Words[at + 1], values.Words[at + 2], values.Words[at + 3] });
            // Without a curve or a gradient: zeros.
            values.SetObject(0, null);
            values.SetObject(1, null);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            Assert.Equal((0u, 0u), (values.GetWord(3), values.Words[at + 3]));
        }

        [Fact]
        public void ASystemThatKillsSleepsUntilItFirstSpawns()
        {
            // Ten a second in steps of a thirtieth: the first particle comes with the third step.
            var asset = Counting(10f);
            asset.m_Data.Systems[1].flags |= VFXSystemFlag.SystemHasKill;
            var effect = Running(asset);
            try
            {
                var awake = new bool[4];
                for (int i = 0; i < awake.Length; i++)
                {
                    effect.UpdateEffect(Thirtieth);
                    awake[i] = effect.HasAnySystemAwake();
                }
                Assert.Equal(new[] { false, false, true, true }, awake);
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        [Fact]
        public void AFramesFixedStepsAreTheNearestCountAndKeepTheRest()
        {
            float step = 1f / 60f, remainder = 0f;
            // A thirtieth holds two steps; three hundredths hold two (1.8 rounds up) and owe the rest.
            Assert.Equal(2u, VFXManager.FixedSteps(1f / 30f, step, 3, ref remainder));
            Assert.Equal(0f, remainder, 6);
            Assert.Equal(2u, VFXManager.FixedSteps(0.03f, step, 3, ref remainder));
            Assert.Equal(0.03f - 2f * step, remainder, 6);
            Assert.True(remainder < 0f);
            // A long frame: its steps are limited, but all of its time is accounted for.
            Assert.Equal(3u, VFXManager.FixedSteps(0.1f, step, 3, ref remainder));
            Assert.InRange(remainder, -step / 2f - 1e-6f, step / 2f + 1e-6f);
            // No time: no step.
            remainder = 0f;
            Assert.Equal(0u, VFXManager.FixedSteps(0.005f, step, 3, ref remainder));
            Assert.Equal(0.005f, remainder);
            Assert.Equal(1u, VFXManager.FixedSteps(0.005f, step, 3, ref remainder));
        }

        [Fact]
        public void AnUpdateModeChoosesTheFramesStepsAndTheirLength()
        {
            var time = new VFXManager.FrameTime { FixedSteps = 2, FixedTime = 2f / 60f, DeltaTime = 0.03f, UnscaledFixedSteps = 3, UnscaledFixedTime = 0.05f, UnscaledDeltaTime = 0.04f };
            (uint, float) Steps(VFXUpdateMode mode, in VFXManager.FrameTime t)
            {
                VisualEffect.UpdateTime(mode, t, out uint steps, out float delta);
                return (steps, delta);
            }
            Assert.Equal((1u, 2f / 60f), Steps(VFXUpdateMode.FixedDeltaTime, time));
            Assert.Equal((1u, 0.03f), Steps(VFXUpdateMode.DeltaTime, time));
            Assert.Equal((1u, 0.04f), Steps(VFXUpdateMode.DeltaTimeAndIgnoreTimeScale, time));
            Assert.Equal((1u, 0.05f), Steps(VFXUpdateMode.IgnoreTimeScale, time));
            Assert.Equal((2u, VFXManager.fixedTimeStep), Steps(VFXUpdateMode.ExactFixedTimeStep, time));
            Assert.Equal((3u, VFXManager.fixedTimeStep), Steps(VFXUpdateMode.FixedDeltaAndExactTimeAndIgnoreTimeScale, time));
            // A frame that holds no fixed step still runs one step, of no time.
            var none = new VFXManager.FrameTime { DeltaTime = 0.005f };
            Assert.Equal((1u, 0f), Steps(VFXUpdateMode.ExactFixedTimeStep, none));
            Assert.Equal((1u, 0f), Steps(VFXUpdateMode.FixedDeltaTime, none));
        }

        [Fact]
        public void GraphValuesPackInBlocksOfFourWordsAfterTheContextData()
        {
            var data = Data(new[]
            {
                Value(VFXValueType.Matrix4x4), Value(VFXValueType.Matrix4x4), Value(VFXValueType.Float), Value(VFXValueType.Float3),
                Value(VFXValueType.Float2), Value(VFXValueType.Float2), Value(VFXValueType.Float),
            }, Array.Empty<VFXExpressionValueContainerDesc>());
            int Words(params int[] expressions)
            {
                VFXParticleSystem.GraphValueOffsets(data, expressions, out int words);
                return words;
            }
            // Two matrices and a float: 4 + 16 + 16 + 1, padded to 40 words.
            Assert.Equal(40, Words(0, 1, 2));
            Assert.Equal(new[] { 4, 20, 36 }, VFXParticleSystem.GraphValueOffsets(data, new[] { 0, 1, 2 }, out _));
            // A float3 with a float fills a block; two float2 fill one; a float starts the next.
            Assert.Equal(4 + 4 + 4 + 4, Words(3, 2, 4, 5, 6));
            Assert.Equal(new[] { 4, 7, 8, 10, 12 }, VFXParticleSystem.GraphValueOffsets(data, new[] { 3, 2, 4, 5, 6 }, out _));
            // A float3 after a float2 does not fit its block.
            Assert.Equal(4 + 4 + 4, Words(4, 3));
            Assert.Equal(new[] { 4, 8 }, VFXParticleSystem.GraphValueOffsets(data, new[] { 4, 3 }, out _));
            Assert.Equal(4, Words());
        }

        // ------------------------------------------------------------------------- an effect

        /// <summary>A spawner at a constant rate feeding a particle system of 20 particles that
        /// never die (so its alive count is what it spawned), with events to play and stop it.</summary>
        static VisualEffectAsset Counting(float rate, VFXCullingFlags culling = VFXCullingFlags.CullNone, VFXUpdateMode mode = VFXUpdateMode.FixedDeltaTime,
            params VFXValueType[] more)
        {
            var spawner = new VFXEditorSystemDesc
            {
                name = "Spawn", type = VFXSystemType.Spawner, layer = uint.MaxValue, buffers = new[] { new VFXMapping("spawner_output", 1) },
                tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.ConstantRateSpawner, values = new[] { new VFXMapping("Rate", 0) } } },
            };
            var particles = new VFXEditorSystemDesc
            {
                name = "Particles", type = VFXSystemType.Particle, layer = 0, capacity = 20, flags = VFXSystemFlag.SystemHasAttributeBuffer,
                buffers = new[] { new VFXMapping("spawner_input", 1) }, tasks = Array.Empty<VFXEditorTaskDesc>(),
            };
            // The rate, then the values asked for besides (expressions 1, 2, ...).
            var expressions = new VFXExpressionDesc[1 + more.Length];
            expressions[0] = Value(VFXValueType.Float);
            for (int i = 0; i < more.Length; i++) expressions[1 + i] = Value(more[i]);
            var data = Data(expressions, new[] { Constant(0, rate) }, new[] { spawner, particles }, new[]
            {
                new VFXEventDesc { name = "OnPlay", startSystems = new uint[] { 0 }, stopSystems = Array.Empty<uint>(), initSystems = Array.Empty<uint>() },
                new VFXEventDesc { name = "OnStop", startSystems = Array.Empty<uint>(), stopSystems = new uint[] { 0 }, initSystems = Array.Empty<uint>() },
            }, spawners: 1);
            var asset = VisualEffectAsset.Create();
            asset.m_Data = data;
            asset.m_Settings.CullingFlags = culling;
            asset.m_Settings.UpdateMode = mode;
            asset.m_Settings.PreWarmStepCount = 0;
            return asset;
        }

        internal static readonly VFXManager.FrameTime Thirtieth = new VFXManager.FrameTime { FixedSteps = 2, FixedTime = Frame, DeltaTime = Frame, UnscaledFixedSteps = 2, UnscaledFixedTime = Frame, UnscaledDeltaTime = Frame };

        internal static VisualEffect Running(VisualEffectAsset asset)
        {
            var effect = new GameObject("effect").AddComponent<VisualEffect>();
            effect.resetSeedOnPlay = false;
            effect.visualEffectAsset = asset;
            return effect;
        }

        static int[] Counts(VisualEffect effect, int frames)
        {
            var counts = new int[frames];
            for (int i = 0; i < frames; i++)
            {
                effect.UpdateEffect(Thirtieth);
                counts[i] = effect.aliveParticleCount;
            }
            return counts;
        }

        [Fact]
        public void AConstantRateSpawnsItsWholeParticlesCarriesTheRestAndStopsAtTheCapacity()
        {
            var effect = Running(Counting(7.3f));
            try
            {
                // 7.3 a second in steps of a thirtieth: 0.2433 a step, a particle when the sum
                // passes a whole number (the Editor's counts: tests/projects/vfx, sim/rate).
                Assert.Equal(new[] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3 }, Counts(effect, 13));
                Assert.True(effect.HasAnySystemAwake());
                var spawner = effect.GetSpawnSystemInfo("Spawn");
                float total = 0f;
                for (int i = 0; i < 13; i++) total += Frame;
                Assert.Equal((VFXSpawnerLoopState.Looping, Frame, total), (spawner.loopState, spawner.deltaTime, spawner.totalTime));
                Assert.Equal(7.3f * Frame, spawner.spawnCount);
                Assert.Equal(new VFXParticleSystemInfo(3, 20, false, default).aliveCount, effect.GetParticleSystemInfo("Particles").aliveCount);
                // A hundred more steps would spawn 24: the system takes what it has room for.
                Counts(effect, 100);
                Assert.Equal(20, effect.aliveParticleCount);
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        sealed class Errors : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
            public readonly List<string> Logged = new List<string>();
            public Errors() { Debug.unityLogger.logHandler = this; }
            public void Dispose() { Debug.unityLogger.logHandler = m_Previous; }
            public void LogException(Exception exception, Object context) => Logged.Add(exception.Message);
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error || type == LogType.Exception) Logged.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
            }
        }

        [Fact]
        public void WithoutARendererASystemWithKernelsCountsOnAndSaysNothing()
        {
            var asset = Counting(30f);
            // An initialize kernel for the particles: here (a test has no renderer, as a
            // headless run has none) nothing can tell whether it compiles, and nothing runs it.
            asset.m_Data.Systems[1].tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.Initialize } };
            asset.m_Data.Processors = new[]
            {
                Array.Empty<Object>(),
                new Object[] { ComputeShader.FromFile("Tests/VfxInitialize.compute", "VfxInitialize", "#pragma kernel CSMain\n") },
            };
            var effect = Running(asset);
            try
            {
                using var errors = new Errors();
                Assert.Equal(new[] { 1, 2, 3 }, Counts(effect, 3));
                Assert.Empty(errors.Logged);
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        /// <summary>The counting effect with an output task for its particles.</summary>
        static VisualEffectAsset WithOutput(VFXTaskType type, Object processor, params VFXMapping[] parameters) =>
            WithOutput(type, processor, Array.Empty<VFXValueType>(), parameters);

        /// <summary>That effect with values besides its rate (expressions 1, 2, ...).</summary>
        static VisualEffectAsset WithOutput(VFXTaskType type, Object processor, VFXValueType[] values, params VFXMapping[] parameters)
        {
            var asset = Counting(30f, more: values);
            asset.m_Data.Systems[1].tasks = new[] { new VFXEditorTaskDesc { type = type, parameters = parameters } };
            asset.m_Data.Processors = new[] { Array.Empty<Object>(), new[] { processor } };
            return asset;
        }

        sealed class Warnings : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
            public readonly List<string> Messages = new List<string>();
            public Warnings() { Debug.unityLogger.logHandler = this; }
            public void Dispose() { Debug.unityLogger.logHandler = m_Previous; }
            public void LogException(Exception exception, Object context) { }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) Messages.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
            }
        }

        [Fact]
        public void AQuadOutputDrawsTheParticlesSpawnedSoFarInOneDraw()
        {
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/VfxQuads\" { SubShader { Pass { Tags { \"LightMode\"=\"UniversalForwardOnly\" } } } }");
            var material = new Material(shader) { renderQueue = 2450 };
            var effect = Running(WithOutput(VFXTaskType.ParticleQuadOutput, material));
            var eye = new GameObject("eye").AddComponent<Camera>();
            try
            {
                using var warnings = new Warnings();
                Assert.True(effect.TryGetComponent<VFXRenderer>(out var renderer));
                var camera = new FrameBuilder.CameraContext { Camera = eye, Position = new Vector3(0f, 0f, -10f), Forward = Vector3.forward };
                var draws = new List<FrameBuilder.DrawItem>();
                // Nothing before the first step: the programs' buffers hold nothing yet.
                renderer.CollectDraws(camera, draws);
                Assert.Empty(draws);
                Counts(effect, 3);
                renderer.CollectDraws(camera, draws);
                var draw = Assert.Single(draws);
                Assert.Same(material, draw.Material);
                Assert.Same(renderer, draw.Renderer);
                Assert.Equal(2450, draw.Queue);
                // Three particles: six indices each, one instance.
                Assert.True(draw.Setup.HasValue);
                Assert.Equal((18u, 1u, (byte)2), (draw.Setup.Value.IndexCount, draw.Setup.Value.Instances, draw.Setup.Value.IndexSize));
                // The batch of one instance, as the programs read it.
                Assert.Equal(new Vector4(BitConverter.Int32BitsToSingle(1), BitConverter.Int32BitsToSingle(1), BitConverter.Int32BitsToSingle(1), 0f),
                    (Vector4)draw.Properties[Shader.PropertyToID("instancingConstants")]);
                // A disabled effect draws nothing, and nothing was said of an output that is drawn.
                effect.enabled = false;
                draws.Clear();
                renderer.CollectDraws(camera, draws);
                Assert.Empty(draws);
                Assert.DoesNotContain(warnings.Messages, m => m.Contains("not drawn"));
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
                Object.DestroyImmediate(eye.gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void ASortIsPlannedByItsListsCapacity()
        {
            string Kernels(uint capacity) => string.Join(" ", Array.ConvertAll(VFXSort.Plan(capacity, true), p => $"{p.Kernel}:{p.From}>{p.To}:{p.X}x{p.Y}" + (p.SubArraySize != null ? "@" + p.SubArraySize : "")));
            // One pass while the list fits a group's shared memory, by its size class.
            Assert.Equal("BitonicSort128:List>Sorted:1x1", Kernels(1));
            Assert.Equal("BitonicSort128:List>Sorted:1x1", Kernels(128));
            Assert.Equal("BitonicSort1024:List>Sorted:1x1", Kernels(129));
            Assert.Equal("BitonicSort2048:List>Sorted:1x1", Kernels(2048));
            Assert.Equal("BitonicSort4096:List>Sorted:1x1", Kernels(4096));
            // Longer: chunks sorted into the scratch list, then merged two by two, alternating
            // between the lists, the last merge writing the indices.
            Assert.Equal("BitonicPrePass:List>Scratch:2x1 MergeFinalPass:Scratch>Sorted:94x1@4096", Kernels(6000));
            Assert.Equal("BitonicPrePass:List>Scratch:5x1 MergePass:Scratch>List:313x1@4096 MergePass:List>Scratch:313x1@8192 MergeFinalPass:Scratch>Sorted:313x1@16384", Kernels(20000));
            // A long list without a scratch list is not planned.
            Assert.Null(VFXSort.Plan(4097, false));
            Assert.NotNull(VFXSort.Plan(4096, false));
        }

        [Fact]
        public void OutputsWhoseShadersShareANameAreNotDrawn()
        {
            string Source(string name, string body = "") => "Shader \"" + name + "\" { SubShader { Pass { Tags { \"LightMode\"=\"UniversalForwardOnly\" } " + body + "} } }";
            VFXEditorTaskDesc Output(int source) => new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, shaderSourceIndex = source };
            var systems = new[]
            {
                new VFXEditorSystemDesc { name = "A", type = VFXSystemType.Particle, tasks = new[] { Output(0), Output(1), Output(2) } },
                new VFXEditorSystemDesc { name = "B", type = VFXSystemType.Particle, tasks = new[] { Output(3) } },
            };
            var sources = new[]
            {
                new VFXShaderSourceDesc { source = Source("Hidden/VFX/G/A/Output Particle|Unlit Quad") },
                new VFXShaderSourceDesc { source = Source("Hidden/VFX/G/A/Output Particle|Unlit Quad", "Cull Off ") },
                new VFXShaderSourceDesc { source = Source("Hidden/VFX/G/A/Glow") },
                new VFXShaderSourceDesc { source = Source("Hidden/VFX/G/B/Output Particle|Unlit Quad") },
            };
            var found = VFXOutput.SourceRefusals(systems, sources);
            Assert.StartsWith("its shader's name 'Hidden/VFX/G/A/Output Particle|Unlit Quad' is another output's, whose shader differs", found[0][0]);
            Assert.Equal(found[0][0], found[0][1]);
            Assert.Null(found[0][2]);
            Assert.Null(found[1][0]);
            // Outputs whose shaders are the same source share one program, and are drawn.
            sources[1] = sources[0];
            Assert.Null(VFXOutput.SourceRefusals(systems, sources)[0][0]);
        }

        [Fact]
        public void IndirectArgumentEntriesAreReservedByKindAndGivenBack()
        {
            // The package's numbering of what arguments are for, from an output's primitive.
            Assert.Equal(VFXIndirectArgs.Kind.Points, VFXOutput.ArgumentsKind(VFXTaskType.ParticlePointOutput));
            Assert.Equal(VFXIndirectArgs.Kind.Quads, VFXOutput.ArgumentsKind(VFXTaskType.ParticleQuadOutput));
            Assert.Equal(VFXIndirectArgs.Kind.Meshes, VFXOutput.ArgumentsKind(VFXTaskType.ParticleMeshOutput));
            Assert.Equal(VFXIndirectArgs.Kind.Triangles, VFXOutput.ArgumentsKind(VFXTaskType.ParticleTriangleOutput));
            Assert.Equal(VFXIndirectArgs.Kind.Octagons, VFXOutput.ArgumentsKind(VFXTaskType.ParticleOctagonOutput));
            Assert.Equal(0xffffffffu, (uint)VFXIndirectArgs.Kind.Dispatch);

            var arguments = new VFXIndirectArgs();
            int quads = arguments.Reserve(VFXIndirectArgs.Kind.Quads);
            int dispatch = arguments.Reserve(VFXIndirectArgs.Kind.Dispatch);
            Assert.Equal((0, 1), (quads, dispatch));
            // Ten words an entry: the arguments of one view, then of two.
            Assert.Equal(40, VFXIndirectArgs.Offset(dispatch));
            // An entry given back is the next one reserved; nothing is made before a fill.
            arguments.Release(quads);
            Assert.Equal(quads, arguments.Reserve(VFXIndirectArgs.Kind.Meshes, 36, 6, 4));
            Assert.Equal(2, arguments.Reserve(VFXIndirectArgs.Kind.Points));
            Assert.Null(arguments.Arguments);
            Assert.Equal(0, arguments.Generation);
        }

        [Fact]
        public void AnIndirectOutputIsNotDrawnWithoutARenderer()
        {
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/VfxIndirect\" { SubShader { Pass { Tags { \"LightMode\"=\"UniversalForwardOnly\" } } } }");
            var material = new Material(shader);
            var asset = WithOutput(VFXTaskType.ParticleQuadOutput, material, new VFXMapping("indirectDraw", 1), new VFXMapping("indirectIndex", 0));
            asset.m_Data.Systems[1].tasks[0].buffers = new[] { new VFXMapping("indirectBuffer", 0) };
            var effect = Running(asset);
            var eye = new GameObject("eye").AddComponent<Camera>();
            try
            {
                using var warnings = new Warnings();
                Assert.Equal(new[] { 1, 2 }, Counts(effect, 2));
                // Its list is made on the GPU: without one the effect simulates, says nothing
                // and has no draw.
                var draws = new List<FrameBuilder.DrawItem>();
                Assert.True(effect.TryGetComponent<VFXRenderer>(out var renderer));
                renderer.CollectDraws(new FrameBuilder.CameraContext { Camera = eye, Position = new Vector3(0f, 0f, -10f), Forward = Vector3.forward }, draws);
                Assert.Empty(draws);
                Assert.Empty(warnings.Messages);
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
                Object.DestroyImmediate(eye.gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void TrianglesAndOctagonsAreDrawnByTheirIndices()
        {
            // An octagon: six triangles over its eight points; up to 8192 in one draw, else
            // instances of 1024 (the vertex programs find (vertexID >> 3) + instanceID * 1024).
            var octagons = Primitive.Octagon.Indices(1);
            (uint, uint) Octagons(uint count)
            {
                var setup = Primitive.Octagon.Draw(octagons, count);
                return (setup.IndexCount, setup.Instances);
            }
            Assert.Equal(8192 * 18, octagons.count);
            Assert.Equal((18u, 1u), Octagons(1));
            Assert.Equal((8192u * 18, 1u), Octagons(8192));
            Assert.Equal((1024u * 18, 9u), Octagons(8193));
            Assert.Equal(new ushort[] { 0, 1, 7, 1, 2, 7, 2, 6, 7, 2, 3, 6, 3, 5, 6, 3, 4, 5, 8, 9, 15, 9, 10, 15, 10, 14, 15, 10, 11, 14, 11, 13, 14, 11, 12, 13 }, Primitive.Octagon.Data(2));
            // Triangles: one draw of 32-bit indices however many (their programs read no
            // instance), made for as many as asked and kept while they reach.
            var triangles = Primitive.Triangle.Indices(30000);
            Assert.Equal(4, triangles.stride);
            Assert.True(triangles.count >= 90000);
            var setup = Primitive.Triangle.Draw(triangles, 30000);
            Assert.Equal((90000u, 1u), (setup.IndexCount, setup.Instances));
            Assert.Same(triangles, Primitive.Triangle.Indices(20000));
            // Outgrown in a frame where a draw holds them: kept until a later frame, then released.
            var bigger = Primitive.Triangle.Indices(200000);
            Assert.NotSame(triangles, bigger);
            Assert.True(triangles.IsValid());
            int frame = Time.s_FrameCount;
            try
            {
                Time.s_FrameCount = frame + 1;
                Assert.Same(bigger, Primitive.Triangle.Indices(1));
                Assert.False(triangles.IsValid());
            }
            finally { Time.s_FrameCount = frame; }
            var data = (uint[])Primitive.Triangle.Data(30000);
            Assert.Equal(new uint[] { 89994, 89995, 89996, 89997, 89998, 89999 }, data[89994..]);
            Assert.Equal(new ushort[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 }, Primitive.Quad.Data(2));
        }

        [Fact]
        public void MoreQuadsThanIndicesReachAreDrawnAsInstances()
        {
            var indices = new GraphicsBuffer(GraphicsBuffer.Target.Index, 16384 * 6, 2);
            try
            {
                (uint, uint) Draw(uint quads)
                {
                    var setup = Primitive.Quad.Draw(indices, quads);
                    return (setup.IndexCount, setup.Instances);
                }
                Assert.Equal((6u, 1u), Draw(1));
                Assert.Equal((98304u, 1u), Draw(16384));
                // One more: instances of 2048 quads, the last one partly past the particles.
                Assert.Equal((12288u, 9u), Draw(16385));
                Assert.Equal((12288u, 10u), Draw(20000));
                Assert.Equal((12288u, 10u), Draw(20480));
                Assert.Equal((12288u, 11u), Draw(20481));
            }
            finally { indices.Release(); }
        }

        [Fact]
        public void AnOutputThatIsNotDrawnSaysSoOnceForItsAssetAndItsSystemStillRuns()
        {
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/VfxRefused\" { SubShader { Pass { } } }");
            var material = new Material(shader);
            var system = new VFXEditorSystemDesc { name = "Particles", type = VFXSystemType.Particle };
            var quads = new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput };
            // What is drawn, and each thing that is not.
            Assert.Null(VFXOutput.Refusal(system, quads, material));
            Assert.Equal("its shader was not imported", VFXOutput.Refusal(system, quads, null));
            Assert.Equal("ParticlePointOutput is not drawn yet (quads, triangles, octagons and meshes are)", VFXOutput.Refusal(system, new VFXEditorTaskDesc { type = VFXTaskType.ParticlePointOutput }, material));
            Assert.Null(VFXOutput.Refusal(system, new VFXEditorTaskDesc { type = VFXTaskType.ParticleTriangleOutput }, material));
            Assert.Null(VFXOutput.Refusal(system, new VFXEditorTaskDesc { type = VFXTaskType.ParticleOctagonOutput }, material));
            // Through an indirect buffer: drawn from the list that counts its particles, or from
            // the sorted copy a sort task of its system makes; not from a list no task makes, nor
            // in a system that sorts one list for all its outputs.
            VFXEditorTaskDesc Indirect(int list, int read) => new VFXEditorTaskDesc
            {
                type = VFXTaskType.ParticleQuadOutput,
                parameters = new[] { new VFXMapping("sortPriority", 0), new VFXMapping("indirectDraw", 1), new VFXMapping("indirectIndex", list) },
                buffers = new[] { new VFXMapping("graphValuesBuffer", 4), new VFXMapping("indirectBuffer", read) },
            };
            Assert.Null(VFXOutput.Refusal(system, Indirect(2, 2), material));
            Assert.Equal("it draws a list of particles no task of its system makes or can sort", VFXOutput.Refusal(system, Indirect(2, 3), material));
            var sortingOne = new VFXEditorSystemDesc
            {
                name = "Particles", type = VFXSystemType.Particle,
                tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.PerOutputSort, buffers = new[] { new VFXMapping("srcBuffer", 2), new VFXMapping("scratchBuffer", -1), new VFXMapping("dstBuffer", 3) } } },
            };
            Assert.Null(VFXOutput.Refusal(sortingOne, Indirect(2, 3), material));
            // A sort into the output's indices of another list than the one that counts them.
            var sortingOther = sortingOne;
            sortingOther.tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.PerOutputSort, buffers = new[] { new VFXMapping("srcBuffer", 5), new VFXMapping("scratchBuffer", -1), new VFXMapping("dstBuffer", 3) } } };
            Assert.Equal("it draws a list of particles no task of its system makes or can sort", VFXOutput.Refusal(sortingOther, Indirect(2, 3), material));
            // A list longer than one pass sorts, without a scratch list to merge through.
            sortingOne.capacity = VFXSort.MaxSortedAtOnce + 1;
            Assert.Equal("it draws a list of particles no task of its system makes or can sort", VFXOutput.Refusal(sortingOne, Indirect(2, 3), material));
            var sorting = new VFXEditorSystemDesc { name = "Particles", type = VFXSystemType.Particle, tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.GlobalSort } } };
            Assert.Equal("its system sorts one list for all its outputs, which is not done yet", VFXOutput.Refusal(sorting, Indirect(2, 2), material));
            // A mesh output's task names its mesh and the mask of its submeshes, and draws
            // itself; one through an indirect buffer is not drawn (an output of several meshes
            // compiles to one such task per mesh).
            var meshes = new VFXEditorTaskDesc { type = VFXTaskType.ParticleMeshOutput, parameters = new[] { new VFXMapping("mesh", 1), new VFXMapping("subMeshMask", 2), new VFXMapping("sortPriority", 0) } };
            Assert.Null(VFXOutput.Refusal(system, meshes, material));
            var oneOfSeveral = new VFXEditorTaskDesc
            {
                type = VFXTaskType.ParticleMeshOutput,
                parameters = new[] { new VFXMapping("mesh", 1), new VFXMapping("subMeshMask", 2), new VFXMapping("indirectDraw", 1), new VFXMapping("indirectIndex", 1) },
                buffers = new[] { new VFXMapping("indirectBuffer", 1) },
            };
            Assert.Equal("it draws meshes through an indirect buffer, which is not done yet", VFXOutput.Refusal(system, oneOfSeveral, material));
            Assert.Equal("its task names no mesh or no submesh mask", VFXOutput.Refusal(system, new VFXEditorTaskDesc { type = VFXTaskType.ParticleMeshOutput }, material));
            Assert.Equal("its shader was not imported", VFXOutput.Refusal(system, meshes, null));
            Assert.Null(VFXOutput.Refusal(system, new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, parameters = new[] { new VFXMapping("indirectDraw", 0) } }, material));
            // A strip system's particles: as quads, joined into strips or one by one, that the
            // output draws itself; strips also from a sorted list.
            var stripSystem = new VFXEditorSystemDesc { flags = VFXSystemFlag.SystemHasStrips };
            var joined = new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, parameters = new[] { new VFXMapping("strips", 1) } };
            Assert.Null(VFXOutput.Refusal(stripSystem, joined, material));
            Assert.Null(VFXOutput.Refusal(stripSystem, new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput }, material));
            VFXEditorTaskDesc Joined(VFXEditorTaskDesc task)
            {
                var parameters = new List<VFXMapping>(task.parameters) { new VFXMapping("strips", 1) };
                task.parameters = parameters.ToArray();
                return task;
            }
            var sortedStrips = new VFXEditorSystemDesc
            {
                name = "Strips", type = VFXSystemType.Particle, flags = VFXSystemFlag.SystemHasStrips,
                tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.PerOutputSort, buffers = new[] { new VFXMapping("srcBuffer", 2), new VFXMapping("scratchBuffer", -1), new VFXMapping("dstBuffer", 3) } } },
            };
            Assert.Null(VFXOutput.Refusal(sortedStrips, Joined(Indirect(2, 3)), material));
            Assert.Equal("it draws strips through an indirect buffer that is not sorted, which is not measured yet", VFXOutput.Refusal(sortedStrips, Joined(Indirect(2, 2)), material));
            Assert.Equal("it draws a strip system's particles one by one through an indirect buffer, which is not measured yet", VFXOutput.Refusal(sortedStrips, Indirect(2, 3), material));
            Assert.Equal("ParticleMeshOutput of a strip system's particles is not measured yet", VFXOutput.Refusal(stripSystem, meshes, material));
            Assert.Equal("it draws with a material variant (a graph compiled for editing), which is not measured yet", VFXOutput.Refusal(system, new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, usesMaterialVariant = true }, material));
            var previous = new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, temporaryBuffers = new[] { new VFXMappingTemporary() } };
            Assert.StartsWith("it keeps buffers of the frame before (motion vectors)", VFXOutput.Refusal(system, previous, material));
            // What the generated shader says of itself: an unlit output's passes are drawn...
            const string unlit = "Shader \"Hidden/VFX/G/S/Output Particle|Unlit Quad\" { SubShader { Pass { Tags { \"LightMode\"=\"SceneSelectionPass\" } }\n"
                + "Pass { Tags { \"LightMode\"=\"Picking\" } } Pass { Tags { \"LightMode\"=\"DepthOnly\" } } Pass { Tags { \"LightMode\" = \"DepthNormalsOnly\" } }\n"
                + "Pass { Tags { \"LightMode\"=\"UniversalForwardOnly\" } HLSLPROGRAM\n#define USE_SOFT_PARTICLE 0\n#define VFX_PRIMITIVE_QUAD 1\nENDHLSL }\n"
                + "Pass { Tags { \"LightMode\"=\"Universal2D\" } } Pass { Tags { \"LightMode\"=\"ShadowCaster\" } } } }";
            Assert.Null(VFXOutput.SourceRefusal(unlit));
            Assert.Null(VFXOutput.SourceRefusal(null));
            // ... and so is one that fades against the camera's depth (soft particles: the
            // pipeline's depth texture) ...
            Assert.Null(VFXOutput.SourceRefusal(unlit.Replace("#define USE_SOFT_PARTICLE 0", "#define USE_SOFT_PARTICLE 1")));
            // ... and a lit output's, forward and deferred; one with another pass is not.
            const string lit = "Shader \"Hidden/VFX/G/S/Output Particle|URP Lit|Quad\" { SubShader { Pass { Tags { \"LightMode\"=\"DepthOnly\" } }\n"
                + "Pass { Tags { \"LightMode\"=\"DepthNormals\" } } Pass { Tags { \"LightMode\"=\"UniversalGBuffer\" } }\n"
                + "Pass { Tags { \"LightMode\"=\"UniversalForward\"} HLSLPROGRAM\n#define URP_LIT 1\n#define USE_SOFT_PARTICLE 0\nENDHLSL } Pass { Tags { \"LightMode\"=\"ShadowCaster\" } } } }";
            Assert.Null(VFXOutput.SourceRefusal(lit));
            Assert.Equal("its pass 'MotionVectors' is not drawn yet", VFXOutput.SourceRefusal(unlit.Replace("\"ShadowCaster\"", "\"MotionVectors\"")));
            Assert.Equal("its pass 'MotionVectors' is not drawn yet", VFXOutput.SourceRefusal(lit.Replace("\"DepthNormals\"", "\"MotionVectors\"")));
            Assert.Equal("its pass 'DBufferProjectorVFX' is not drawn yet", VFXOutput.SourceRefusal(unlit.Replace("\"DepthOnly\"", "\"DBufferProjectorVFX\"")));
            // A Shader Graph output of a 2D sprite graph: its sprite and normals passes are drawn
            // by the 2D renderer, its forward pass by the others.
            const string sprite = "Shader \"Hidden/VFX/G/S/Output Particle|Shader Graph Quad - Sprite Lit\" { SubShader { Pass { Tags { \"LightMode\"=\"Universal2D\" } }\n"
                + "Pass { Tags { \"LightMode\"=\"NormalsRendering\" } } Pass { Tags { \"LightMode\"=\"UniversalForward\" } } Pass { Tags { \"LightMode\"=\"SceneSelectionPass\" } } } }";
            Assert.Null(VFXOutput.SourceRefusal(sprite));
            // Read for each output task of a compiled graph from the shader source it names:
            // not for a kernel, a task without a source of the graph's, or a source out of range.
            var compiled = new[]
            {
                new VFXEditorSystemDesc { name = "Spawn", type = VFXSystemType.Spawner },
                new VFXEditorSystemDesc
                {
                    name = "Particles", type = VFXSystemType.Particle,
                    tasks = new[]
                    {
                        new VFXEditorTaskDesc { type = VFXTaskType.Update, shaderSourceIndex = 0 },
                        new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, shaderSourceIndex = 1 },
                        new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, shaderSourceIndex = 2 },
                        new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, shaderSourceIndex = 7 },
                        new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, externalProcessor = material },
                    },
                },
            };
            var sources = new[]
            {
                new VFXShaderSourceDesc { compute = true, source = "Tags { \"LightMode\"=\"MotionVectors\" }" },
                new VFXShaderSourceDesc { source = unlit },
                // Another output of the system, named apart (outputs named alike are not drawn).
                new VFXShaderSourceDesc { source = unlit.Replace("\"ShadowCaster\"", "\"MotionVectors\"").Replace("/Output Particle|Unlit Quad", "/Output Particle MV|Unlit Quad") },
            };
            var found = VFXOutput.SourceRefusals(compiled, sources);
            Assert.Empty(found[0]);
            const string Moving = "its pass 'MotionVectors' is not drawn yet";
            Assert.Equal(new[] { null, null, Moving, null, null }, found[1]);
            // The reason found in the source is the output's when nothing of its task refuses first.
            Assert.Equal(Moving, VFXOutput.Refusal(system, quads, material, Moving));
            Assert.Equal("it draws a list of particles no task of its system makes or can sort", VFXOutput.Refusal(system, Indirect(2, 3), material, Moving));
            // A lit output through an indirect buffer that is not sorted: each is measured, the two together are not.
            VFXEditorTaskDesc From(VFXEditorTaskDesc task, int sourceIndex)
            {
                task.shaderSourceIndex = sourceIndex;
                return task;
            }
            var litIndirect = VFXOutput.SourceRefusals(
                new[] { new VFXEditorSystemDesc { name = "Particles", type = VFXSystemType.Particle, tasks = new[] { From(Indirect(2, 2), 0), From(quads, 0), From(Indirect(2, 2), 1) } } },
                new[] { new VFXShaderSourceDesc { source = lit }, new VFXShaderSourceDesc { source = unlit } });
            Assert.Equal(new[] { VFXOutput.LitIndirectRefusal, null, null }, litIndirect[0]);
            // From a sorted list a lit output is drawn (what an alpha blended one does by default).
            var litSorted = VFXOutput.SourceRefusals(
                new[] { new VFXEditorSystemDesc { name = "Particles", type = VFXSystemType.Particle, tasks = new[] { From(Indirect(2, 3), 0) } } },
                new[] { new VFXShaderSourceDesc { source = lit } });
            Assert.Equal(new string[] { null }, litSorted[0]);
            // Not a lit output of strips, also from a sorted list.
            var litSortedStrips = VFXOutput.SourceRefusals(
                new[] { new VFXEditorSystemDesc { name = "Strips", type = VFXSystemType.Particle, flags = VFXSystemFlag.SystemHasStrips, tasks = new[] { From(Joined(Indirect(2, 3)), 0) } } },
                new[] { new VFXShaderSourceDesc { source = lit } });
            Assert.Equal(new[] { VFXOutput.LitStripRefusal }, litSortedStrips[0]);
            // Strips are measured unlit: a lit output of a strip system is not drawn.
            var litStrips = VFXOutput.SourceRefusals(
                new[] { new VFXEditorSystemDesc { name = "Strips", type = VFXSystemType.Particle, flags = VFXSystemFlag.SystemHasStrips, tasks = new[] { From(quads, 0), From(quads, 1) } } },
                new[] { new VFXShaderSourceDesc { source = lit }, new VFXShaderSourceDesc { source = unlit } });
            Assert.Equal(new[] { VFXOutput.LitStripRefusal, null }, litStrips[0]);
            // A Shader Graph output lit by 2D lights (no lighting define, a normals pass) is lit
            // too: drawn from a sorted list, not from one that is not sorted, nor as strips.
            var sprites = VFXOutput.SourceRefusals(
                new[]
                {
                    new VFXEditorSystemDesc { name = "Particles", type = VFXSystemType.Particle, tasks = new[] { From(Indirect(2, 3), 0), From(Indirect(2, 2), 0), From(Indirect(2, 2), 1) } },
                    new VFXEditorSystemDesc { name = "Strips", type = VFXSystemType.Particle, flags = VFXSystemFlag.SystemHasStrips, tasks = new[] { From(Joined(Indirect(2, 3)), 0) } },
                },
                new[] { new VFXShaderSourceDesc { source = sprite }, new VFXShaderSourceDesc { source = sprite.Replace("\"NormalsRendering\"", "\"UniversalForward\"").Replace("Sprite Lit", "Sprite Unlit") } });
            Assert.Equal(new[] { null, VFXOutput.LitIndirectRefusal, null }, sprites[0]);
            Assert.Equal(new[] { VFXOutput.LitStripRefusal }, sprites[1]);

            var asset = WithOutput(VFXTaskType.ParticleQuadOutput, material);
            asset.m_Data.Systems[1].tasks[0].parameters = Indirect(2, 3).parameters;
            asset.m_Data.Systems[1].tasks[0].buffers = Indirect(2, 3).buffers;
            asset.m_Name = "Smoke";
            VisualEffect first = null, second = null;
            var eye = new GameObject("eye").AddComponent<Camera>();
            try
            {
                using var warnings = new Warnings();
                first = Running(asset);
                first.name = "chimney";
                // Said when the effect's systems are made, once for the asset.
                first.Reinit();
                second = Running(asset);
                var said = Assert.Single(warnings.Messages, m => m.Contains("not drawn"));
                Assert.StartsWith("Unbound: VisualEffect '", said);
                Assert.Contains("(Smoke): an output of 'Particles' is not drawn: it draws a list of particles no task of its system makes or can sort", said);
                // The system runs all the same, and its renderer has no draw.
                Assert.Equal(new[] { 1, 2, 3 }, Counts(first, 3));
                var draws = new List<FrameBuilder.DrawItem>();
                Assert.True(first.TryGetComponent<VFXRenderer>(out var renderer));
                renderer.CollectDraws(new FrameBuilder.CameraContext { Camera = eye, Position = new Vector3(0f, 0f, -10f), Forward = Vector3.forward }, draws);
                Assert.Empty(draws);
            }
            finally
            {
                if (first) Object.DestroyImmediate(first.gameObject);
                if (second) Object.DestroyImmediate(second.gameObject);
                Object.DestroyImmediate(eye.gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void EventsPauseRateStepsAndReinitMoveTheEffectAsAsked()
        {
            var effect = Running(Counting(30f));
            try
            {
                // 30 a second: one particle a step.
                Assert.Equal(new[] { 1, 2, 3 }, Counts(effect, 3));
                // A stop is taken at the next step: nothing more is spawned.
                effect.Stop();
                Assert.Equal(new[] { 3, 3 }, Counts(effect, 2));
                Assert.Equal(VFXSpawnerLoopState.Finished, effect.GetSpawnSystemInfo("Spawn").loopState);
                effect.Play();
                Assert.Equal(new[] { 4, 5 }, Counts(effect, 2));
                // Paused: steps of no time. One frame advanced runs as if it were not.
                effect.pause = true;
                Assert.Equal(new[] { 5, 5 }, Counts(effect, 2));
                effect.AdvanceOneFrame();
                Assert.Equal(new[] { 6, 6 }, Counts(effect, 2));
                effect.pause = false;
                effect.playRate = 2f;
                Assert.Equal(new[] { 8, 10 }, Counts(effect, 2));
                effect.playRate = 1f;
                // Three steps of a tenth of a second by hand (nine particles), then the frame's own.
                effect.Simulate(0.1f, 3);
                Assert.Equal(new[] { 20 }, Counts(effect, 1));
                // Started over: nothing spawned, and the initial event plays it again.
                effect.Reinit();
                Assert.Equal(0, effect.aliveParticleCount);
                Assert.Equal(VFXSpawnerLoopState.Finished, effect.GetSpawnSystemInfo("Spawn").loopState);
                Assert.Equal(new[] { 1, 2 }, Counts(effect, 2));
                // Disabled, it keeps its systems and its state; enabled again, it starts over.
                Counts(effect, 1);
                effect.enabled = false;
                Assert.Equal(3, effect.aliveParticleCount);
                Assert.Equal(VFXSpawnerLoopState.Looping, effect.GetSpawnSystemInfo("Spawn").loopState);
                // Its renderer is off with it (no camera takes it), and on again with it.
                Assert.True(effect.TryGetComponent<VFXRenderer>(out var renderer));
                Assert.False(renderer.enabled);
                effect.enabled = true;
                Assert.True(renderer.enabled);
                effect.gameObject.SetActive(false);
                Assert.False(renderer.enabled);
                effect.gameObject.SetActive(true);
                Assert.True(renderer.enabled);
                Assert.Equal(0, effect.aliveParticleCount);
                Assert.Equal(VFXSpawnerLoopState.Finished, effect.GetSpawnSystemInfo("Spawn").loopState);
                Assert.Equal(new[] { 1, 2 }, Counts(effect, 2));
                // Without its initial event it waits for a play.
                effect.Reinit(false);
                Assert.Equal(new[] { 0, 0 }, Counts(effect, 2));
                effect.SendEvent("OnPlay");
                Assert.Equal(new[] { 1 }, Counts(effect, 1));
                // An event the graph does not have changes nothing.
                effect.SendEvent("Nothing");
                Assert.Equal(new[] { 2 }, Counts(effect, 1));
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        [Fact]
        public void AnEffectNoCameraSeesRunsItsStartThenWaitsUnlessItsAssetSaysOtherwise()
        {
            var culledByDefault = Running(Counting(30f, VFXCullingFlags.CullDefault));
            var never = Running(Counting(30f, VFXCullingFlags.CullNone));
            try
            {
                // Before its first update an effect counts as culled.
                Assert.True(culledByDefault.culled);
                // The first update runs whole (nothing was simulated yet); no camera ever takes
                // the renderer, so from then on only the asset that does not cull simulates.
                Assert.Equal(new[] { 1, 1, 1 }, Counts(culledByDefault, 3));
                Assert.True(culledByDefault.culled);
                Assert.Equal(new[] { 1, 2, 3 }, Counts(never, 3));
                Assert.True(never.culled);
                // A culled effect that does not simulate keeps its events for when it does.
                culledByDefault.Stop();
                Assert.Equal(new[] { 1 }, Counts(culledByDefault, 1));
                Assert.Equal(VFXSpawnerLoopState.Looping, culledByDefault.GetSpawnSystemInfo("Spawn").loopState);
            }
            finally
            {
                Object.DestroyImmediate(culledByDefault.gameObject);
                Object.DestroyImmediate(never.gameObject);
            }
        }

        [Fact]
        public void SystemGettersAnswerNothingForUnknownNamesAndThrowForAParticleSystemOfTheWrongKind()
        {
            var effect = Running(Counting(30f));
            try
            {
                Assert.Equal(default, effect.GetParticleSystemInfo("Nothing"));
                Assert.Equal(default, effect.GetParticleSystemInfo("Spawn"));
                Assert.Equal(VFXSpawnerLoopState.Finished, effect.GetSpawnSystemInfo("Nothing").loopState);
                var wrong = Assert.Throws<ArgumentException>(() => effect.GetSpawnSystemInfo("Particles"));
                Assert.Equal("System \"Particles\" does not exist or is not a particle system", wrong.Message);
                Assert.Throws<NullReferenceException>(() => effect.GetSpawnSystemInfo(Shader.PropertyToID("Spawn"), null));
                // A disabled effect keeps its systems: they still answer.
                effect.enabled = false;
                Assert.Equal(20u, effect.GetParticleSystemInfo("Particles").capacity);
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }
    }
}
