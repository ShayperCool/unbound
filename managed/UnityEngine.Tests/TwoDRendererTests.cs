using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Sprite skinning data and sprite shape rendering as measured in the Unity Editor
    /// (`tests/projects/twod`: skin.*, spriteshape.*).</summary>
    public class TwoDRendererTests : IDisposable
    {
        readonly List<Object> m_Made = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Made) if (o) Object.DestroyImmediate(o);
        }

        /// <summary>The probe's limb: 32x128 pixels, pivot at the bottom centre, 32 pixels per unit,
        /// a mesh of five rows and two bones.</summary>
        Sprite Limb()
        {
            var texture = new Texture2D(32, 128, TextureFormat.RGBA32, false);
            m_Made.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 128), new Vector2(0.5f, 0f), 32f, 0, SpriteMeshType.FullRect);
            m_Made.Add(sprite);
            var vertices = new Vector2[10];
            var weights = new BoneWeight[10];
            for (int r = 0; r < 5; r++)
            for (int c = 0; c < 2; c++)
            {
                vertices[r * 2 + c] = new Vector2(c * 32f, r * 32f);
                weights[r * 2 + c] = r < 2 ? new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                    : r == 2 ? new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f }
                    : new BoneWeight { boneIndex0 = 1, weight0 = 1f };
            }
            var triangles = new List<ushort>();
            for (ushort a = 0; a < 8; a += 2) triangles.AddRange(new[] { a, (ushort)(a + 1), (ushort)(a + 3), a, (ushort)(a + 3), (ushort)(a + 2) });
            var bones = new[]
            {
                new SpriteBone { name = "upper", position = new Vector3(16f, 8f, 0f), rotation = Quaternion.Euler(0f, 0f, 90f), length = 56f, parentId = -1 },
                new SpriteBone { name = "lower", position = new Vector3(56f, 0f, 0f), rotation = Quaternion.identity, length = 56f, parentId = 0 },
            };
            sprite.SetImportedMesh(vertices, triangles.ToArray(), weights, bones);
            return sprite;
        }

        static void Near(float expected, float actual) => Assert.True(Mathf.Abs(expected - actual) < 1e-5f, $"{expected} vs {actual}");

        [Fact]
        public void SkinnedSpritesGiveBonesInUnitsAndTheInverseBonePosesAsBindPoses()
        {
            var sprite = Limb();
            var bones = sprite.GetBones();
            Assert.Equal(new Vector3(0f, 0.25f, 0f), bones[0].position);
            Assert.Equal(new Vector3(1.75f, 0f, 0f), bones[1].position);
            Assert.Equal(1.75f, bones[1].length);
            var poses = sprite.GetBindPoses();
            Assert.Equal(2, poses.Length);
            // Measured: (0 -1 0 0 | 1 0 0 0 | 0 0 1 0 | -0.25 0 0 1) and the same with -2, by columns.
            Near(0f, poses[0].m00);
            Near(1f, poses[0].m01);
            Near(-1f, poses[0].m10);
            Near(-0.25f, poses[0].m03);
            Near(0f, poses[0].m13);
            Near(-2f, poses[1].m03);
            Near(0f, poses[1].m13);
        }

        [Fact]
        public void SkinnedSpritesHaveTangentsAndWeightsPlainOnesOnlyPositionsAndUvs()
        {
            var sprite = Limb();
            Assert.Equal(10, sprite.GetVertexCount());
            Assert.True(sprite.HasVertexAttribute(VertexAttribute.Tangent));
            Assert.True(sprite.HasVertexAttribute(VertexAttribute.BlendWeight));
            Assert.True(sprite.HasVertexAttribute(VertexAttribute.BlendIndices));
            Assert.False(sprite.HasVertexAttribute(VertexAttribute.Normal));
            Assert.False(sprite.HasVertexAttribute(VertexAttribute.Color));
            Assert.Equal(new Vector4(1f, 0f, 0f, -1f), sprite.GetVertexAttribute<Vector4>(VertexAttribute.Tangent)[3]);
            Assert.Equal(new Vector3(0.5f, 4f, 0f), sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position)[9]);
            Assert.Equal(new Vector2(1f, 0.25f), sprite.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0)[3]);
            var w = sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight)[4];
            Assert.Equal((0, 0.5f, 1, 0.5f), (w.boneIndex0, w.weight0, w.boneIndex1, w.weight1));
            Assert.Equal(new ushort[] { 0, 1, 3, 0, 3, 2 }, sprite.GetIndices().GetSubArray(0, 6).ToArray());
            Assert.Equal(new Vector3(1f, 4f, 0.2f), sprite.bounds.size);

            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            m_Made.Add(texture);
            var plain = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect);
            m_Made.Add(plain);
            Assert.Equal(4, plain.GetVertexCount());
            Assert.True(plain.HasVertexAttribute(VertexAttribute.Position));
            Assert.True(plain.HasVertexAttribute(VertexAttribute.TexCoord0));
            Assert.False(plain.HasVertexAttribute(VertexAttribute.Tangent));
            Assert.False(plain.HasVertexAttribute(VertexAttribute.BlendWeight));
            Assert.Empty(plain.GetBones());
            Assert.Equal(0, plain.GetBindPoses().Length);
        }

        [Fact]
        public unsafe void DeformableBuffersReplaceTheSpriteVerticesAndBoundsUntilDeactivated()
        {
            var go = new GameObject("Skinned");
            m_Made.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Limb();
            Assert.True(renderer.IsUsingDeformableBuffer(IntPtr.Zero));
            using var buffer = new NativeArray<byte>(10 * 28, Allocator.Persistent);
            renderer.SetDeformableBuffer(buffer);
            renderer.SetLocalAABB(new Bounds(new Vector3(0.2f, 1.9f, 0f), new Vector3(1.8f, 4f, 0f)));
            Assert.False(renderer.IsUsingDeformableBuffer(IntPtr.Zero));
            Assert.True(renderer.IsUsingDeformableBuffer((IntPtr)buffer.GetUnsafeReadOnlyPtr()));
            Assert.Equal(new Vector3(1.8f, 4f, 0f), renderer.localBounds.size);
            renderer.DeactivateDeformableBuffer();
            Assert.True(renderer.IsUsingDeformableBuffer(IntPtr.Zero));
            Assert.Equal(new Vector3(1f, 4f, 0.2f), renderer.localBounds.size);
        }

        [Fact]
        public void SpriteShapeEdgesDrawBySpriteAfterTheFillAndTakeTheGeneratedBounds()
        {
            var go = new GameObject("Shape");
            m_Made.Add(go);
            var renderer = go.AddComponent<SpriteShapeRenderer>();
            Assert.Equal(0, renderer.GetSplineMeshCount());
            var segments = renderer.GetSegments(8);
            renderer.GetChannels(32, out var indices, out var positions, out var uvs);
            var bounds = renderer.GetBounds();
            // A fill triangle, then edges of sprites 1, 2, 0, 0 (a quad each), as the generator writes them.
            int[] sprites = { -1, 1, 2, 0, 0 };
            int vertex = 0, index = 0;
            for (int s = 0; s < sprites.Length; s++)
            {
                int count = s == 0 ? 3 : 4;
                for (int v = 0; v < count; v++) positions[vertex + v] = new Vector3(s + v, v, 0f);
                int[] tris = s == 0 ? new[] { 0, 1, 2 } : new[] { 0, 3, 1, 0, 2, 3 };
                foreach (var t in tris) indices[index++] = (ushort)t;
                vertex += count;
                segments[s] = new SpriteShapeSegment { geomIndex = s, vertexCount = count, indexCount = tris.Length, spriteIndex = sprites[s] };
            }
            bounds[0] = new Bounds(new Vector3(2f, 1f, 0f), new Vector3(8f, 3f, 0f));
            renderer.SetLocalAABB(new Bounds(Vector3.zero, new Vector3(12f, 7f, 0f)));
            renderer.Prepare(default, new SpriteShapeParameters(), new Sprite[3]);
            Assert.Equal(new Vector3(12f, 7f, 0f), renderer.localBounds.size);
            renderer.Build();
            Assert.Equal(new[] { -1, 0, 0, 1, 2 }, renderer.DrawnSprites());
            Assert.Equal(new Vector3(8f, 3f, 0f), renderer.localBounds.size);
            Assert.Equal(2, renderer.GetSplineMeshCount());
        }
    }
}
