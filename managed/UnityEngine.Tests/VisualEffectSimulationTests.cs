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
    public class VisualEffectSimulationTests
    {
        const float Frame = 1f / 30f;

        static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

        static VFXExpressionDesc Value(VFXValueType type) =>
            new VFXExpressionDesc { op = VFXExpressionOperation.Value, data = new[] { -1, -1, -1, (int)type } };

        static VFXExpressionDesc Op(VFXExpressionOperation op, int a = -1, int b = -1, int c = -1, int d = -1) =>
            new VFXExpressionDesc { op = op, data = new[] { a, b, c, d } };

        static VFXExpressionValueContainerDesc Constant<T>(int expression, T value) =>
            new VFXExpressionValueContainerDesc<T> { expressionIndex = (uint)expression, value = value };

        static VisualEffectCompiledData Data(VFXExpressionDesc[] expressions, VFXExpressionValueContainerDesc[] values,
            VFXEditorSystemDesc[] systems = null, VFXEventDesc[] events = null, int spawners = 0)
        {
            var resource = new VisualEffectResource();
            // The event attributes (one per spawner after the asset's own): a spawn count of one.
            var buffers = new VFXCPUBufferDesc[spawners + 1];
            for (int i = 0; i < buffers.Length; i++)
            {
                var initial = new VFXCPUBufferData();
                initial.PushFloat(1f);
                buffers[i] = new VFXCPUBufferDesc
                {
                    capacity = 1, stride = 1, initialData = initial,
                    layout = new[] { new VFXLayoutElementDesc { name = "spawnCount", type = VFXValueType.Float, offset = new VFXLayoutOffset { structure = 1 } } },
                };
            }
            resource.SetRuntimeData(new VFXExpressionSheet { expressions = expressions, values = values, exposed = Array.Empty<VFXExposedMapping>() },
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
            var color = Data(new[] { Value(VFXValueType.Float3), Op(VFXExpressionOperation.RGBtoHSV, 0) }, new[] { Constant(0, Vector3.one) });
            Assert.Equal("the expression operation RGBtoHSV", VisualEffect.Unsupported(color));
            var strips = Data(Array.Empty<VFXExpressionDesc>(), Array.Empty<VFXExpressionValueContainerDesc>(), new[]
            {
                new VFXEditorSystemDesc { name = "Trail", type = VFXSystemType.Particle, layer = 0, flags = VFXSystemFlag.SystemHasKill | VFXSystemFlag.SystemHasStrips, tasks = Array.Empty<VFXEditorTaskDesc>() },
            });
            Assert.Equal("SystemHasStrips of the system 'Trail'", VisualEffect.Unsupported(strips));
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
        static VisualEffectAsset Counting(float rate, VFXCullingFlags culling = VFXCullingFlags.CullNone, VFXUpdateMode mode = VFXUpdateMode.FixedDeltaTime)
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
            var data = Data(new[] { Value(VFXValueType.Float) }, new[] { Constant(0, rate) }, new[] { spawner, particles }, new[]
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

        static readonly VFXManager.FrameTime Thirtieth = new VFXManager.FrameTime { FixedSteps = 2, FixedTime = Frame, DeltaTime = Frame, UnscaledFixedSteps = 2, UnscaledFixedTime = Frame, UnscaledDeltaTime = Frame };

        static VisualEffect Running(VisualEffectAsset asset)
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
            bool warned = VisualEffect.s_WarnedNotDrawn;
            VisualEffect.s_WarnedNotDrawn = true;
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
                VisualEffect.s_WarnedNotDrawn = warned;
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
            bool warned = VisualEffect.s_WarnedNotDrawn;
            VisualEffect.s_WarnedNotDrawn = true;
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
                VisualEffect.s_WarnedNotDrawn = warned;
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        [Fact]
        public void EventsPauseRateStepsAndReinitMoveTheEffectAsAsked()
        {
            bool warned = VisualEffect.s_WarnedNotDrawn;
            VisualEffect.s_WarnedNotDrawn = true;
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
                VisualEffect.s_WarnedNotDrawn = warned;
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        [Fact]
        public void AnEffectNoCameraSeesRunsItsStartThenWaitsUnlessItsAssetSaysOtherwise()
        {
            bool warned = VisualEffect.s_WarnedNotDrawn;
            VisualEffect.s_WarnedNotDrawn = true;
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
                VisualEffect.s_WarnedNotDrawn = warned;
                Object.DestroyImmediate(culledByDefault.gameObject);
                Object.DestroyImmediate(never.gameObject);
            }
        }

        [Fact]
        public void SystemGettersAnswerNothingForUnknownNamesAndThrowForAParticleSystemOfTheWrongKind()
        {
            bool warned = VisualEffect.s_WarnedNotDrawn;
            VisualEffect.s_WarnedNotDrawn = true;
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
                VisualEffect.s_WarnedNotDrawn = warned;
                Object.DestroyImmediate(effect.gameObject);
            }
        }
    }
}
