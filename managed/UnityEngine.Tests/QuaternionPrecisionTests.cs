using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class QuaternionPrecisionTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        public QuaternionPrecisionTests() => ProjectInfo.UnityVersion = "6000.0.71f1";
        public void Dispose() => ProjectInfo.UnityVersion = m_Release;
        static float F(int bits) => BitConverter.Int32BitsToSingle(bits);
        static Quaternion Q(int x, int y, int z, int w) => new Quaternion(F(x), F(y), F(z), F(w));
        static void Bits(Quaternion q, int x, int y, int z, int w)
        {
            Assert.Equal(x, BitConverter.SingleToInt32Bits(q.x));
            Assert.Equal(y, BitConverter.SingleToInt32Bits(q.y));
            Assert.Equal(z, BitConverter.SingleToInt32Bits(q.z));
            Assert.Equal(w, BitConverter.SingleToInt32Bits(q.w));
        }

        // Unity 6000.0.71f1, quaternioneuler fixed-input controls.
        [Theory]
        [InlineData(0, 0, 0, 1065353216, 1065353216, 0, 0, 0, 1065353216)]
        [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 1065353216)]
        [InlineData(1, -2147483647, 1, -2147483647, 0, 0, 0, 0, 1065353216)]
        [InlineData(507307272, -1631787768, 520989638, -1623399160, 2140871, 1044051131, -1095043909, 1057765261, -1086655301)]
        [InlineData(423718298, -1715376742, 437326388, -1706988134, 2, 1044475290, -1094619750, 1058083380, -1086231142)]
        [InlineData(563319535, -1575775505, 576543846, -1567386897, 102728163, 1044051131, -1095043909, 1057765260, -1086655301)]
        [InlineData(1050253722, -1087163597, 1066192077, -1083808154, 1076258406, 1044284530, -1092729554, 1060021267, -1089543339)]
        [InlineData(1065353216, 1073741824, 1077936128, 1082130432, 1106247680, 1044051130, 1052439738, 1057765259, 1060828346)]
        [InlineData(2139095039, 1065353216, -1073741824, 1077936128, 2139095040, 0, 0, -2147483648, 0)]
        [InlineData(1594541859, -544553181, 1607477941, -536164573, 2139095040, 0, -2147483648, 0, -2147483648)]
        [InlineData(-4194304, 1065353216, 1073741824, 1077936128, -4194304, -4194304, -4194304, -4194304, -4194304)]
        [InlineData(1065353216, 1073741824, 1077936128, -4194304, -4194304, -4194304, -4194304, -4194304, -4194304)]
        [InlineData(2139095040, 1065353216, 1073741824, 1077936128, 2139095040, -4194304, 0, 0, 0)]
        [InlineData(1065353216, -8388608, 1073741824, 1077936128, 2139095040, 0, -4194304, 0, 0)]
        [InlineData(-2147483648, 0, -2147483648, 0, 0, 0, 0, 0, 1065353216)]
        [InlineData(0, 0, 1045178139, 1065016408, 1065353215, 0, 0, 1045178140, 1065016409)]
        [InlineData(0, 0, 1039034558, 1065239150, 1065353215, 0, 0, 1039034559, 1065239151)]
        [InlineData(0, 0, -1093882899, 1063954408, 1065353216, 0, 0, -1093882899, 1063954408)]
        [InlineData(1060439282, 0, 0, 1060439284, 1065353215, 1060439283, 0, 0, 1060439285)]
        public void DotAndAllNormalizeFormsRetainRecordedBits(int x, int y, int z, int w, int dot, int nx, int ny, int nz, int nw)
        {
            var q = Q(x, y, z, w);
            Assert.Equal(dot, BitConverter.SingleToInt32Bits(Quaternion.Dot(q, q)));
            Bits(Quaternion.Normalize(q), nx, ny, nz, nw);
            Bits(q.normalized, nx, ny, nz, nw);
            Bits(q, x, y, z, w);
            q.Normalize();
            Bits(q, nx, ny, nz, nw);
        }

        [Theory]
        [InlineData(1065353198, false, 1043063594)]
        [InlineData(1065353199, false, 1042746211)]
        [InlineData(1065353200, true, 0)]
        [InlineData(-1082130432, false, 0)]
        public void EqualityAndAngleHaveDistinctSignRulesAtTheThreshold(int w, bool equal, int angle)
        {
            var q = Q(0, 0, 0, w);
            Assert.Equal(equal, Quaternion.identity == q);
            Assert.Equal(!equal, Quaternion.identity != q);
            Assert.Equal(angle, BitConverter.SingleToInt32Bits(Quaternion.Angle(Quaternion.identity, q)));
            Bits(Quaternion.RotateTowards(Quaternion.identity, q, 0f), 0, 0, 0,
                angle == 0 ? w : 1065353216);
        }

        [Fact]
        public void CancellationDoesNotOverflowBeforeTheSingleRounding()
        {
            var a = new Quaternion(float.MaxValue, float.MaxValue, 1f, 0f);
            var b = new Quaternion(float.MaxValue, -float.MaxValue, 1f, 0f);
            Assert.Equal(1f, Quaternion.Dot(a, b));
            Assert.True(float.IsNaN(QuaternionMath.Dot(a, b)));
        }

        [Fact]
        public void EngineAndOtherReleasesKeepTheirArithmetic()
        {
            var q = new Quaternion(0f, 0f, .199367926f, .9799247f);
            Bits(Quaternion.Normalize(q), 0, 0, 1045178140, 1065016409);
            Bits(QuaternionMath.Normalize(q), 0, 0, 1045178139, 1065016408);
            ProjectInfo.UnityVersion = "6000.0.72f1";
            Assert.False(Compat.QuaternionApiPrecisionMeasured);
            Bits(Quaternion.Normalize(q), 0, 0, 1045178139, 1065016408);
        }

        [Fact]
        public void PublicAxisConversionRetainsItsOwnFloatArithmetic()
        {
            var q = new Quaternion(1f, 2f, 3f, 4f);
            q.ToAngleAxis(out float angle, out Vector3 axis);
            Assert.Equal(1118591706, BitConverter.SingleToInt32Bits(angle));
            Assert.Equal(1049155190, BitConverter.SingleToInt32Bits(axis.x));
            QuaternionMath.ToAngleAxis(q, out _, out var engineAxis);
            Assert.Equal(1049155191, BitConverter.SingleToInt32Bits(engineAxis.x));
            new Quaternion(9.999999e-6f, 0f, 0f, 0f).ToAngleAxis(out angle, out axis);
            Assert.Equal(0f, angle);
            Assert.Equal(Vector3.right, axis);
            new Quaternion(1e-5f, 0f, 0f, 0f).ToAngleAxis(out angle, out axis);
            Assert.Equal(180f, angle);
            Assert.Equal(Vector3.right, axis);
        }

        [Fact]
        public void ScriptAndEngineHotCallsDoNotAllocate()
        {
            var q = new Quaternion(.3f, -.7f, 1.1f, -.9f);
            _ = Quaternion.Normalize(q);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                q = Quaternion.Normalize(q);
                q = QuaternionMath.Normalize(q);
                _ = Quaternion.Dot(q, q);
                _ = QuaternionMath.Dot(q, q);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
