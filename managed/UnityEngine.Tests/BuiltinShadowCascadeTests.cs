using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public class BuiltinShadowCascadeTests
    {
        [Theory]
        [InlineData(0,1)]
        [InlineData(1,1)]
        [InlineData(2,2)]
        [InlineData(4,4)]
        public void OrthographicUsesOneMapAndPerspectiveUsesTheQualityCount(int quality,int perspective)
        {
            string version=ProjectInfo.UnityVersion;int previous=QualitySettings.shadowCascades;
            try
            {
                ProjectInfo.UnityVersion="6000.0.71f1";QualitySettings.shadowCascades=quality;
                Assert.Equal(1,FrameBuilder.BuiltinShadowCascades(true));
                Assert.Equal(perspective,FrameBuilder.BuiltinShadowCascades(false));
                ProjectInfo.UnityVersion="2022.3.1f1";
                Assert.Equal(perspective,FrameBuilder.BuiltinShadowCascades(true));
            }
            finally {QualitySettings.shadowCascades=previous;ProjectInfo.UnityVersion=version;}
        }
    }
}
