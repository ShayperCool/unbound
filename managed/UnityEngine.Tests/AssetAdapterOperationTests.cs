using System;
using Unbound.Runtime;
using UnityEngine.Audio;
using Xunit;

namespace UnityEngine.Tests
{
    // The typed registry: reading and cloning resolve separately to the closest registered type.
    public class AssetAdapterOperationTests
    {
        static readonly Type[] s_Migrated =
        {
            typeof(UnityEngine.AI.NavMeshData), typeof(AnimationClip), typeof(AudioMixer), typeof(AudioMixerGroup), typeof(AudioMixerSnapshot),
            typeof(Avatar), typeof(AvatarMask), typeof(BillboardAsset), typeof(ComputeShader), typeof(Cubemap), typeof(CubemapArray),
            typeof(CustomRenderTexture), typeof(Flare), typeof(LightingSettings), typeof(Material), typeof(Mesh), typeof(RenderTexture),
            typeof(RuntimeAnimatorController), typeof(Shader), typeof(ShaderVariantCollection), typeof(Sprite), typeof(TerrainData),
            typeof(TerrainLayer), typeof(Texture2D), typeof(Texture2DArray), typeof(Texture3D), typeof(UnityEngine.U2D.SpriteAtlas),
        };

        [Fact]
        public void MigratedTypesDeclareNoEngineInterfaces()
        {
            foreach (var type in s_Migrated)
            {
                Assert.False(typeof(ISerializedAsset).IsAssignableFrom(type), type.Name);
                Assert.False(typeof(Instantiation.ICloneable).IsAssignableFrom(type), type.Name);
            }
        }

        [Fact]
        public void EachOperationResolvesToTheClosestRegistration()
        {
            // A derived type with its own slots keeps them; a reader-only type has no clone.
            Assert.Equal(typeof(CustomRenderTexture), AssetAdapters.Reader(typeof(CustomRenderTexture)).DeclaringType);
            Assert.Equal(typeof(RenderTexture), AssetAdapters.Reader(typeof(RenderTexture)).DeclaringType);
            Assert.Equal(typeof(RuntimeAnimatorController), AssetAdapters.Reader(typeof(AnimatorOverrideController)).DeclaringType);
            Assert.Null(AssetAdapters.Reader(typeof(Shader)));
            var texture = new CustomRenderTexture(4, 4) { name = "crt" };
            var array = new Texture2DArray(2, 2, 2, TextureFormat.RGBA32, false);
            var mesh = new Mesh { name = "mesh", vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
            try
            {
                Assert.True(AssetAdapters.TryClone(texture, out var crtCopy));
                Assert.IsType<CustomRenderTexture>(crtCopy);
                Assert.False(AssetAdapters.TryClone(array, out _));
                Assert.True(AssetAdapters.TryClone(mesh, out var meshCopy));
                var copy = Assert.IsType<Mesh>(meshCopy);
                copy.vertices = new[] { Vector3.one, Vector3.up, Vector3.right };
                Assert.Equal(Vector3.zero, mesh.vertices[0]);
                Object.DestroyImmediate(crtCopy);
                Object.DestroyImmediate(copy);
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(array);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
