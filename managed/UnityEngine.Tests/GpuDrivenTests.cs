using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using Unity.Collections;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The engine side of the GPU Resident Drawer: ObjectDispatcher change tracking and
    /// the renderers GPUDrivenProcessor takes (tests/projects/residentdrawer compares both with the
    /// Editor through the drawer). Declarations come from a stand-in host: a shader declares
    /// DOTS_INSTANCING_ON or not.</summary>
    public sealed class GpuDrivenTests : IDisposable
    {
        readonly List<Object> m_Made = new List<Object>();
        readonly ObjectDispatcher m_Dispatcher = new ObjectDispatcher();
        readonly Shader m_Dots, m_Plain;
        static int s_Made;
        readonly List<string> m_Logs = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;

        public GpuDrivenTests()
        {
            ShaderDeclarations.ResetForTests();
            ShaderDeclarations.s_FrameForTests = 1;
            ShaderDeclarations.s_CallForTests = Call;
            m_Dots = ShaderUtil.CreateShaderAsset("Shader \"Tests/GpuDriven/Dots" + (++s_Made) + "\" { SubShader { Pass { } } }");
            m_Plain = ShaderUtil.CreateShaderAsset("Shader \"Tests/GpuDriven/Plain" + (++s_Made) + "\" { SubShader { Pass { } } }");
            // Unbound's own diagnostics bypass Application's log callbacks: they are observed at
            // the logger's handler.
            m_PreviousHandler = m_Logger.logHandler;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;

            internal CaptureHandler(ILogHandler inner, List<string> logs)
            {
                m_Inner = inner;
                m_Logs = logs;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type != LogType.Log) m_Logs.Add(type + ": " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        public void Dispose()
        {
            m_Logger.logHandler = m_PreviousHandler;
            m_Dispatcher.Dispose();
            foreach (var o in m_Made) if (o) Object.DestroyImmediate(o);
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.s_FrameForTests = null;
            ShaderDeclarations.ResetForTests();
        }

        string Call(string method, string request)
        {
            using var document = JsonDocument.Parse(request);
            var name = document.RootElement.GetProperty("name").GetString();
            string sets = name == m_Dots.name ? "[[\"_\",\"DOTS_INSTANCING_ON\"]]" : "[[\"_\",\"SOMETHING_ELSE\"]]";
            return "{\"found\":true,\"subshaders_known\":true,\"keywords_known\":true,\"transient\":false,\"reason\":\"\",\"subshaders\":[{\"tags\":[],\"passes\":[{\"name\":\"P\",\"tags\":[],\"keyword_sets\":"
                + sets + "}],\"lod\":0,\"restricted\":false}],\"keywords\":[]}";
        }

        T Keep<T>(T o) where T : Object
        {
            m_Made.Add(o);
            return o;
        }

        MeshRenderer Renderer(string name, Shader shader)
        {
            var go = Keep(new GameObject(name));
            go.AddComponent<MeshFilter>().sharedMesh = Keep(new Mesh { name = name + " mesh" });
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Keep(new Material(shader));
            return renderer;
        }

        int[] Changed<T>() where T : Object
        {
            var data = m_Dispatcher.GetTypeChangesAndClear<T>(Allocator.Temp);
            try { return data.changedID.ToArray(); }
            finally { data.Dispose(); }
        }

        [Fact]
        public void TrackingQueuesTheObjectsAliveThenAsCreated()
        {
            var renderer = Renderer("alive", m_Dots);
            m_Dispatcher.EnableTypeTracking<MeshRenderer>();
            Assert.Contains(renderer.GetInstanceID(), Changed<MeshRenderer>());
            // Asked again: nothing new.
            Assert.DoesNotContain(renderer.GetInstanceID(), Changed<MeshRenderer>());
        }

        [Fact]
        public void ChangesMoveToTheEndAndDestroyedObjectsLeaveTheChangedQueue()
        {
            m_Dispatcher.EnableTypeTracking<MeshRenderer>();
            var a = Renderer("a", m_Dots);
            var b = Renderer("b", m_Dots);
            var c = Renderer("c", m_Dots);
            a.shadowCastingMode = ShadowCastingMode.Off;
            var ids = Changed<MeshRenderer>();
            Assert.Equal(new[] { b.GetInstanceID(), c.GetInstanceID(), a.GetInstanceID() }, Array.FindAll(ids, id => id == a.GetInstanceID() || id == b.GetInstanceID() || id == c.GetInstanceID()));
            b.receiveShadows = false;
            int bId = b.GetInstanceID();
            Object.DestroyImmediate(b.gameObject);
            var data = m_Dispatcher.GetTypeChangesAndClear<MeshRenderer>(Allocator.Temp);
            Assert.DoesNotContain(bId, data.changedID.ToArray());
            Assert.Contains(bId, data.destroyedID.ToArray());
            data.Dispose();
        }

        [Fact]
        public void TypesAreTrackedExactlyAndPropertyBlocksMaterialsAndMeshesRecordChanges()
        {
            m_Dispatcher.EnableTypeTracking<Renderer>();
            m_Dispatcher.EnableTypeTracking<MeshRenderer>();
            m_Dispatcher.EnableTypeTracking<Material>();
            m_Dispatcher.EnableTypeTracking<Mesh>();
            var renderer = Renderer("r", m_Dots);
            Changed<Renderer>();
            Changed<MeshRenderer>();
            Changed<Material>();
            Changed<Mesh>();
            renderer.SetPropertyBlock(new MaterialPropertyBlock());
            renderer.sharedMaterial.SetColor("_BaseColor", Color.red);
            renderer.GetComponent<MeshFilter>().sharedMesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
            Assert.Contains(renderer.GetInstanceID(), Changed<MeshRenderer>());
            // A base type of the object is not its type.
            Assert.DoesNotContain(renderer.GetInstanceID(), Changed<Renderer>());
            Assert.Contains(renderer.sharedMaterial.GetInstanceID(), Changed<Material>());
            Assert.Contains(renderer.GetComponent<MeshFilter>().sharedMesh.GetInstanceID(), Changed<Mesh>());
        }

        [Fact]
        public void TransformChangesListTheMovedComponentsWithTheirMatrices()
        {
            var still = Renderer("still", m_Dots);
            var moving = Renderer("moving", m_Dots);
            m_Dispatcher.EnableTransformTracking<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS);
            // First dispatch: every live one, not seen before.
            m_Dispatcher.GetTransformChangesAndClear<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS, Allocator.Temp).Dispose();
            moving.transform.position = new Vector3(1, 2, 3);
            var data = m_Dispatcher.GetTransformChangesAndClear<MeshRenderer>(ObjectDispatcher.TransformTrackingType.GlobalTRS, Allocator.Temp);
            var ids = data.transformedID.ToArray();
            Assert.Contains(moving.GetInstanceID(), ids);
            Assert.DoesNotContain(still.GetInstanceID(), ids);
            Assert.Equal(new Vector3(1, 2, 3), (Vector3)data.localToWorldMatrices[Array.IndexOf(ids, moving.GetInstanceID())].GetColumn(3));
            data.Dispose();
        }

        [Fact]
        public void UndispatchedTypesStopBeingTrackedAfterTheHistoryWithTheEditorsError()
        {
            Assert.Equal(64, m_Dispatcher.maxDispatchHistoryFramesCount);
            m_Dispatcher.maxDispatchHistoryFramesCount = 2;
            m_Dispatcher.EnableTypeTracking<Material>();
            var system = (ObjectDispatch.System)typeof(ObjectDispatcher).GetField("m_System", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(m_Dispatcher);
            system.CheckHistory(Time.frameCount + 2);
            Assert.Empty(m_Logs);
            system.CheckHistory(Time.frameCount + 3);
            Assert.Contains(m_Logs, m => m.StartsWith("Error: ObjectDispatcher hasn't collected changes for type Material for more than 2 frames."));
            Assert.Equal(0, system.TypeCount);
        }

        [Fact]
        public void ChangesOfARenderersDataOutsideItRecordTheRenderer()
        {
            m_Dispatcher.EnableTypeTracking<MeshRenderer>();
            m_Dispatcher.EnableTypeTracking<Material>();
            var renderer = Renderer("held", m_Dots);
            var other = Renderer("other", m_Dots);
            int id = renderer.GetInstanceID();
            Changed<MeshRenderer>();
            Changed<Material>();
            // The filter's own instance of its mesh, made on first access: a new mesh drawn.
            var filter = renderer.GetComponent<MeshFilter>();
            Keep(filter.mesh);
            Assert.Equal(new[] { id }, Changed<MeshRenderer>());
            Assert.Same(filter.sharedMesh, filter.mesh);
            Assert.Empty(Changed<MeshRenderer>());
            // Its LOD group and levels, set after it was drawn, and the group destroyed.
            var group = Keep(new GameObject("group")).AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.5f, new Renderer[] { renderer }) });
            Assert.Equal(new[] { id }, Changed<MeshRenderer>());
            group.SetLODs(new[] { new LOD(0.5f, new Renderer[] { other }), new LOD(0.1f, new Renderer[] { renderer }) });
            Assert.Equal(new[] { id, other.GetInstanceID() }, Sorted(Changed<MeshRenderer>(), id, other.GetInstanceID()));
            Object.DestroyImmediate(group);
            Assert.Null(renderer.m_LODGroup);
            Assert.Equal(new[] { id, other.GetInstanceID() }, Sorted(Changed<MeshRenderer>(), id, other.GetInstanceID()));
            // Its layer; the same layer again changes nothing.
            renderer.gameObject.layer = 30;
            Assert.Equal(new[] { id }, Changed<MeshRenderer>());
            renderer.gameObject.layer = 30;
            Assert.Empty(Changed<MeshRenderer>());
            // A material pass turned off (GPU-driven rendering keeps the motion vectors pass's).
            var material = renderer.sharedMaterial;
            material.SetShaderPassEnabled("MotionVectors", false);
            Assert.Equal(new[] { material.GetInstanceID() }, Changed<Material>());
            material.SetShaderPassEnabled("MotionVectors", false);
            Assert.Empty(Changed<Material>());
            material.SetShaderPassEnabled("MotionVectors", true);
            Assert.Equal(new[] { material.GetInstanceID() }, Changed<Material>());
        }

        /// <summary>The ids among <paramref name="ids"/> that are <paramref name="a"/> or
        /// <paramref name="b"/>, in that order.</summary>
        static int[] Sorted(int[] ids, int a, int b)
        {
            var found = new List<int>();
            if (Array.IndexOf(ids, a) >= 0) found.Add(a);
            if (Array.IndexOf(ids, b) >= 0) found.Add(b);
            Assert.Equal(found.Count, ids.Length);
            return found.ToArray();
        }

        [Fact]
        public void LightmappedRenderersStayOnTheOrdinaryPathWithAWarning()
        {
            var processor = new GPUDrivenProcessor();
            var lightmapped = Renderer("lightmapped", m_Dots);
            lightmapped.lightmapIndex = 0;
            var influence = Renderer("influence", m_Dots);
            influence.lightmapIndex = 0xFFFE;
            var none = Renderer("none", m_Dots);
            Assert.Equal(-1, none.lightmapIndex);
            var taken = Taken(processor, lightmapped, influence, none);
            Assert.Equal(new[] { influence.GetInstanceID(), none.GetInstanceID() }, taken);
            Assert.False(lightmapped.m_GpuDriven);
            Assert.Contains(m_Logs, m => m.StartsWith("Warning: Unbound: GPU-driven rendering does not take lightmapped renderers yet"));
            processor.Dispose();
        }

        static List<int> Taken(GPUDrivenProcessor processor, params MeshRenderer[] renderers)
        {
            var ids = new int[renderers.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = renderers[i].GetInstanceID();
            var taken = new List<int>();
            processor.EnableGPUDrivenRenderingAndDispatchRendererData(ids, (in GPUDrivenRendererGroupData data, IList<Mesh> meshes, IList<Material> materials) =>
                taken.AddRange(data.rendererGroupID.ToArray()));
            return taken;
        }

        [Fact]
        public void TheProcessorTakesEligibleRenderersAndHidesThemFromTheOrdinaryCulling()
        {
            var processor = new GPUDrivenProcessor();
            var plain = Renderer("plain", m_Dots);
            var block = Renderer("block", m_Dots);
            block.SetPropertyBlock(Keep2(new MaterialPropertyBlock(), b => b.SetColor("_BaseColor", Color.red)));
            var ordered = Renderer("ordered", m_Dots);
            ordered.sortingOrder = 1;
            var noDots = Renderer("noDots", m_Plain);
            var disallowed = Renderer("disallowed", m_Dots);
            disallowed.allowGPUDrivenRendering = false;
            var disabled = Renderer("disabled", m_Dots);
            disabled.enabled = false;
            // Streams given before the renderer was first drawn (no merged mesh yet), and an
            // Animation without clips culling by its renderers: taken.
            var streams = Renderer("streams", m_Dots);
            streams.additionalVertexStreams = streams.GetComponent<MeshFilter>().sharedMesh;
            var clipless = Renderer("clipless", m_Dots);
            clipless.gameObject.AddComponent<Animation>().cullingType = AnimationCullingType.BasedOnRenderers;
            var animated = Renderer("animated", m_Dots);
            animated.gameObject.AddComponent<Animator>().cullingMode = AnimatorCullingMode.CullCompletely;
            // Realtime GI's own vertex stream: refused.
            var realtime = Renderer("realtime", m_Dots);
            realtime.enlightenVertexStream = realtime.GetComponent<MeshFilter>().sharedMesh;
            // More than 128 materials: the Editor's error, and taken all the same.
            var many = Renderer("many", m_Dots);
            var materials = new Material[129];
            for (int i = 0; i < materials.Length; i++) materials[i] = many.sharedMaterial;
            many.sharedMaterials = materials;
            var taken = Taken(processor, plain, block, ordered, noDots, disallowed, disabled, streams, clipless, animated, realtime, many);
            Assert.Equal(new[] { plain.GetInstanceID(), streams.GetInstanceID(), clipless.GetInstanceID(), many.GetInstanceID() }, taken);
            Assert.Contains("Error: The number of sub-meshes on the MeshRenderer \"many\" is 129. GPU Resident Drawer does not support more than 128 sub-meshes. The normal rendering path will be used for this MeshRenderer instead.", m_Logs);
            Assert.True(plain.m_GpuDriven);
            Assert.False(block.m_GpuDriven);
            // A camera's culling leaves it to GPU-driven rendering and marks it visible, even
            // behind the camera on a layer the camera does not draw; an ordinary renderer there
            // is not.
            var camera = Keep(new GameObject("camera")).AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, 5);
            camera.cullingMask = ~(1 << 30);
            plain.gameObject.layer = 30;
            block.gameObject.layer = 30;
            var renderers = Cull(camera);
            Assert.DoesNotContain(plain, renderers);
            Assert.DoesNotContain(block, renderers);
            Assert.True(plain.isVisible);
            Assert.False(block.isVisible);
            plain.gameObject.layer = 0;
            // Turned off again: drawn as usual.
            processor.DisableGPUDrivenRendering(new[] { plain.GetInstanceID() });
            Assert.False(plain.m_GpuDriven);
            Assert.Contains(plain, Cull(camera));
            processor.Dispose();
        }

        static List<Renderer> Cull(Camera camera)
        {
            Assert.True(camera.TryGetCullingParameters(out var parameters));
            var renderers = new List<Renderer>(Culling.Get(Culling.Cull(ref parameters)).Renderers);
            Culling.EndFrame();
            return renderers;
        }

        static MaterialPropertyBlock Keep2(MaterialPropertyBlock block, Action<MaterialPropertyBlock> set)
        {
            set(block);
            return block;
        }

        [Fact]
        public void PackedDataKeepsEveryField()
        {
            var packed = new GPUDrivenPackedRendererData(true, false, 0b101, ShadowCastingMode.TwoSided, LightProbeUsage.UseProxyVolume,
                MotionVectorGenerationMode.ForceNoMotion, true, false, true, false, true);
            Assert.True(packed.receiveShadows);
            Assert.False(packed.staticShadowCaster);
            Assert.Equal(0b101, packed.lodMask);
            Assert.Equal(ShadowCastingMode.TwoSided, packed.shadowCastingMode);
            Assert.Equal(LightProbeUsage.UseProxyVolume, packed.lightProbeUsage);
            Assert.Equal(MotionVectorGenerationMode.ForceNoMotion, packed.motionVecGenMode);
            Assert.True(packed.isPartOfStaticBatch);
            Assert.False(packed.movedCurrentFrame);
            Assert.True(packed.hasTree);
            Assert.False(packed.smallMeshCulling);
            Assert.True(packed.supportsIndirect);
            var material = new GPUDrivenPackedMaterialData(true, false, true);
            Assert.True(material.isTransparent && !material.isMotionVectorsPassEnabled && material.isIndirectSupported);
            Assert.True(material.Equals(new GPUDrivenPackedMaterialData(true, false, true)));
        }

        [Fact]
        public void LodGroupDataPlacesTheGroupAndListsItsLevels()
        {
            var processor = new GPUDrivenProcessor();
            var go = Keep(new GameObject("group"));
            go.transform.position = new Vector3(1, 0, 0);
            go.transform.localScale = new Vector3(2, -3, 1);
            var group = go.AddComponent<LODGroup>();
            var a = Renderer("lod a", m_Dots);
            var b = Renderer("lod b", m_Dots);
            group.SetLODs(new[] { new LOD(0.5f, new Renderer[] { a, b }) { fadeTransitionWidth = 0.25f }, new LOD(0.1f, new Renderer[] { b }) });
            group.size = 2f;
            group.localReferencePoint = new Vector3(0, 1, 0);
            var animated = Keep(new GameObject("animated")).AddComponent<LODGroup>();
            animated.animateCrossFading = true;
            int[] ids = null, invalid = null;
            short[] counts = null;
            processor.DispatchLODGroupData(new[] { group.GetInstanceID(), animated.GetInstanceID() }, (in GPUDrivenLODGroupData data) =>
            {
                ids = data.lodGroupID.ToArray();
                invalid = data.invalidLODGroupID.ToArray();
                counts = data.lodRenderersCount.ToArray();
                Assert.Equal(new Vector3(1, -3, 0), data.worldSpaceReferencePoint[0]);
                Assert.Equal(6f, data.worldSpaceSize[0]);
                Assert.Equal(3, data.renderersCount[0]);
                Assert.Equal(0.25f, data.lodFadeTransitionWidth[0]);
            });
            Assert.Equal(new[] { group.GetInstanceID() }, ids);
            Assert.Equal(new[] { animated.GetInstanceID() }, invalid);
            Assert.Equal(new short[] { 2, 1 }, counts);
            Assert.Equal(0b11, group.LevelMask(b));
            Assert.Equal(0b01, group.LevelMask(a));
            processor.Dispose();
        }
    }
}
