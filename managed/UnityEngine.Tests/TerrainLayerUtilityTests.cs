using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The terrain layer checks as Unity's own C# writes them.</summary>
    public class TerrainLayerUtilityTests
    {
        [Fact]
        public void ATextureThatIsNoAssetCountsAsANormalMap()
        {
            var texture = new Texture2D(4, 4);
            try
            {
                Assert.True(UnityEditor.TerrainLayerUtility.CheckNormalMapTextureType(texture));
                Assert.True(UnityEditor.TerrainLayerUtility.CheckNormalMapTextureType(null));
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
