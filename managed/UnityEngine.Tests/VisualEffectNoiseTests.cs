using System;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.VFX;
using Xunit;
using static UnityEngine.Tests.VisualEffectSimulationTests;

namespace UnityEngine.Tests
{
    /// <summary>The noise expressions compute on the CPU. The expected bits are the Unity
    /// 6000.0.71f1 Editor's (tests/projects/vfx, <c>noise</c>).</summary>
    public class VisualEffectNoiseTests
    {
        static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
        static uint B(float value) => BitConverter.SingleToUInt32Bits(value);

        static void Same(uint[] expected, params float[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i].ToString("x8"), B(actual[i]).ToString("x8"));
        }

        [Fact]
        public void ValueNoiseAndItsDerivativesAreTheEditorsBits()
        {
            var one = VFXExpressionNoise.GenerateValueNoise1D(F(0x40912068), F(0xc030b22a), 2, F(0x3fb2deec), F(0xc023b630));
            Same(new uint[] { 0x3eb3980c, 0xbe58579c }, one.x, one.y);
            // A cell at the far end of the lattice's period and a negative persistence.
            one = VFXExpressionNoise.GenerateValueNoise1D(F(0xc28a0000), F(0xc0223cb1), 1, F(0xbfb792a3), F(0x3f835737));
            Same(new uint[] { 0x3f1720a3, 0x3de26387 }, one.x, one.y);
            var two = VFXExpressionNoise.GenerateValueNoise2D(new Vector2(F(0x40f5615a), F(0xc04fccdc)), F(0x401bd830), 4, F(0x3f865e02), F(0xbf28c462));
            Same(new uint[] { 0x3eaf44c9, 0x3da298cf, 0xbe36ce0d }, two.x, two.y, two.z);
            two = VFXExpressionNoise.GenerateValueNoise2D(new Vector2(70.5f, 68f), F(0xc006f452), 5, F(0xbf8b2c64), F(0x3fcc0b0a));
            Same(new uint[] { 0x3e525fb1, 0xbe36d4b2, 0xbd59b643 }, two.x, two.y, two.z);
        }

        [Fact]
        public void PerlinNoiseAndItsDerivativesAreTheEditorsBits()
        {
            var one = VFXExpressionNoise.GeneratePerlinNoise1D(F(0x4039cbdc), F(0xc02550d0), 4, F(0x3f70834c), F(0xbe0e7208));
            Same(new uint[] { 0xbe4b37d3, 0x3e5fad83 }, one.x, one.y);
            var two = VFXExpressionNoise.GeneratePerlinNoise2D(new Vector2(F(0xbbb09000), F(0xc090ab64)), F(0x3eb8ec48), 5, F(0xbf402a1b), F(0xc02545c4));
            Same(new uint[] { 0xbf663e35, 0xbf71658f, 0xbeb252e8 }, two.x, two.y, two.z);
            var three = VFXExpressionNoise.GeneratePerlinNoise3D(new Vector3(F(0x40ac7c2c), F(0xc0e5803a), F(0x40034ef4)), F(0xbfd25694), 3, F(0xbf024f9a), F(0xbf97f206));
            Same(new uint[] { 0x3e236b02, 0x3f33f5b4, 0x3e3326c4, 0x3ed91806 }, three.x, three.y, three.z, three.w);
        }

        [Fact]
        public void CellularNoiseAndItsDerivativesAreTheEditorsBits()
        {
            var one = VFXExpressionNoise.GenerateCellularNoise1D(F(0xc0053218), F(0xbb209200), 5, F(0xbf8813b5), F(0x3fec93f1));
            Same(new uint[] { 0x3dd66980, 0x3ed47011 }, one.x, one.y);
            var three = VFXExpressionNoise.GenerateCellularNoise3D(new Vector3(0f, -1f, -69f), F(0x3fcb989e), 5, F(0xbf9fa992), F(0xc0059714));
            Same(new uint[] { 0x3e77f97e, 0x3d8e9d97, 0xbfa07f0f, 0x3eab339c }, three.x, three.y, three.z, three.w);
        }

        [Fact]
        public void CurlsAreTheEditorsBits()
        {
            var two = VFXExpressionNoise.GeneratePerlinCurlNoise2D(new Vector2(F(0xc0ce09b8), F(0xc0bbeffe)), F(0xbf84a668), 2, F(0xbf94f0f8), F(0x40255421));
            Same(new uint[] { 0x40a808ee, 0x40c1e445 }, two.x, two.y);
            var three = VFXExpressionNoise.GenerateValueCurlNoise3D(new Vector3(F(0x3f2d2db0), F(0x4067a94c), F(0xc08ee5cc)), F(0x40137c36), 5, F(0x3f5c7cbe), F(0x40287647));
            Same(new uint[] { 0xbd8d2d67, 0xbe59c99d, 0x3e52078e }, three.x, three.y, three.z);
            three = VFXExpressionNoise.GenerateCellularCurlNoise3D(new Vector3(F(0x40ef12cc), F(0x3fdade58), F(0x3fd4e960)), F(0xc0198350), 4, F(0xbf89b752), F(0x3fa0874d));
            Same(new uint[] { 0x3ed5845a, 0x40f28fe1, 0x408bba58 }, three.x, three.y, three.z);
        }

        [Fact]
        public void NoOctavesGiveNoNumberAndAnInfiniteCoordinateKeepsTheFiniteParts()
        {
            var none = VFXExpressionNoise.GeneratePerlinNoise2D(new Vector2(3f, 70f), 1f, 0, 0.5f, 2f);
            Assert.True(float.IsNaN(none.x) && float.IsNaN(none.y) && float.IsNaN(none.z));
            var negative = VFXExpressionNoise.GenerateValueNoise1D(0.5f, 1f, -3, 0.5f, 2f);
            Assert.True(float.IsNaN(negative.x) && float.IsNaN(negative.y));
            // The curl over the planes an infinite coordinate is not in stays a number.
            var curl = VFXExpressionNoise.GenerateCellularCurlNoise3D(new Vector3(68f, 68f, float.PositiveInfinity), F(0x3feba6e5), 5, F(0xbf943a9e), F(0x3e096538));
            Assert.True(float.IsNaN(curl.x) && float.IsNaN(curl.y));
            Assert.Equal("3f7b340c", B(curl.z).ToString("x8"));
            // A coordinate past the range of a 32-bit integer does not stop the noise.
            var huge = VFXExpressionNoise.GenerateCellularNoise1D(1e12f, 1f, 1, 0.5f, 2f);
            Assert.False(float.IsNaN(huge.x));
        }

        [Fact]
        public void TheVoronoiNoiseIsNoNumberAndNoGraphEvaluatesIt()
        {
            Assert.True(float.IsNaN(VFXExpressionNoise.GenerateVoroNoise2D(new Vector2(1.25f, -2.6f), 1f, 0.5f, 0.5f)));
            Assert.True(float.IsNaN(VFXExpressionNoise.GenerateVoroNoise2D(new Vector2(0f, 0f), 1f, 0f, 1f)));
            Assert.False(VFXExpressionEvaluator.Supported(VFXExpressionOperation.VoroNoise2D));
        }

        static readonly Vector3 point = new Vector3(F(0x40ac7c2c), F(0xc0e5803a), F(0x40034ef4));
        static readonly Vector3 tuning = new Vector3(F(0xbfd25694), F(0xbf024f9a), F(0xbf97f206));

        /// <summary>Perlin noise of a graph whose operation reads the count of octaves as it is.</summary>
        static Vector4 Unclamped(int octaves)
        {
            var data = Data(new[] { Value(VFXValueType.Float3), Value(VFXValueType.Float3), Value(VFXValueType.Int32), Op(VFXExpressionOperation.PerlinNoise3D, 0, 1, 2) },
                new[] { Constant(0, point), Constant(1, tuning), Constant(2, octaves) });
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            return values.GetVector(3, 4);
        }

        [Fact]
        public void AGraphsNoiseOperationsReadACoordinateItsTuningAndAnOctaveCount()
        {
            var data = Data(new[]
            {
                Value(VFXValueType.Float3), Value(VFXValueType.Float3), Value(VFXValueType.Int32),
                Op(VFXExpressionOperation.PerlinNoise3D, 0, 1, 2),
                Value(VFXValueType.Float),
                Op(VFXExpressionOperation.ValueNoise1D, 4, 1, 2),
                Value(VFXValueType.Float2),
                Op(VFXExpressionOperation.CellularCurlNoise2D, 6, 1, 2),
            }, new[]
            {
                Constant(0, point), Constant(1, tuning), Constant(2, 3), Constant(4, 2.5f), Constant(6, new Vector2(0.4f, -3.2f)),
            });
            foreach (var op in data.Operations) Assert.True(VFXExpressionEvaluator.Supported(op), op.ToString());
            Assert.True(float.IsNaN(Unclamped(0).x), "a graph that reads no octaves sums nothing, as the folded noise");
            // Not an Editor value (more than eight octaves are not measured): the graph's path and
            // the folded one compute the same.
            Assert.Equal(VFXExpressionNoise.GeneratePerlinNoise3D(point, tuning.x, 12, tuning.y, tuning.z), Unclamped(12));
            var values = new VisualEffectValues(data);
            VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
            var perlin = values.GetVector(3, 4);
            Same(new uint[] { 0x3e236b02, 0x3f33f5b4, 0x3e3326c4, 0x3ed91806 }, perlin.x, perlin.y, perlin.z, perlin.w);
            var line = VFXExpressionNoise.GenerateValueNoise1D(2.5f, tuning.x, 3, tuning.y, tuning.z);
            Assert.Equal(new Vector4(line.x, line.y, 0f, 0f), values.GetVector(5, 2));
            var curl = VFXExpressionNoise.GenerateCellularCurlNoise2D(new Vector2(0.4f, -3.2f), tuning.x, 3, tuning.y, tuning.z);
            Assert.Equal(new Vector4(curl.x, curl.y, 0f, 0f), values.GetVector(7, 2));
        }
    }
}
