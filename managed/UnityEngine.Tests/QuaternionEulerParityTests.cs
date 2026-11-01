using Xunit;
namespace UnityEngine.Tests
{
    public class QuaternionEulerParityTests
    {
        [Theory]
        [InlineData(23f,35f,7f,.207775027f,.282511622f,-.002785083f,.9364875f)]
        [InlineData(17f,-29f,13f,.114148952f,-.262237549f,.145164222f,.9471689f)]
        [InlineData(1.3f,2.7f,9.1f,.0131743588f,.0225843135f,.07903549f,.9965288f)]
        [InlineData(37.2f,-68.3f,119.7f,-.327483833f,-.495476663f,.7681654f,.239121526f)]
        public void EulerOverloadsRetainMeasuredComponents(float x,float y,float z,float qx,float qy,float qz,float qw)
        {
            var vector=Quaternion.Euler(new Vector3(x,y,z));
            var components=Quaternion.Euler(x,y,z);
            Assert.Equal(qx,vector.x); Assert.Equal(qy,vector.y);
            Assert.Equal(qz,vector.z); Assert.Equal(qw,vector.w);
            Assert.Equal(qx,components.x); Assert.Equal(qy,components.y);
            Assert.Equal(qz,components.z); Assert.Equal(qw,components.w);
        }
    }
}
