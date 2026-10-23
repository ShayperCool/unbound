using Xunit;

namespace UnityEngine.Tests
{
    public class PropertyBlockTests
    {
        [Fact]
        public void APropertyBlockOverridesTheMaterialItDraws()
        {
            var go = new GameObject("block");
            var renderer = go.AddComponent<MeshRenderer>();
            var material = new Material { color = Color.red };
            renderer.sharedMaterial = material;
            Assert.Same(material, renderer.DrawnMaterial(0, material));

            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", Color.green);
            renderer.SetPropertyBlock(block);
            var drawn = renderer.DrawnMaterial(0, material);
            Assert.NotSame(material, drawn);
            Assert.Equal(Color.green, drawn.color);
            Assert.Equal(Color.red, material.color);
            // Reused until the material or the block changes.
            Assert.Same(drawn, renderer.DrawnMaterial(0, material));
            material.SetFloat("_Other", 2f);
            var again = renderer.DrawnMaterial(0, material);
            Assert.NotSame(drawn, again);
            Assert.Equal(2f, again.GetFloat("_Other"));
            Assert.Equal(Color.green, again.color);
            Object.DestroyImmediate(go);
        }
    }
}
