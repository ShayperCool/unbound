using Xunit;

namespace UnityEngine.Tests
{
    public class SkinnedMeshTests
    {
        [Fact]
        public void VerticesFollowTheirBones()
        {
            var root = new GameObject("root");
            var boneA = new GameObject("a").transform;
            var boneB = new GameObject("b").transform;
            boneA.SetParent(root.transform, false);
            boneB.SetParent(root.transform, false);
            boneB.localPosition = new Vector3(0f, 1f, 0f);

            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0.5f, 0f) },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
                },
                // Mesh space to each bone's space at bind time.
                bindposes = new[] { boneA.worldToLocalMatrix, boneB.worldToLocalMatrix },
            };
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { boneA, boneB };

            // Bone b moves 2 to the right: its vertex follows, the shared one half as much.
            boneB.localPosition += new Vector3(2f, 0f, 0f);
            Time.s_FrameCount++;
            var baked = new Mesh();
            renderer.BakeMesh(baked);
            var v = baked.vertices;
            Assert.Equal(new Vector3(0f, 0f, 0f), v[0]);
            Assert.Equal(new Vector3(2f, 1f, 0f), v[1]);
            Assert.Equal(new Vector3(1f, 0.5f, 0f), v[2]);

            // One bone per vertex: the shared vertex takes its heaviest (first) bone only.
            renderer.quality = SkinQuality.Bone1;
            Time.s_FrameCount++;
            renderer.BakeMesh(baked);
            Assert.Equal(new Vector3(0f, 0.5f, 0f), baked.vertices[2]);
            Object.DestroyImmediate(root);
        }

        /// <summary>The mesh a skinned renderer draws keeps its arrays from frame to frame (a
        /// frame's skinning allocates none) and takes each frame's pose, normals and tangents
        /// included; the shared mesh keeps its own.</summary>
        [Fact]
        public void TheDrawnMeshFollowsEachFramesPoseInTheSameArrays()
        {
            var root = new GameObject("root");
            var bone = new GameObject("bone").transform;
            bone.SetParent(root.transform, false);
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, -1f) },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                },
                bindposes = new[] { bone.worldToLocalMatrix },
            };
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            try
            {
                Time.s_FrameCount++;
                var drawn = renderer.DrawGeometry(out _);
                var (positions, normals, tangents) = (drawn.m_Vertices, drawn.m_Normals, drawn.m_Tangents);
                Assert.Equal(new Vector3(1f, 0f, 0f), positions[1]);

                // A quarter turn about z and a move: positions, normals and tangents follow.
                bone.localPosition = new Vector3(0f, 0f, 2f);
                bone.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Time.s_FrameCount++;
                Assert.Same(drawn, renderer.DrawGeometry(out _));
                Assert.Same(positions, drawn.m_Vertices);
                Assert.Same(normals, drawn.m_Normals);
                Assert.Same(tangents, drawn.m_Tangents);
                Assert.True((drawn.m_Vertices[1] - new Vector3(0f, 1f, 2f)).sqrMagnitude < 1e-10f);
                Assert.True((drawn.m_Tangents[2] - new Vector4(0f, 1f, 0f, -1f)).sqrMagnitude < 1e-10f);
                Assert.Equal(Vector3.forward, drawn.m_Normals[0]);
                Assert.True((drawn.bounds.center - new Vector3(-0.5f, 0.5f, 2f)).sqrMagnitude < 1e-10f);
                // The shared mesh is untouched.
                Assert.Equal(new Vector3(1f, 0f, 0f), mesh.vertices[1]);
                Assert.Equal(new Vector4(1f, 0f, 0f, -1f), mesh.tangents[2]);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>A renderer skins once a skinning pass (before a frame's UpdateAllSkinnedMeshes
        /// the last frame's goes on, so a bone moved since is drawn from the next pass); its previous
        /// buffer is the current one unless a camera drew its motion vectors in the last pass, then
        /// the last pass's skin (tests/projects/renderapi prevskin.*).</summary>
        [Fact]
        public void SkinBuffersAlternateOnlyAfterMotionVectorsWereDrawn()
        {
            var root = new GameObject("root");
            var bone = new GameObject("bone").transform;
            bone.SetParent(root.transform, false);
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[] { new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f } },
                bindposes = new[] { Matrix4x4.identity },
            };
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            renderer.updateWhenOffscreen = true;
            try
            {
                Assert.Null(renderer.GetVertexBuffer());
                Assert.Null(renderer.GetPreviousVertexBuffer());
                NextSkinningFrame();
                float Current() => renderer.m_Skinned.vertices[0].x;
                using (var current = renderer.GetVertexBuffer())
                using (var previous = renderer.GetPreviousVertexBuffer())
                {
                    Assert.Equal(current.Handle, previous.Handle);
                    Assert.Equal(0f, Current());
                }
                // Moved within the pass: its skin waits for the next one.
                bone.localPosition = new Vector3(1f, 0f, 0f);
                renderer.DrawGeometry(out _);
                using (renderer.GetVertexBuffer()) Assert.Equal(0f, Current());
                // No motion vectors drawn: the previous buffer is the current one.
                NextSkinningFrame();
                using (var current = renderer.GetVertexBuffer())
                using (var previous = renderer.GetPreviousVertexBuffer())
                {
                    Assert.Equal(1f, Current());
                    Assert.Equal(current.Handle, previous.Handle);
                }
                // Motion vectors drawn in this pass: the next pass keeps this skin as the previous.
                renderer.m_MotionEpoch = SkinnedMeshRenderer.SkinPass;
                bone.localPosition = new Vector3(2f, 0f, 0f);
                NextSkinningFrame();
                using (var current = renderer.GetVertexBuffer())
                using (var previous = renderer.GetPreviousVertexBuffer())
                {
                    Assert.Equal(2f, Current());
                    Assert.NotEqual(current.Handle, previous.Handle);
                    Assert.Equal(1f, renderer.m_SkinnedPrevious.vertices[0].x);
                }
                // Then the two buffers swap again: the last pass's skin is the previous one.
                renderer.m_MotionEpoch = SkinnedMeshRenderer.SkinPass;
                bone.localPosition = new Vector3(3f, 0f, 0f);
                NextSkinningFrame();
                using (var current = renderer.GetVertexBuffer())
                using (var previous = renderer.GetPreviousVertexBuffer())
                {
                    Assert.Equal(3f, Current());
                    Assert.Equal(2f, renderer.m_SkinnedPrevious.vertices[0].x);
                    Assert.NotEqual(current.Handle, previous.Handle);
                }
                // Skinning now (BakeMesh) between a read and the next pass leaves the pass's skin.
                var baked = new Mesh();
                renderer.m_MotionEpoch = SkinnedMeshRenderer.SkinPass;
                bone.localPosition = new Vector3(3.5f, 0f, 0f);
                renderer.BakeMesh(baked);
                Assert.Equal(3.5f, baked.vertices[0].x);
                Assert.Equal(3f, Current());
                Object.DestroyImmediate(baked);
                NextSkinningFrame();
                using (var current = renderer.GetVertexBuffer())
                using (var previous = renderer.GetPreviousVertexBuffer())
                {
                    Assert.Equal(3.5f, Current());
                    Assert.Equal(3f, renderer.m_SkinnedPrevious.vertices[0].x);
                    Assert.NotEqual(current.Handle, previous.Handle);
                }
                // Without skinnedMotionVectors the buffers do not alternate.
                renderer.skinnedMotionVectors = false;
                renderer.m_MotionEpoch = SkinnedMeshRenderer.SkinPass;
                bone.localPosition = new Vector3(4f, 0f, 0f);
                NextSkinningFrame();
                using (var current = renderer.GetVertexBuffer())
                using (var previous = renderer.GetPreviousVertexBuffer())
                {
                    Assert.Equal(4f, Current());
                    Assert.Equal(current.Handle, previous.Handle);
                }
                // A disabled renderer keeps its last skin.
                renderer.enabled = false;
                bone.localPosition = new Vector3(5f, 0f, 0f);
                NextSkinningFrame();
                using (renderer.GetVertexBuffer()) Assert.Equal(4f, Current());
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>The skin buffer holds the vertices in the root bone's space, not the
        /// renderer's, when the two differ (tests/projects/vfx <c>skinned/*/buffer</c>: a root
        /// bone turned and moved from the renderer); drawn and baked, the skin is where it was.
        /// The root bone an effect applies: its world transform, of this frame and of the frame
        /// before.</summary>
        [Fact]
        public void TheSkinBufferIsInTheRootBonesSpace()
        {
            var root = new GameObject("root");
            root.transform.SetPositionAndRotation(new Vector3(0.4f, 0.1f, 0f), Quaternion.Euler(0f, 0f, 15f));
            var lower = new GameObject("lower").transform;
            lower.SetParent(root.transform, false);
            lower.localPosition = new Vector3(0f, -1f, 0f);
            lower.localRotation = Quaternion.Euler(0f, 0f, -10f);
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-0.3f, -1f, 0f), new Vector3(0.3f, -1f, 0f), new Vector3(-0.3f, -0.5f, 0f) },
                triangles = new[] { 0, 2, 1 },
                boneWeights = new[] { new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f } },
                bindposes = new[] { lower.worldToLocalMatrix * root.transform.localToWorldMatrix },
            };
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { lower };
            renderer.rootBone = lower;
            renderer.updateWhenOffscreen = true;
            var baked = new Mesh();
            try
            {
                Assert.Null(renderer.GetVertexBuffer());
                NextSkinningFrame();
                using (renderer.GetVertexBuffer())
                {
                    // (-0.3, -1) of the renderer is (-0.3, 0) from the root bone, turned by 10 degrees.
                    var inRoot = renderer.m_Skinned.vertices[0];
                    var expected = Quaternion.Euler(0f, 0f, 10f) * new Vector3(-0.3f, 0f, 0f);
                    Assert.True((inRoot - expected).sqrMagnitude < 1e-10f, inRoot.ToString("F6"));
                }
                var drawn = renderer.DrawGeometry(out var model);
                var world = root.transform.TransformPoint(new Vector3(-0.3f, -1f, 0f));
                Assert.True((model.MultiplyPoint3x4(drawn.vertices[0]) - world).sqrMagnitude < 1e-10f);
                renderer.BakeMesh(baked);
                Assert.True((baked.vertices[0] - new Vector3(-0.3f, -1f, 0f)).sqrMagnitude < 1e-10f, baked.vertices[0].ToString("F6"));
                // The root bone as an effect applies it: its world transform (tests/projects/vfx
                // `skinned operations`), not the one relative to the renderer.
                var before = lower.localToWorldMatrix;
                Assert.Equal(before, renderer.EffectRootTransform(false));
                Assert.NotEqual(root.transform.worldToLocalMatrix * before, renderer.EffectRootTransform(false));
                // Asked for the first time: the frame before's is this frame's.
                Assert.Equal(before, renderer.EffectRootTransform(true));
                // The next frame, after the bone moved: the frame before's is the last one asked.
                lower.localPosition = new Vector3(0f, -2f, 0f);
                NextSkinningFrame();
                Assert.Equal(before, renderer.EffectRootTransform(true));
                Assert.Equal(lower.localToWorldMatrix, renderer.EffectRootTransform(false));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(baked);
            }
        }

        /// <summary>What an effect samples of a renderer (tests/projects/vfx `skinned*`): its skin,
        /// whose first stream holds positions and normals and whose vertex count is the mesh's;
        /// once sampled the renderer's vertex buffer has gained the target effects read
        /// (Vertex and Structured, 1 to 17). No skin before the first pass; a destroyed renderer
        /// gives none.</summary>
        [Fact]
        public void AnEffectSamplesTheSkinAndTheRendererGainsTheTarget()
        {
            var (root, bone, mesh, renderer) = OneBoneSkin();
            try
            {
                Assert.Equal(GraphicsBuffer.Target.Vertex, renderer.vertexBufferTarget);
                Assert.Null(renderer.EffectSkin(false));
                Assert.Equal(GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Structured, renderer.vertexBufferTarget);
                NextSkinningFrame();
                var skin = renderer.EffectSkin(false);
                Assert.NotNull(skin);
                Assert.Same(skin, renderer.EffectSkin(true));
                Assert.Equal(mesh.vertexCount, skin.vertexCount);
                Assert.Equal(0, skin.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Position));
                Assert.Equal(GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Structured, skin.vertexBufferTarget);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>Under a non-uniform scale a skin's normals light the same once its buffer was
        /// asked for (tests/projects/renderapi <c>skinNormal.*</c>: before, after and the next
        /// frame draw alike): baked, they are the normals of before.</summary>
        [Fact]
        public void AskingForTheBufferKeepsTheNormalsUnderANonUniformScale()
        {
            var (root, bone, mesh, renderer) = OneBoneSkin();
            root.transform.localScale = new Vector3(2f, 3f, 4f);
            bone.localRotation = Quaternion.Euler(20f, 30f, 40f);
            mesh.normals = new[] { new Vector3(0f, 0f, -1f), new Vector3(0.6f, 0f, -0.8f), new Vector3(0f, 0.6f, -0.8f) };
            var before = new Mesh();
            var after = new Mesh();
            try
            {
                NextSkinningFrame();
                renderer.BakeMesh(before);
                using (renderer.GetVertexBuffer()) { }
                NextSkinningFrame();
                using (renderer.GetVertexBuffer()) { }
                renderer.BakeMesh(after);
                for (int i = 0; i < 3; i++)
                {
                    Assert.True((before.normals[i] - after.normals[i]).sqrMagnitude < 1e-10f, before.normals[i].ToString("F6") + " " + after.normals[i].ToString("F6"));
                    Assert.True((before.vertices[i] - after.vertices[i]).sqrMagnitude < 1e-10f, before.vertices[i].ToString("F6") + " " + after.vertices[i].ToString("F6"));
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(before);
                Object.DestroyImmediate(after);
            }
        }

        /// <summary>The next frame, and its skinning pass (UpdateAllSkinnedMeshes), whatever pass
        /// other tests left last.</summary>
        internal static void NextSkinningFrame()
        {
            Time.s_FrameCount++;
            SkinnedMeshRenderer.SkinningPass();
        }

        static (GameObject root, Transform bone, Mesh mesh, SkinnedMeshRenderer renderer) OneBoneSkin()
        {
            var root = new GameObject("root");
            var bone = new GameObject("bone").transform;
            bone.SetParent(root.transform, false);
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[] { new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f } },
                bindposes = new[] { Matrix4x4.identity },
            };
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            renderer.updateWhenOffscreen = true;
            return (root, bone, mesh, renderer);
        }

        /// <summary>The skinning pass skips a renderer whose buffer was asked for once its mesh is
        /// removed or destroyed, and the renderers after it still skin.</summary>
        [Fact]
        public void TheSkinningPassSkipsRenderersWithoutAMesh()
        {
            var a = OneBoneSkin();
            var b = OneBoneSkin();
            try
            {
                NextSkinningFrame();
                using (a.renderer.GetVertexBuffer()) { }
                using (b.renderer.GetVertexBuffer()) { }
                a.renderer.sharedMesh = null;
                b.bone.localPosition = new Vector3(1f, 0f, 0f);
                NextSkinningFrame();
                Assert.Equal(1f, b.renderer.m_Skinned.vertices[0].x);
                Assert.Null(a.renderer.GetVertexBuffer());
                // A destroyed mesh: not skinned again (its skin keeps the last pose).
                Object.DestroyImmediate(b.mesh);
                b.bone.localPosition = new Vector3(2f, 0f, 0f);
                NextSkinningFrame();
                Assert.Equal(1f, b.renderer.m_Skinned.vertices[0].x);
            }
            finally
            {
                Object.DestroyImmediate(a.root); Object.DestroyImmediate(a.mesh);
                Object.DestroyImmediate(b.root); Object.DestroyImmediate(b.mesh);
            }
        }

        /// <summary>A renderer draws one skin a skinning pass, its first draw's; with
        /// forceMatrixRecalculationPerRender each draw skins the bones as they are then (the
        /// Scripting API: set it "to manually render a skinned mesh multiple times within a
        /// single update").</summary>
        [Fact]
        public void ForcedRecalculationSkinsEachDraw()
        {
            var s = OneBoneSkin();
            try
            {
                NextSkinningFrame();
                Assert.Equal(0f, s.renderer.DrawGeometry(out _).vertices[0].x);
                s.bone.localPosition = new Vector3(1f, 0f, 0f);
                Assert.Equal(0f, s.renderer.DrawGeometry(out _).vertices[0].x);
                // A frame whose pass has not run yet (a camera rendered in Update) draws the last
                // pass's skin; the particle shape takes the pose as it is.
                Time.s_FrameCount++;
                Assert.Equal(0f, s.renderer.DrawGeometry(out _).vertices[0].x);
                var current = s.renderer.CurrentGeometry(out _);
                Assert.Equal(1f, current.vertices[0].x);
                Assert.Equal(0f, s.renderer.DrawGeometry(out _).vertices[0].x);
                // The current pose is skinned once a frame (an emission of many particles reads it
                // once), and again after BakeMesh skinned now, with the bones as they are then.
                int version = current.m_PositionsVersion;
                s.bone.localPosition = new Vector3(1.5f, 0f, 0f);
                Assert.Same(current, s.renderer.CurrentGeometry(out _));
                Assert.Equal(version, current.m_PositionsVersion);
                var baked = new Mesh();
                s.renderer.BakeMesh(baked);
                Object.DestroyImmediate(baked);
                s.bone.localPosition = new Vector3(1.75f, 0f, 0f);
                Assert.Equal(1.75f, s.renderer.CurrentGeometry(out _).vertices[0].x);
                s.bone.localPosition = new Vector3(1f, 0f, 0f);
                s.renderer.forceMatrixRecalculationPerRender = true;
                Assert.Equal(1f, s.renderer.DrawGeometry(out _).vertices[0].x);
                s.bone.localPosition = new Vector3(2f, 0f, 0f);
                Assert.Equal(2f, s.renderer.DrawGeometry(out _).vertices[0].x);
            }
            finally
            {
                Object.DestroyImmediate(s.root); Object.DestroyImmediate(s.mesh);
            }
        }

        /// <summary>The current pose (a particle system's emission shape) is a skin made now, in
        /// world space whether or not the buffer was asked for: asking for it moves only the
        /// pass's skin into the root bone's space. Under a scaled root bone its positions and
        /// normals are the same before and after the request.</summary>
        [Fact]
        public void TheCurrentPoseStaysInWorldSpaceAfterABufferRequest()
        {
            var s = OneBoneSkin();
            try
            {
                s.root.transform.position = new Vector3(10f, 0f, 0f);
                s.bone.localScale = new Vector3(100f, 2f, 3f);
                s.renderer.rootBone = s.bone;
                s.mesh.normals = new[] { new Vector3(0f, 0f, -1f), new Vector3(0.6f, 0f, -0.8f), new Vector3(0f, 0.6f, -0.8f) };
                NextSkinningFrame();
                var before = s.renderer.CurrentGeometry(out var world);
                var vertices = before.vertices;
                var normals = before.normals;
                Assert.Equal(Matrix4x4.identity, world);
                using (s.renderer.GetVertexBuffer()) { }
                NextSkinningFrame();
                using (s.renderer.GetVertexBuffer()) { }
                var after = s.renderer.CurrentGeometry(out var stillWorld);
                Assert.Equal(Matrix4x4.identity, stillWorld);
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(vertices[i], after.vertices[i]);
                    Assert.Equal(normals[i], after.normals[i]);
                }
            }
            finally
            {
                Object.DestroyImmediate(s.root); Object.DestroyImmediate(s.mesh);
            }
        }
    }
}
