using System;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.VFX;
using Xunit;
using static UnityEngine.Tests.VisualEffectSimulationTests;

namespace UnityEngine.Tests
{
    /// <summary>Expression operations of matrices, colour spaces and object sizes, on compiled
    /// graphs made by hand.</summary>
    public class VisualEffectOperationTests
    {
        static VisualEffectValues Evaluated(VisualEffectCompiledData data)
        {
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            return values;
        }

        static uint[] Words(Vector4 v) => new[] { BitConverter.SingleToUInt32Bits(v.x), BitConverter.SingleToUInt32Bits(v.y), BitConverter.SingleToUInt32Bits(v.z) };

        [Fact]
        public void MatricesAreMadeOfRowsColumnsAndAxesAndSplitIntoThem()
        {
            var a = new Vector4(1f, 2f, 3f, 4f);
            var b = new Vector4(5f, -6f, 7f, 8f);
            var c = new Vector4(-1f, 0.5f, 0.25f, 2f);
            var d = new Vector4(0f, 0.125f, -3f, 1f);
            var data = Data(new[]
            {
                Value(VFXValueType.Float4), Value(VFXValueType.Float4), Value(VFXValueType.Float4), Value(VFXValueType.Float4),
                Op(VFXExpressionOperation.RowToMatrix, 0, 1, 2, 3),
                Op(VFXExpressionOperation.ColumnToMatrix, 0, 1, 2, 3),
                Value(VFXValueType.Int32),
                Op(VFXExpressionOperation.MatrixToRow, 4, 6),
                Op(VFXExpressionOperation.MatrixToColumn, 4, 6),
                Value(VFXValueType.Float3), Value(VFXValueType.Float3), Value(VFXValueType.Float3), Value(VFXValueType.Float3),
                Op(VFXExpressionOperation.AxisToMatrix, 9, 10, 11, 12),
            }, new[]
            {
                Constant(0, a), Constant(1, b), Constant(2, c), Constant(3, d), Constant(6, 2),
                Constant(9, new Vector3(1f, 0f, 0f)), Constant(10, new Vector3(0f, 0f, 1f)), Constant(11, new Vector3(0f, -1f, 0f)), Constant(12, new Vector3(4f, 5f, 6f)),
            });
            foreach (var op in data.Operations) Assert.True(VFXExpressionEvaluator.Supported(op), op.ToString());
            var values = Evaluated(data);
            var rows = values.GetMatrix(4);
            Assert.Equal(c, rows.GetRow(2));
            var columns = values.GetMatrix(5);
            Assert.Equal(b, columns.GetColumn(1));
            Assert.Equal(c, values.GetVector(7, 4));
            Assert.Equal(rows.GetColumn(2), values.GetVector(8, 4));
            var axes = values.GetMatrix(13);
            Assert.Equal(new Vector4(0f, 0f, 1f, 0f), axes.GetColumn(1));
            Assert.Equal(new Vector4(4f, 5f, 6f, 1f), axes.GetColumn(3));
        }

        [Fact]
        public void AMatrixsAxisIsAColumnWithoutItsLastRow()
        {
            // A frame flat along an axis that is turned: its first column is zero, no row is.
            var flat = Matrix4x4.TRS(new Vector3(7f, 8f, 9f), Quaternion.Euler(0f, 0f, 40f), new Vector3(0f, 2f, 3f));
            flat.m30 = 0.25f;
            flat.m31 = 0.5f;
            var data = Data(new[]
            {
                Value(VFXValueType.Matrix4x4), Value(VFXValueType.Int32), Value(VFXValueType.Int32), Value(VFXValueType.Int32),
                Op(VFXExpressionOperation.MatrixToAxis, 0, 1),
                Op(VFXExpressionOperation.MatrixToAxis, 0, 2),
                Op(VFXExpressionOperation.MatrixToAxis, 0, 3),
                Value(VFXValueType.Int32),
                Op(VFXExpressionOperation.MatrixToAxis, 0, 7),
            }, new[] { Constant(0, flat), Constant(1, 0), Constant(2, 1), Constant(3, 2), Constant(7, 3) });
            foreach (var op in data.Operations) Assert.True(VFXExpressionEvaluator.Supported(op), op.ToString());
            var values = Evaluated(data);
            Assert.Equal(Vector4.zero, values.GetVector(4, 3));
            Vector4 second = flat.GetColumn(1), third = flat.GetColumn(2);
            Assert.Equal(new Vector4(second.x, second.y, second.z, 0f), values.GetVector(5, 3));
            Assert.Equal(new Vector4(third.x, third.y, third.z, 0f), values.GetVector(6, 3));
            Assert.NotEqual(0f, second.x);
            // The fourth column is the frame's position (a position on a mesh reads it).
            Assert.Equal(new Vector4(7f, 8f, 9f, 0f), values.GetVector(8, 3));
        }

        [Fact]
        public void AFramesPositionAnglesAndScaleComeFromItsAffinePart()
        {
            var frame = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.Euler(30f, -120f, 200f), new Vector3(2f, 0.5f, 3f));
            // A last row that is not (0, 0, 0, 1) is left out of the angles and the scale.
            var bent = frame;
            bent.m30 = 0.25f;
            bent.m33 = 2f;
            var data = Data(new[]
            {
                Value(VFXValueType.Matrix4x4),
                Op(VFXExpressionOperation.ExtractPositionFromMatrix, 0),
                Op(VFXExpressionOperation.ExtractAnglesFromMatrix, 0),
                Op(VFXExpressionOperation.ExtractScaleFromMatrix, 0),
            }, new[] { Constant(0, bent) });
            var values = Evaluated(data);
            Assert.Equal(new Vector4(1f, 2f, 3f, 0f), values.GetVector(1, 3));
            var angles = Quaternion.ToEulerRad(frame.rotation) * Mathf.Rad2Deg;
            Assert.Equal(new Vector4(angles.x, angles.y, angles.z, 0f), values.GetVector(2, 3));
            // Not made positive, as eulerAngles is.
            Assert.True(angles.y < 0f || angles.x < 0f || angles.z < 0f);
            var scale = frame.lossyScale;
            Assert.Equal(new Vector4(scale.x, scale.y, scale.z, 0f), values.GetVector(3, 3));
        }

        [Fact]
        public void TheAnglesOfAFrameWithoutARotationComeFromItsRotationAsItIs()
        {
            // The rotation of a frame of zeros or with a zero axis is not of unit length: its
            // angles are taken from it as it is, where Quaternion.eulerAngles normalizes it first
            // (values of the Unity 6000.0.71f1 Editor, tests/projects/vfx operations/*/v05).
            var at = new Vector3(10f, 20f, 30f);
            var turn = Quaternion.Euler(30f, -120f, 200f);
            var data = Data(new[]
            {
                Value(VFXValueType.Matrix4x4), Op(VFXExpressionOperation.ExtractAnglesFromMatrix, 0),
                Value(VFXValueType.Matrix4x4), Op(VFXExpressionOperation.ExtractAnglesFromMatrix, 2),
            }, new[] { Constant(0, Matrix4x4.zero), Constant(2, Matrix4x4.TRS(at, turn, new Vector3(2f, 0f, 3f))) });
            var values = Evaluated(data);
            // The angles go through MathF.Atan2, the platform's math library: on macOS it rounds the
            // last bit of these two the other way (the C library's arithmetic, not emulated).
            var zeroFrame = OperatingSystem.IsMacOS()
                ? new[] { 0xc207a65du, 0xc218130du, 0xc218130du }
                : new[] { 0xc207a65du, 0xc218130cu, 0xc218130cu };
            Assert.Equal(zeroFrame, Words(values.GetVector(1, 3)));
            Assert.Equal(new[] { 0x41f00001u, 0xc2f00001u, 0xc3200000u }, Words(values.GetVector(3, 3)));
            var normalized = Quaternion.ToEulerRad(Matrix4x4.zero.rotation) * Mathf.Rad2Deg;
            Assert.NotEqual(0xc207a65du, BitConverter.SingleToUInt32Bits(normalized.x));
        }

        [Fact]
        public void ColoursGoToHueSaturationValueAndBackBySixths()
        {
            var data = Data(new[]
            {
                Value(VFXValueType.Float3), Op(VFXExpressionOperation.RGBtoHSV, 0),
                Value(VFXValueType.Float3), Op(VFXExpressionOperation.HSVtoRGB, 2),
            }, new[] { Constant(0, new Vector3(0.8f, 0.3f, 0.6f)), Constant(2, new Vector3(0.71f, 0.6f, 0.9f)) });
            var values = Evaluated(data);
            Color.RGBToHSV(new Color(0.8f, 0.3f, 0.6f), out float h, out float s, out float v);
            Assert.Equal(new Vector4(h, s, v, 0f), values.GetVector(1, 3));
            // 0.71 of the circle is in its fifth sixth (240 to 300 degrees): red from the hue's
            // place in it, green the least, blue the most.
            float hue = 0.71f * 360f, chroma = 0.6f * 0.9f, second = (1f - MathF.Abs(hue / 60f % 2f - 1f)) * chroma, rest = 0.9f - chroma;
            Assert.Equal(new Vector4(second + rest, 0f + rest, rest + chroma, 0f), values.GetVector(3, 3));
            // The middle of every sixth, where the second component is half the chroma: the
            // largest component turns red, yellow, green, cyan, blue, magenta; a hue of one gives
            // what a hue of zero does.
            var sixths = new[]
            {
                (1f / 12f, new Vector3(1f, 0.5f, 0f)), (3f / 12f, new Vector3(0.5f, 1f, 0f)), (5f / 12f, new Vector3(0f, 1f, 0.5f)),
                (7f / 12f, new Vector3(0f, 0.5f, 1f)), (9f / 12f, new Vector3(0.5f, 0f, 1f)), (11f / 12f, new Vector3(1f, 0f, 0.5f)),
                (1f, new Vector3(1f, 0f, 0f)),
                // A hue outside the circle is not wrapped into it: the second component goes
                // negative below it and the last sixth's rule holds above it (tests/projects/vfx
                // operations/*/q04).
                (-0.25f, new Vector3(1f, -1.5f, 0f)), (1.25f, new Vector3(1f, 0f, 0.5f)),
            };
            foreach (var (h0, expected) in sixths)
            {
                var one = Data(new[] { Value(VFXValueType.Float3), Op(VFXExpressionOperation.HSVtoRGB, 0) }, new[] { Constant(0, new Vector3(h0, 1f, 1f)) });
                var rgb = Evaluated(one).GetVector(1, 3);
                Assert.Equal(expected.x, rgb.x, 5);
                Assert.Equal(expected.y, rgb.y, 5);
                Assert.Equal(expected.z, rgb.z, 5);
            }
        }

        [Fact]
        public void AnyMatrixIsInvertedAndOneWithoutAnInverseGivesZeros()
        {
            // A perspective projection with a lens shift: not affine, so its affine inverse would
            // be wrong.
            var projection = Matrix4x4.Perspective(63f, 1.5f, 0.3f, 1000f);
            projection.m02 = -0.2f;
            var data = Data(new[]
            {
                Value(VFXValueType.Matrix4x4), Op(VFXExpressionOperation.InverseMatrix, 0),
                Value(VFXValueType.Matrix4x4), Op(VFXExpressionOperation.InverseMatrix, 2),
            }, new[] { Constant(0, projection), Constant(2, new Matrix4x4(new Vector4(1f, 2f, 3f, 4f), new Vector4(2f, 4f, 6f, 8f), Vector4.one, new Vector4(0f, 0f, 0f, 1f))) });
            Assert.True(VFXExpressionEvaluator.Supported(VFXExpressionOperation.InverseMatrix));
            var values = Evaluated(data);
            Assert.Equal(projection.inverse, values.GetMatrix(1));
            var product = projection * values.GetMatrix(1);
            for (int i = 0; i < 16; i++) Assert.Equal(Matrix4x4.identity[i], product[i], 4);
            Assert.Equal(Matrix4x4.zero, values.GetMatrix(3));
        }

        [Fact]
        public void ATexturesSizeAndABuffersCountAreZeroWithoutOne()
        {
            var texture = new Texture2D(8, 4);
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 7, 12);
            try
            {
                var data = Data(new[]
                {
                    Value(VFXValueType.Texture2D), Op(VFXExpressionOperation.TextureWidth, 0), Op(VFXExpressionOperation.TextureHeight, 0),
                    Value(VFXValueType.Buffer), Op(VFXExpressionOperation.BufferCount, 3),
                }, Array.Empty<VFXExpressionValueContainerDesc>());
                var values = new VisualEffectValues(data);
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
                Assert.Equal(0u, values.GetWord(1));
                Assert.Equal(0u, values.GetWord(4));
                values.SetObject(0, texture);
                values.SetObject(3, buffer);
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
                Assert.Equal(8u, values.GetWord(1));
                Assert.Equal(4u, values.GetWord(2));
                Assert.Equal(7u, values.GetWord(4));
                // A buffer released while it is set keeps its count; a texture destroyed while
                // it is set has no size (tests/projects/vfx operations/*/u00..u02).
                buffer.Release();
                Object.DestroyImmediate(texture);
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
                Assert.Equal(0u, values.GetWord(1));
                Assert.Equal(0u, values.GetWord(2));
                Assert.Equal(7u, values.GetWord(4));
            }
            finally
            {
                buffer.Release();
                Object.DestroyImmediate(texture);
            }
        }
    }
}
