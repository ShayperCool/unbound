using Xunit;

namespace UnityEngine.Tests
{
    public class QuaternionVectorParityTests
    {
        [Theory]
        [InlineData(0f, 0f, 0f, 0f, 0.3f, -0.7f, 1.1f, 0.3f, -0.7f, 1.1f)]
        [InlineData(0.23f, -0.17f, 0.31f, 0.9f, -2.3f, 4.7f, 0.011f, -4.716937f, 2.19014645f, 0.427840382f)]
        [InlineData(0.3f, -0.7f, 1.1f, -0.9f, 1000.125f, -0.0003125f, 17.25f, -2367.18042f, -2417.54956f, -602.8343f)]
        [InlineData(1f, 2f, 3f, 4f, 0.3f, -0.7f, 1.1f, 30.7f, 26.1f, -26.9f)]
        [InlineData(-1.3f, 0.4f, 0.27f, -2.1f, -2.3f, 4.7f, 0.011f, -0.813062f, -6.92874241f, 24.398098f)]
        [InlineData(-0.38839376f, -0.795187056f, -0.08882126f, -0.4570982f, 0.3f, -0.7f, 1.1f, 0.415876746f, -0.5032881f, -1.16779637f)]
        [InlineData(1e-06f, -2e-06f, 3e-06f, -4e-06f, 1e-06f, -2e-07f, 3e-08f, 1e-06f, -2e-07f, 3e-08f)]
        [InlineData(17f, -29f, 13f, -41f, 1000.125f, -0.0003125f, 17.25f, -1970607.38f, -2041216.25f, -1975209.13f)]
        public void PublicInputsRetainMeasuredRotation(float qx, float qy, float qz, float qw,
            float px, float py, float pz, float x, float y, float z)
        {
            var result = new Quaternion(qx, qy, qz, qw) * new Vector3(px, py, pz);
            Assert.Equal(x, result.x);
            Assert.Equal(y, result.y);
            Assert.Equal(z, result.z);
        }
    }
}
