using System;
using Xunit;
using static UnityEngine.Tests.AllocationMeasurement;

namespace UnityEngine.Tests
{
    public class PropertyBlockTests
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static (GameObject, MeshRenderer, Material) Make(Color color, int materials = 1)
        {
            var go = new GameObject("block");
            var renderer = go.AddComponent<MeshRenderer>();
            var material = new Material { color = color };
            var shared = new Material[materials];
            for (int i = 0; i < materials; i++) shared[i] = i == 0 ? material : new Material { color = color };
            renderer.sharedMaterials = shared;
            return (go, renderer, material);
        }

        static MaterialPropertyBlock Block(Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            return block;
        }

        [Fact]
        public void APropertyBlockOverridesTheMaterialItDraws()
        {
            var (go, renderer, material) = Make(Color.red);
            Assert.Same(material, renderer.DrawnMaterial(0, material));

            renderer.SetPropertyBlock(Block(Color.green));
            var drawn = renderer.DrawnMaterial(0, material);
            Assert.NotSame(material, drawn);
            Assert.Equal(Color.green, drawn.color);
            Assert.Equal(Color.red, material.color);
            // Reused until the material or the block changes; made again in place.
            Assert.Same(drawn, renderer.DrawnMaterial(0, material));
            int version = drawn.m_Version;
            material.SetFloat("_Other", 2f);
            var again = renderer.DrawnMaterial(0, material);
            Assert.Same(drawn, again);
            Assert.NotEqual(version, again.m_Version);
            Assert.Equal(2f, again.GetFloat("_Other"));
            Assert.Equal(Color.green, again.color);
            Object.DestroyImmediate(go);
        }

        /// <summary>Unity copies the block when it is set: an edit draws only once it is set again
        /// (tests/projects/propertyblocks `edited`, `setAgain`).</summary>
        [Fact]
        public void AnEditedBlockDrawsOnlyOnceSetAgain()
        {
            var (go, renderer, material) = Make(Color.red);
            var block = Block(Color.green);
            renderer.SetPropertyBlock(block);
            var drawn = renderer.DrawnMaterial(0, material);
            Assert.Equal(Color.green, drawn.color);

            block.SetColor("_Color", Color.blue);
            Assert.Equal(Color.green, renderer.DrawnMaterial(0, material).color);
            var read = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(read);
            Assert.Equal(Color.green, read.GetColor("_Color"));

            renderer.SetPropertyBlock(block);
            Assert.Same(drawn, renderer.DrawnMaterial(0, material));
            Assert.Equal(Color.blue, drawn.color);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void ABlockRefilledAndSetEveryFrameDrawsItsLatestValues()
        {
            var (go, renderer, material) = Make(Color.red);
            var block = new MaterialPropertyBlock();
            foreach (var color in new[] { Color.green, Color.blue, Color.green, Color.yellow })
            {
                block.Clear();
                block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block);
                Assert.Equal(color, renderer.DrawnMaterial(0, material).color);
            }
            Object.DestroyImmediate(go);
        }

        /// <summary>A block set again without _Color: the material's colour again (the copy is made
        /// again from the material, not only overwritten by the block).</summary>
        [Fact]
        public void AValueTheBlockNoLongerHasDrawsTheMaterials()
        {
            var (go, renderer, material) = Make(Color.red);
            var texture = new Texture2D(2, 2);
            var block = Block(Color.green);
            block.SetTexture("_Extra", texture);
            renderer.SetPropertyBlock(block);
            var drawn = renderer.DrawnMaterial(0, material);
            Assert.Equal(Color.green, drawn.color);
            Assert.True(drawn.m_Textures.ContainsKey(Shader.PropertyToID("_Extra")));

            block.Clear();
            block.SetFloat("_Other", 1f);
            renderer.SetPropertyBlock(block);
            Assert.Same(drawn, renderer.DrawnMaterial(0, material));
            Assert.Equal(Color.red, drawn.color);
            Assert.False(drawn.m_Vectors.ContainsKey(ColorId) && drawn.m_RawBindings.Contains(ColorId));
            Assert.False(drawn.m_Textures.ContainsKey(Shader.PropertyToID("_Extra")));
            Assert.Equal(1f, drawn.GetFloat("_Other"));
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(texture);
        }

        [Fact]
        public void ASlotsBlockIsCopiedToo()
        {
            var (go, renderer, material) = Make(Color.red, 2);
            var slot = renderer.sharedMaterials[1];
            var block = Block(Color.green);
            renderer.SetPropertyBlock(block, 1);
            Assert.Equal(Color.green, renderer.DrawnMaterial(1, slot).color);
            Assert.Same(material, renderer.DrawnMaterial(0, material));

            block.SetColor("_Color", Color.blue);
            Assert.Equal(Color.green, renderer.DrawnMaterial(1, slot).color);
            renderer.SetPropertyBlock(block, 1);
            Assert.Equal(Color.blue, renderer.DrawnMaterial(1, slot).color);
            Object.DestroyImmediate(go);
        }

        /// <summary>A block kept by reference (a terrain's own, a tree's) and edited where it is
        /// kept draws its new values.</summary>
        [Fact]
        public void AnInternalBlockEditedInPlaceDrawsItsNewValues()
        {
            var (go, renderer, material) = Make(Color.red);
            var block = Block(Color.green);
            renderer.m_PropertyBlock = block;
            var drawn = renderer.DrawnMaterial(0, material);
            Assert.Equal(Color.green, drawn.color);
            block.SetColor("_Color", Color.blue);
            Assert.Equal(Color.blue, renderer.DrawnMaterial(0, material).color);
            block.SetVectorArray("_Values", new[] { Vector4.one });
            int version = drawn.m_Version;
            block.SetVectorArray("_Values", new[] { Vector4.zero });
            renderer.DrawnMaterial(0, material);
            Assert.NotEqual(version, drawn.m_Version);
            Assert.Equal(Vector4.zero, drawn.GetVectorArray("_Values")[0]);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void SettingTheSameValuesAgainMakesNothingAgain()
        {
            var (go, renderer, material) = Make(Color.red);
            var block = Block(Color.green);
            block.SetVectorArray("_Values", new[] { Vector4.one, Vector4.zero });
            renderer.SetPropertyBlock(block);
            var drawn = renderer.DrawnMaterial(0, material);
            int version = drawn.m_Version;
            renderer.SetPropertyBlock(Block(Color.green).With(b => b.SetVectorArray("_Values", new[] { Vector4.one, Vector4.zero })));
            Assert.Same(drawn, renderer.DrawnMaterial(0, material));
            Assert.Equal(version, drawn.m_Version);

            // 0 and -0 are different values.
            block.SetFloat("_Zero", 0f);
            renderer.SetPropertyBlock(block);
            renderer.DrawnMaterial(0, material);
            version = drawn.m_Version;
            block.SetFloat("_Zero", -0f);
            renderer.SetPropertyBlock(block);
            renderer.DrawnMaterial(0, material);
            Assert.NotEqual(version, drawn.m_Version);
            Object.DestroyImmediate(go);
        }

        /// <summary>Per draw nothing is allocated, also when a block is set again every frame with
        /// the same values.</summary>
        [Fact]
        public void DrawingUnderAnUnchangedBlockAllocatesNothing()
        {
            var (go, renderer, material) = Make(Color.red);
            var block = Block(Color.green);
            block.SetVectorArray("_Values", new[] { Vector4.one, Vector4.zero });
            block.SetMatrix("_Matrix", Matrix4x4.identity);
            renderer.SetPropertyBlock(block);
            renderer.DrawnMaterial(0, material);
            renderer.SetPropertyBlock(block);
            EmptyAllocationContext();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                renderer.SetPropertyBlock(block);
                renderer.DrawnMaterial(0, material);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Object.DestroyImmediate(go);
        }

        /// <summary>A block whose values change every frame makes the drawn copy again in place,
        /// without a new material.</summary>
        [Fact]
        public void AChangingBlockKeepsOneDrawnMaterial()
        {
            var (go, renderer, material) = Make(Color.red);
            var block = Block(Color.green);
            renderer.SetPropertyBlock(block);
            var drawn = renderer.DrawnMaterial(0, material);
            for (int i = 0; i < 10; i++)
            {
                block.SetVector("_Color", new Vector4(i / 10f, 0f, 0f, 1f));
                renderer.SetPropertyBlock(block);
                Assert.Same(drawn, renderer.DrawnMaterial(0, material));
                Assert.Equal(i / 10f, drawn.m_Vectors[ColorId].x);
            }
            Object.DestroyImmediate(go);
        }

        /// <summary>The drawn copy made again in place takes the material's arrays again, filled
        /// into the arrays it has, and stays independent of the material.</summary>
        [Fact]
        public void TheDrawnCopyFollowsTheMaterialsArrays()
        {
            var (go, renderer, material) = Make(Color.red);
            material.SetFloatArray("_Weights", new[] { 1f, 2f });
            renderer.SetPropertyBlock(Block(Color.green));
            var drawn = renderer.DrawnMaterial(0, material);
            Assert.Equal(new[] { 1f, 2f }, drawn.GetFloatArray("_Weights"));
            material.SetFloatArray("_Weights", new[] { 3f, 4f });
            Assert.Same(drawn, renderer.DrawnMaterial(0, material));
            Assert.Equal(new[] { 3f, 4f }, drawn.GetFloatArray("_Weights"));
            drawn.SetFloatArray("_Weights", new[] { 5f, 6f });
            Assert.Equal(new[] { 3f, 4f }, material.GetFloatArray("_Weights"));
            Object.DestroyImmediate(go);
        }

        /// <summary>Terrain.SetSplatMaterialPropertyBlock copies the block as SetPropertyBlock does
        /// (tests/projects/propertyblocks `edited.get.terrain`).</summary>
        [Fact]
        public void ATerrainsSplatBlockIsCopied()
        {
            var go = new GameObject("terrain");
            var terrain = go.AddComponent<Terrain>();
            var block = Block(Color.green);
            terrain.SetSplatMaterialPropertyBlock(block);
            block.SetColor("_Color", Color.blue);
            var read = new MaterialPropertyBlock();
            terrain.GetSplatMaterialPropertyBlock(read);
            Assert.Equal(Color.green, read.GetColor("_Color"));
            int version = terrain.SplatProperties.m_Version;
            terrain.SetSplatMaterialPropertyBlock(block);
            terrain.GetSplatMaterialPropertyBlock(read);
            Assert.Equal(Color.blue, read.GetColor("_Color"));
            Assert.NotEqual(version, terrain.SplatProperties.m_Version);
            terrain.SetSplatMaterialPropertyBlock(null);
            terrain.GetSplatMaterialPropertyBlock(read);
            Assert.True(read.isEmpty);
            Object.DestroyImmediate(go);
        }
    }

    public class TerrainTemplateTests
    {
        /// <summary>A terrain's material template edited in place is drawn (tests/projects/propertyblocks
        /// `materialChanged`), by the same pass materials taking its values again, without
        /// allocating.</summary>
        [Fact]
        public void AnEditedTemplateRefreshesThePassesInPlace()
        {
            var go = new GameObject("terrain");
            var data = new TerrainData { heightmapResolution = 33 };
            data.terrainLayers = new[] { new TerrainLayer { diffuseTexture = new Texture2D(4, 4) } };
            // A hole: every material cuts it out with the holes map, made once.
            data.SetHoles(3, 3, new bool[,] { { false } });
            var terrain = go.AddComponent<Terrain>();
            terrain.terrainData = data;
            var template = new Material { color = Color.red };
            terrain.materialTemplate = template;
            var renderer = TerrainRenderer.For(terrain);
            renderer.UpdateMaterials(terrain, data);
            var pass = renderer.PassesForTest[0];
            Assert.Equal(Color.red, pass.color);

            template.color = Color.green;
            renderer.UpdateMaterials(terrain, data);
            Assert.Same(pass, renderer.PassesForTest[0]);
            Assert.Equal(Color.green, pass.color);
            // The splat values the terrain sets are set again over the template's.
            Assert.Same(data.terrainLayers[0].diffuseTexture, pass.GetTexture("_Splat0"));
            var holes = pass.GetTexture("_TerrainHolesTexture");
            Assert.NotNull(holes);
            Assert.True(pass.IsKeywordEnabled("_ALPHATEST_ON"));

            int version = pass.m_Version;
            renderer.UpdateMaterials(terrain, data);
            Assert.Equal(version, pass.m_Version);

            EmptyAllocationContext();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
            {
                template.SetColor("_Color", new Color(i / 20f, 0f, 0f));
                renderer.UpdateMaterials(terrain, data);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Same(pass, renderer.PassesForTest[0]);
            Assert.Same(holes, pass.GetTexture("_TerrainHolesTexture"));

            // New holes: the map is made again.
            var pixels = ((Texture2D)holes).GetPixels32();
            data.SetHoles(4, 4, new bool[,] { { false } });
            template.color = Color.white;
            renderer.UpdateMaterials(terrain, data);
            Assert.NotEqual(pixels, ((Texture2D)renderer.PassesForTest[0].GetTexture("_TerrainHolesTexture")).GetPixels32());

            // Another template makes the passes again.
            terrain.materialTemplate = new Material { color = Color.blue };
            renderer.UpdateMaterials(terrain, data);
            Assert.NotSame(pass, renderer.PassesForTest[0]);
            Assert.Equal(Color.blue, renderer.PassesForTest[0].color);
            Object.DestroyImmediate(go);
        }
    }

    static class BlockTestExtensions
    {
        internal static MaterialPropertyBlock With(this MaterialPropertyBlock block, Action<MaterialPropertyBlock> fill)
        {
            fill(block);
            return block;
        }
    }
}
