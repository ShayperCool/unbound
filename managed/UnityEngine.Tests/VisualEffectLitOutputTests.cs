using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Outputs of meshes and what an effect's renderer takes from its asset: with the
    /// simulation tests' effects (their helpers build the compiled graphs).</summary>
    public partial class VisualEffectSimulationTests
    {
        [Fact]
        public void AMeshOutputDrawsAnInstanceForEachParticleOfEachSubmeshItsMaskNames()
        {
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/VfxMeshes\" { SubShader { Pass { Tags { \"LightMode\"=\"UniversalForwardOnly\" } } } }");
            var material = new Material(shader);
            // Three submeshes of a triangle each.
            var mesh = new Mesh { vertices = new Vector3[9], subMeshCount = 3 };
            for (int i = 0; i < 3; i++) mesh.SetTriangles(new[] { 3 * i, 3 * i + 1, 3 * i + 2 }, i);
            var single = new Mesh { vertices = new Vector3[3], triangles = new[] { 0, 1, 2 } };
            var effect = Running(WithOutput(VFXTaskType.ParticleMeshOutput, material, new[] { VFXValueType.Mesh, VFXValueType.Uint32 },
                new VFXMapping("mesh", 1), new VFXMapping("subMeshMask", 2)));
            var eye = new GameObject("eye").AddComponent<Camera>();
            try
            {
                using var warnings = new Warnings();
                Assert.True(effect.TryGetComponent<VFXRenderer>(out var renderer));
                var camera = new FrameBuilder.CameraContext { Camera = eye, Position = new Vector3(0f, 0f, -10f), Forward = Vector3.forward };
                var draws = new List<FrameBuilder.DrawItem>();
                (int Submesh, uint Instances, ulong Mesh)[] Drawn(Mesh of, uint mask)
                {
                    effect.Values.SetObject(1, of);
                    effect.Values.SetWord(2, mask);
                    draws.Clear();
                    renderer.CollectDraws(camera, draws);
                    var drawn = new (int, uint, ulong)[draws.Count];
                    for (int i = 0; i < drawn.Length; i++)
                    {
                        Assert.Same(material, draws[i].Material);
                        Assert.Same(renderer, draws[i].Renderer);
                        // Instances of the mesh's own indices: none of a buffer's.
                        Assert.Equal((0ul, 0u), (draws[i].Setup.Value.Indices, draws[i].Setup.Value.IndexCount));
                        drawn[i] = (draws[i].Submesh, draws[i].Setup.Value.Instances, draws[i].Mesh);
                    }
                    return drawn;
                }
                // Nothing before the first step.
                Assert.Empty(Drawn(mesh, 0xffffffffu));
                Counts(effect, 4);
                ulong handle = mesh.Sync();
                // Every submesh, in the submeshes' order, each an instance per particle.
                Assert.Equal(new[] { (0, 4u, handle), (1, 4u, handle), (2, 4u, handle) }, Drawn(mesh, 0xffffffffu));
                Assert.Equal(new[] { (0, 4u, handle), (2, 4u, handle) }, Drawn(mesh, 0b101u));
                Assert.Equal(new[] { (1, 4u, handle) }, Drawn(mesh, 0b10u));
                // No submesh named, only ones the mesh does not have, no mesh, a destroyed one: no draw.
                Assert.Empty(Drawn(mesh, 0u));
                Assert.Empty(Drawn(mesh, 0xfffffff8u));
                Assert.Empty(Drawn(null, 0xffffffffu));
                Assert.Equal(new[] { (0, 4u, single.Sync()) }, Drawn(single, 0xffffffffu));
                Assert.Empty(Drawn(single, 0b110u));
                Object.DestroyImmediate(single);
                Assert.Empty(Drawn(single, 0xffffffffu));
                Assert.DoesNotContain(warnings.Messages, m => m.Contains("not drawn"));
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
                Object.DestroyImmediate(eye.gameObject);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void AnEffectsRendererCastsShadowsAndWritesMotionVectorsAsItsAssetsGraphCompiled()
        {
            var casting = Counting(30f);
            casting.m_Data.ShadowCastingMode = ShadowCastingMode.On;
            casting.m_Data.MotionVectorGenerationMode = MotionVectorGenerationMode.Object;
            casting.m_Settings.Renderer.receiveShadows = true;
            // What the asset saves of these two is not what its renderer takes.
            var plain = Counting(30f);
            plain.m_Settings.Renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            plain.m_Settings.Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            var effect = new GameObject("effect").AddComponent<VisualEffect>();
            try
            {
                Assert.True(effect.TryGetComponent<VFXRenderer>(out var renderer));
                (ShadowCastingMode, bool, MotionVectorGenerationMode) Asked() => (renderer.shadowCastingMode, renderer.receiveShadows, renderer.motionVectorGenerationMode);
                Assert.Equal((ShadowCastingMode.Off, false, MotionVectorGenerationMode.Camera), Asked());
                effect.visualEffectAsset = casting;
                Assert.Equal((ShadowCastingMode.On, true, MotionVectorGenerationMode.Object), Asked());
                // A renderer that casts shadows is in every light's list of casters: there too
                // it is culled by its own bounds, like a mesh's, not taken wherever it is.
                Assert.True(FrameBuilder.Culler.TrustsBounds(renderer));
                effect.visualEffectAsset = plain;
                Assert.Equal((ShadowCastingMode.Off, false, MotionVectorGenerationMode.Camera), Asked());
                effect.visualEffectAsset = casting;
                effect.visualEffectAsset = null;
                Assert.Equal((ShadowCastingMode.Off, false, MotionVectorGenerationMode.Camera), Asked());
                // A setter of an object takes one: none is refused whatever the name, and so
                // is an engine object that was destroyed.
                Assert.Equal("m", Assert.Throws<ArgumentNullException>(() => effect.SetMesh("Shape", null)).ParamName);
                Assert.Equal("t", Assert.Throws<ArgumentNullException>(() => effect.SetTexture("Nothing", null)).ParamName);
                Assert.Equal("c", Assert.Throws<ArgumentNullException>(() => effect.SetAnimationCurve("Nothing", null)).ParamName);
                Assert.Equal("g", Assert.Throws<ArgumentNullException>(() => effect.SetGradient("Nothing", null)).ParamName);
                var gone = new Mesh();
                Object.DestroyImmediate(gone);
                Assert.Throws<MissingReferenceException>(() => effect.SetMesh("Shape", gone));
                var goneTexture = new Texture2D(2, 2);
                Object.DestroyImmediate(goneTexture);
                Assert.Throws<MissingReferenceException>(() => effect.SetTexture("Nothing", goneTexture));
                // A name the effect does not have is no error with an object.
                var live = new Mesh();
                effect.SetMesh("Nothing", live);
                Object.DestroyImmediate(live);

                // The renderer lists the materials of the graph's outputs: by their sort
                // priority, and in the systems' and tasks' order where that is the same.
                Assert.Empty(renderer.sharedMaterials);
                var shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/VfxListed\" { SubShader { Pass { } } }");
                Material Named(string name) => new Material(shader) { name = name };
                Material a = Named("a"), b = Named("b"), c = Named("c"), d = Named("d");
                var listed = Counting(30f);
                VFXEditorTaskDesc Output(int priority) => new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, parameters = new[] { new VFXMapping("sortPriority", priority) } };
                listed.m_Data.Systems[0].tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.ConstantRateSpawner }, new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput } };
                listed.m_Data.Systems[1].tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.Update }, Output(1), Output(-2), Output(1) };
                listed.m_Data.Processors = new[] { new Object[] { null, a }, new Object[] { null, b, c, d } };
                effect.visualEffectAsset = listed;
                Assert.Equal(new[] { c, a, b, d }, renderer.sharedMaterials);
                effect.visualEffectAsset = null;
                Assert.Empty(renderer.sharedMaterials);
                foreach (var material in new[] { a, b, c, d }) Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }
    }
}
