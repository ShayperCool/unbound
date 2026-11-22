using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Verifies incremental transform-change dispatch, LODGroup world/cull caching,
    /// version-gated EndFrameMotion, and OnWillRenderObject live script tracking.</summary>
    public sealed class StaticSceneCullingCacheTests : IDisposable
    {
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly float m_LodBias = QualitySettings.lodBias;
        readonly int m_MaxLod = QualitySettings.maximumLODLevel;

        public void Dispose()
        {
            QualitySettings.lodBias = m_LodBias;
            QualitySettings.maximumLODLevel = m_MaxLod;
            foreach (var o in m_Objects)
                if (o != null) Object.DestroyImmediate(o);
            Culling.EndFrame();
        }

        GameObject Track(GameObject go)
        {
            m_Objects.Add(go);
            return go;
        }

        [Fact]
        public void ObjectDispatcherIncrementalTransformTrackingMatchesStateChangesAndDisableMoveEnable()
        {
            var parent = Track(new GameObject("Parent"));
            var a = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var b = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var c = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            a.transform.SetParent(parent.transform, false);
            var ra = a.GetComponent<MeshRenderer>();
            var rb = b.GetComponent<MeshRenderer>();
            var rc = c.GetComponent<MeshRenderer>();

            using var dispatcher = new ObjectDispatcher();
            dispatcher.EnableTransformTracking<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS);

            // Initial seed dispatch reports all live MeshRenderers.
            using (var init = dispatcher.GetTransformChangesAndClear<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS, Allocator.Temp))
            {
                var ids = new HashSet<int>(init.transformedID.ToArray());
                Assert.Contains(ra.GetInstanceID(), ids);
                Assert.Contains(rb.GetInstanceID(), ids);
                Assert.Contains(rc.GetInstanceID(), ids);
            }

            // Steady frame with no movement reports zero changes.
            using (var none = dispatcher.GetTransformChangesAndClear<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS, Allocator.Temp))
            {
                Assert.Equal(0, none.transformedID.Length);
            }

            // Moving parent marks child `a` dirty without marking unmoved sibling `b`.
            parent.transform.position = new Vector3(3f, 1f, -2f);
            using (var movedParent = dispatcher.GetTransformChangesAndClear<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS, Allocator.Temp))
            {
                var ids = new HashSet<int>(movedParent.transformedID.ToArray());
                Assert.Contains(ra.GetInstanceID(), ids);
                Assert.DoesNotContain(rb.GetInstanceID(), ids);
                Assert.DoesNotContain(rc.GetInstanceID(), ids);
                int idx = Array.IndexOf(movedParent.transformedID.ToArray(), ra.GetInstanceID());
                Assert.Equal(ra.transform.localToWorldMatrix, movedParent.localToWorldMatrices[idx]);
            }

            // Moving `c` while disabled, draining while disabled, then re-enabling reports `c`.
            rc.enabled = false;
            c.transform.position = new Vector3(10f, 0f, 0f);
            using (var whileDisabled = dispatcher.GetTransformChangesAndClear<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS, Allocator.Temp))
            {
                Assert.DoesNotContain(rc.GetInstanceID(), whileDisabled.transformedID.ToArray());
            }
            rc.enabled = true;
            using (var afterReenable = dispatcher.GetTransformChangesAndClear<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS, Allocator.Temp))
            {
                var ids = new HashSet<int>(afterReenable.transformedID.ToArray());
                Assert.Contains(rc.GetInstanceID(), ids);
                Assert.DoesNotContain(rb.GetInstanceID(), ids);
            }

            // Newly created MeshRenderer after initial seed is reported immediately.
            var d = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var rd = d.GetComponent<MeshRenderer>();
            using (var added = dispatcher.GetTransformChangesAndClear<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS, Allocator.Temp))
            {
                var ids = new HashSet<int>(added.transformedID.ToArray());
                Assert.Contains(rd.GetInstanceID(), ids);
                Assert.DoesNotContain(rb.GetInstanceID(), ids);
            }
        }

        [Fact]
        public void LodGroupWorldCacheAndCameraLodParamsMatchUncachedAfterMutations()
        {
            QualitySettings.lodBias = 1f;
            QualitySettings.maximumLODLevel = 0;

            var parent = Track(new GameObject("LodParent"));
            var root = Track(new GameObject("LodRoot"));
            root.transform.SetParent(parent.transform, false);
            var r0 = Track(GameObject.CreatePrimitive(PrimitiveType.Cube)).GetComponent<MeshRenderer>();
            var r1 = Track(GameObject.CreatePrimitive(PrimitiveType.Cube)).GetComponent<MeshRenderer>();
            r0.transform.SetParent(root.transform, false);
            r1.transform.SetParent(root.transform, false);

            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.5f, new Renderer[] { r0 }), new LOD(0.15f, new Renderer[] { r1 }) });
            group.size = 2f;
            group.localReferencePoint = new Vector3(1f, 0f, 0f);

            var cam = Track(new GameObject("LodCam")).AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.transform.position = new Vector3(0f, 0f, -5f);

            void AssertMatches()
            {
                var lodParams = LODGroup.CameraLodParams.For(cam);
                int pass = LODGroup.NextCullPass();
                Assert.Equal(group.Draws(r0, cam, cam.transform.position), group.Draws(r0, in lodParams, pass));
                Assert.Equal(group.Draws(r1, cam, cam.transform.position), group.Draws(r1, in lodParams, pass));
            }

            AssertMatches();
            // Move parent, scale root, and change size/localReferencePoint — cache must invalidate cleanly.
            parent.transform.position = new Vector3(0f, 0f, 8f);
            AssertMatches();
            root.transform.localScale = new Vector3(1f, 3f, 1f);
            AssertMatches();
            group.localReferencePoint = new Vector3(0f, 2f, -4f);
            group.size = 0.75f;
            AssertMatches();
            cam.orthographic = true;
            cam.orthographicSize = 3f;
            AssertMatches();
        }

        [Fact]
        public void EndFrameMotionUpdatesMovedStaticAndSkinnedRootBoneRenderers()
        {
            var staticCube = Track(GameObject.CreatePrimitive(PrimitiveType.Cube)).GetComponent<MeshRenderer>();
            var movedCube = Track(GameObject.CreatePrimitive(PrimitiveType.Cube)).GetComponent<MeshRenderer>();

            // Seed initial motion matrices via EndFrameMotion.
            FrameBuilder.s_MotionRendered = true;
            FrameBuilder.EndFrameMotion();
            var initialStatic = staticCube.m_PreviousModel;
            var initialMoved = movedCube.m_PreviousModel;
            Assert.True(staticCube.m_HasPreviousModel);
            Assert.True(movedCube.m_HasPreviousModel);

            // Move only `movedCube` and run EndFrameMotion again.
            movedCube.transform.position = new Vector3(5f, 2f, -1f);
            FrameBuilder.s_MotionRendered = true;
            FrameBuilder.EndFrameMotion();
            Assert.Equal(initialStatic, staticCube.m_PreviousModel);
            Assert.NotEqual(initialMoved, movedCube.m_PreviousModel);
            Assert.Equal(movedCube.transform.localToWorldMatrix, movedCube.m_PreviousModel);

            // Control: SkinnedMeshRenderer whose separate rootBone moves must update even when its own transform does not.
            var bone = Track(new GameObject("RootBone")).transform;
            var skinGo = Track(new GameObject("Skin"));
            var skin = skinGo.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                },
                bindposes = new[] { Matrix4x4.identity },
            };
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone };
            skin.rootBone = bone;
            try
            {
                FrameBuilder.s_MotionRendered = true;
                FrameBuilder.EndFrameMotion();
                var beforeBoneMove = skin.m_PreviousModel;

                bone.position = new Vector3(0f, 4f, 0f);
                FrameBuilder.s_MotionRendered = true;
                FrameBuilder.EndFrameMotion();
                Assert.NotEqual(beforeBoneMove, skin.m_PreviousModel);
                Assert.Equal(skin.MotionMatrix, skin.m_PreviousModel);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>Without any OnWillRenderObject script the pass still gives each renderer it
        /// reaches its bounds, as it did when it looked for scripts: a particle system's first
        /// bounds come from a render (before, the pass returned at once and the system got none).</summary>
        [Fact]
        public void WithoutWillRenderScriptsThePassStillGivesRenderersTheirBounds()
        {
            var go = Track(new GameObject("Particles"));
            var ps = go.AddComponent<ParticleSystem>();
            ps.m_HasBounds = ps.m_DrawPending = ps.m_BoundsFrozen = false;
            Assert.Equal(0, Renderer.s_WillRenderScriptCount);
            Renderer.SendWillRenderObject(Matrix4x4.identity, ~0);
            Assert.True(ps.m_HasBounds);
        }

        sealed class MoveCameraOnVisible : MonoBehaviour
        {
            public Camera Camera;
            void OnBecameVisible() => Camera.transform.position = new Vector3(0f, 0f, -500f);
        }

        /// <summary>OnBecameVisible scripts run inside the culling: a level change they cause (here
        /// the camera moving away when LOD 0 becomes visible) reaches the group's later renderers
        /// in the same culling, as when every renderer read the camera anew.</summary>
        [Fact]
        public void LevelChangesMadeByOnBecameVisibleReachTheRestOfTheCulling()
        {
            QualitySettings.lodBias = 1f;
            QualitySettings.maximumLODLevel = 0;
            var root = Track(new GameObject("LodRoot"));
            var near = Track(GameObject.CreatePrimitive(PrimitiveType.Cube)).GetComponent<MeshRenderer>();
            var far = Track(GameObject.CreatePrimitive(PrimitiveType.Cube)).GetComponent<MeshRenderer>();
            near.transform.SetParent(root.transform, false);
            far.transform.SetParent(root.transform, false);
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.5f, new Renderer[] { near }), new LOD(0.001f, new Renderer[] { far }) });
            group.size = 1f;
            var cam = Track(new GameObject("Cam")).AddComponent<Camera>();
            // At 1 unit LOD 0 is drawn (view distance 1.15 under its reach of 2); at 500, LOD 1.
            cam.transform.position = new Vector3(0f, 0f, -1f);
            near.gameObject.AddComponent<MoveCameraOnVisible>().Camera = cam;
            Assert.True(cam.TryGetCullingParameters(out var p));
            Culling.Cull(ref p);
            Assert.True(near.isVisible);
            Assert.True(far.isVisible, "the level after the camera moved");
        }

        sealed class WillRenderProbe : MonoBehaviour
        {
            public int Calls;
            void OnWillRenderObject() => Calls++;
        }

        [Fact]
        public void WillRenderObjectLiveScriptCountTracksAttachAndDestroy()
        {
            int baseline = Renderer.s_WillRenderScriptCount;
            var cube = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var probe = cube.AddComponent<WillRenderProbe>();
            Assert.Equal(baseline + 1, Renderer.s_WillRenderScriptCount);

            var cam = Track(new GameObject("Cam")).AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 0f, -5f);
            Assert.True(cam.TryGetCullingParameters(out var p));
            Culling.Cull(ref p);
            Assert.Equal(1, probe.Calls);

            Object.DestroyImmediate(probe);
            Assert.Equal(baseline, Renderer.s_WillRenderScriptCount);
        }

        [Fact]
        public void ReflectionProbeCacheInvalidatesOnProbeIntensityMoveAndRendererMove()
        {
            var probeGo = Track(new GameObject("RefProbe"));
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(10f, 10f, 10f);
            probe.intensity = 1f;
            var cubeTex = new Cubemap(4, TextureFormat.RGBA32, false);
            probe.bakedTexture = cubeTex;
            var cubeGo = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var renderer = cubeGo.GetComponent<MeshRenderer>();
            int hdr0Id = Shader.PropertyToID("unity_SpecCube0_HDR");
            int cube0Id = Shader.PropertyToID("unity_SpecCube0");
            try
            {
                ReflectionProbe.BeginFrame();
                var d1 = ReflectionProbe.Apply(renderer, null, srp: true);
                Assert.NotNull(d1);
                Assert.Same(cubeTex, d1[cube0Id]);
                var hdr1 = (Vector4)d1[hdr0Id];

                // Second call with unchanged state returns cached standalone dictionary directly.
                var d2 = ReflectionProbe.Apply(renderer, null, srp: true);
                Assert.Same(d1, d2);

                // Changing probe intensity must invalidate cache even mid-frame.
                probe.intensity = 2.5f;
                var d3 = ReflectionProbe.Apply(renderer, null, srp: true);
                var hdr3 = (Vector4)d3[hdr0Id];
                Assert.True(Mathf.Abs(hdr3.x - hdr1.x * 2.5f) < 1e-4f);

                // Passing existing properties merges without mutating standalone cache.
                int extraId = Shader.PropertyToID("_Slice4Extra");
                var existing = new Dictionary<int, object> { [extraId] = 7f };
                var merged = ReflectionProbe.Apply(renderer, existing, srp: true);
                Assert.Equal(7f, merged[extraId]);
                Assert.Same(cubeTex, merged[cube0Id]);
                Assert.False(d3.ContainsKey(extraId));

                // Moving renderer outside the probe box invalidates cache and returns input unchanged.
                cubeGo.transform.position = new Vector3(100f, 0f, 0f);
                Assert.Null(ReflectionProbe.Apply(renderer, null, srp: true));
                Assert.Same(existing, ReflectionProbe.Apply(renderer, existing, srp: true));
            }
            finally
            {
                Object.DestroyImmediate(cubeTex);
            }
        }

        [Fact]
        public void ReflectionProbeCacheOutlivesAFrameWithTheSameProbes()
        {
            var probe = Track(new GameObject("Probe")).AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(10f, 10f, 10f);
            var cube = new Cubemap(4, TextureFormat.RGBA32, false);
            probe.bakedTexture = cube;
            var renderer = Track(GameObject.CreatePrimitive(PrimitiveType.Cube)).GetComponent<MeshRenderer>();
            try
            {
                ReflectionProbe.BeginFrame();
                var first = ReflectionProbe.Apply(renderer, null, srp: true);
                ReflectionProbe.BeginFrame();
                Assert.Same(first, ReflectionProbe.Apply(renderer, null, srp: true));
                // Another texture of the same probe: new values, whatever the objects' hash codes.
                var other = new Cubemap(4, TextureFormat.RGBA32, false);
                try
                {
                    probe.bakedTexture = other;
                    Assert.Same(other, ReflectionProbe.Apply(renderer, null, srp: true)[Shader.PropertyToID("unity_SpecCube0")]);
                }
                finally { probe.bakedTexture = cube; Object.DestroyImmediate(other); }
            }
            finally { Object.DestroyImmediate(cube); }
        }

        sealed class BrightenProbeOnVisible : MonoBehaviour
        {
            public ReflectionProbe Probe;
            void OnBecameVisible() => Probe.intensity = 3f;
        }

        /// <summary>A shadow caster list's collection runs OnBecameVisible scripts: a probe change
        /// one makes reaches the list's draws, also of a renderer whose probe values were cached
        /// before the list.</summary>
        [Fact]
        public void ProbeChangesMadeWhileAListCollectsReachItsDraws()
        {
            var previousShaderCall = ShaderPassInfo.s_CallForTests;
            var sources = FrameBuilder.Sources.ToArray();
            var camera = Track(new GameObject("Camera")).AddComponent<Camera>();
            var light = Track(new GameObject("Light")).AddComponent<Light>();
            light.type = LightType.Directional;
            var probe = Track(new GameObject("Probe")).AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(10f, 10f, 10f);
            probe.intensity = 1f;
            var cube = new Cubemap(4, TextureFormat.RGBA32, false);
            probe.bakedTexture = cube;
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
            mesh.RecalculateBounds();
            Shader shader = null;
            Material material = null;
            var data = new CullingData { Camera = camera };
            try
            {
                ShaderPassInfo.ResetForTests();
                ShaderPassInfo.s_CallForTests = name => System.Text.Json.JsonSerializer.Serialize(new
                {
                    shader = name, found = true, tags = new Dictionary<string, string>(),
                    passes = new[] { new { name = "ShadowCaster", tags = new Dictionary<string, string> { ["LightMode"] = "ShadowCaster" } } },
                });
                shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/ProbeList\" { SubShader { Pass { Tags { \"LightMode\"=\"ShadowCaster\" } } } }");
                material = new Material(shader) { renderQueue = 2000 };
                MeshRenderer Caster(string name)
                {
                    var go = Track(new GameObject(name));
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = material;
                    data.Renderers.Add(r);
                    return r;
                }
                var brightener = Caster("Brightener");
                brightener.gameObject.AddComponent<BrightenProbeOnVisible>().Probe = probe;
                var cached = Caster("Cached");
                int hdr = Shader.PropertyToID("unity_SpecCube0_HDR");
                float before = ((Vector4)ReflectionProbe.Apply(cached, null, srp: true)[hdr]).x;
                data.Lights = new NativeArray<VisibleLight>(1, Allocator.Temp);
                data.Lights[0] = new VisibleLight(light, default, false, false, false);
                data.ShadowSplits.Add(default);
                data.ShadowLights.Add(new LightShadowCasterCullingInfo { splitRange = new RangeInt(0, 1) });
                var tags = new List<ShaderTagId> { new ShaderTagId("ShadowCaster") };
                FrameBuilder.Sources.Clear();
                FrameBuilder.DrawRendererList(data, new DrawingSettings(tags[0], new SortingSettings(camera)), new FilteringSettings(RenderQueueRange.all),
                    tags, shadows: ShadowObjectsFilter.AllObjects, shadowLight: 0, shadowSplit: 0);
                Assert.Equal(3f, probe.intensity);
                Assert.Equal(before * 3f, ((Vector4)cached.m_ReflectionCache.Values[1]).x, 4);
            }
            finally
            {
                data.Dispose();
                FrameBuilder.Sources.Clear(); FrameBuilder.Sources.AddRange(sources);
                ShaderPassInfo.ResetForTests(); ShaderPassInfo.s_CallForTests = previousShaderCall;
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(cube);
                if (material != null) Object.DestroyImmediate(material);
                if (shader != null) Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void ShaderPropertyFramedUtf8MatchesWireFormatForAsciiAndMultibyte()
        {
            foreach (var name in new[] { "_MainTex", "unity_SpecCube0_HDR", "_Slice4_日本語_Prop" })
            {
                int id = Shader.PropertyToID(name);
                Assert.Equal(name, Shader.IdToName(id));
                var framed = Shader.IdToFramedUtf8(id);
                Assert.NotNull(framed);
                var expectedUtf8 = System.Text.Encoding.UTF8.GetBytes(name);
                Assert.Equal(4 + expectedUtf8.Length, framed.Length);
                uint len = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(framed);
                Assert.Equal((uint)expectedUtf8.Length, len);
                Assert.Equal(expectedUtf8, framed.AsSpan(4).ToArray());
            }
            Assert.Null(Shader.IdToName(-1));
            Assert.Null(Shader.IdToFramedUtf8(-1));
            Assert.Null(Shader.IdToFramedUtf8(int.MaxValue));
        }

        [Fact]
        public void EndFrameMotionTracksNewlyEnabledAndDisabledMovedReenabledRenderers()
        {
            // Ensure seeded first.
            FrameBuilder.s_MotionRendered = true;
            FrameBuilder.EndFrameMotion();

            // Newly enabled renderer after seeding must get updated on next EndFrameMotion.
            var freshGo = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            freshGo.transform.position = new Vector3(1f, 2f, 3f);
            var fresh = freshGo.GetComponent<MeshRenderer>();
            FrameBuilder.s_MotionRendered = true;
            FrameBuilder.EndFrameMotion();
            Assert.True(fresh.m_HasPreviousModel);
            Assert.Equal(fresh.transform.localToWorldMatrix, fresh.m_PreviousModel);

            // Disable, move, run EndFrameMotion while disabled, then re-enable and run EndFrameMotion.
            fresh.enabled = false;
            freshGo.transform.position = new Vector3(9f, 8f, 7f);
            FrameBuilder.s_MotionRendered = true;
            FrameBuilder.EndFrameMotion();
            fresh.enabled = true;
            FrameBuilder.s_MotionRendered = true;
            FrameBuilder.EndFrameMotion();
            Assert.Equal(fresh.transform.localToWorldMatrix, fresh.m_PreviousModel);
        }
    }
}
