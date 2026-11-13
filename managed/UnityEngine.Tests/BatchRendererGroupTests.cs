using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>BatchRendererGroup: registration and batches as the Scripting API describes them,
    /// the culling callback a scriptable culling invokes (its context as the Editor gives it for a
    /// camera, tests/projects/brg), the draw items its commands become and the release of the
    /// arrays it allocated.</summary>
    public sealed unsafe class BatchRendererGroupTests : IDisposable
    {
        readonly GameObject m_CameraObject = new GameObject("brg camera");
        readonly Camera m_Camera;
        readonly Mesh m_Mesh = new Mesh();
        readonly Shader m_Shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/Brg\" { SubShader { Pass { } } }");
        readonly Material m_Material;
        readonly List<BatchRendererGroup> m_Groups = new List<BatchRendererGroup>();
        readonly List<GraphicsBuffer> m_Buffers = new List<GraphicsBuffer>();
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public BatchRendererGroupTests()
        {
            m_Camera = m_CameraObject.AddComponent<Camera>();
            m_Camera.transform.position = new Vector3(0f, 1.5f, -6f);
            m_Camera.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            m_Mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0f, 2f, 0f) };
            m_Mesh.triangles = new[] { 0, 1, 2 };
            m_Material = new Material(m_Shader);
        }

        public void Dispose()
        {
            Culling.EndFrame();
            foreach (var group in m_Groups) group.Dispose();
            foreach (var buffer in m_Buffers) buffer.Dispose();
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
            Object.DestroyImmediate(m_CameraObject);
            Object.DestroyImmediate(m_Material);
            Object.DestroyImmediate(m_Shader);
            Object.DestroyImmediate(m_Mesh);
        }

        BatchRendererGroup Group(BatchRendererGroup.OnPerformCulling culling, IntPtr user = default)
        {
            var group = new BatchRendererGroup(culling, user);
            m_Groups.Add(group);
            return group;
        }

        static JobHandle Nothing(BatchRendererGroup g, BatchCullingContext c, BatchCullingOutput o, IntPtr u) => default;

        CullingData Cull()
        {
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            return Culling.Get(Culling.Cull(ref parameters));
        }

        BatchID AddBatch(BatchRendererGroup group, params (string Name, uint Value)[] metadata)
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 64, 4);
            m_Buffers.Add(buffer);
            var values = new NativeArray<MetadataValue>(metadata.Length, Allocator.Temp);
            for (int i = 0; i < metadata.Length; i++) values[i] = new MetadataValue { NameID = Shader.PropertyToID(metadata[i].Name), Value = metadata[i].Value };
            // The device's target: a constant buffer through a window of the whole buffer.
            var id = BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer
                ? group.AddBatch(values, buffer.bufferHandle, 0, 256)
                : group.AddBatch(values, buffer.bufferHandle);
            values.Dispose();
            return id;
        }

        /// <summary>AddBatch refuses, with the Editor's message and BatchID.Null, a window the
        /// device does not take (tests/projects/brg errors.add, on each device): none for a
        /// constant buffer, one at an offset off the alignment or larger than the device's constant
        /// buffers (twice the shader's window is taken), any window or offset for a raw buffer. A
        /// refused batch takes no ID.</summary>
        [Theory]
        [InlineData("OpenGLCore")]
        [InlineData("Vulkan")]
        public void AddBatchRefusesWindowsTheDeviceDoesNotTake(string device)
        {
            var previousDevice = GraphicsDeviceProfile.Current;
            var values = new NativeArray<MetadataValue>(0, Allocator.Temp);
            var logged = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            try
            {
                GraphicsDeviceProfile.Select(device);
                var group = Group(Nothing);
                var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 64, 4);
                m_Buffers.Add(buffer);
                Debug.unityLogger.logHandler = new CapturingHandler(logged, previous);
                if (device == "OpenGLCore")
                {
                    Assert.Equal(BatchBufferTarget.ConstantBuffer, BatchRendererGroup.BufferTarget);
                    Assert.Equal(BatchID.Null, group.AddBatch(values, buffer.bufferHandle));
                    Assert.Equal(BatchID.Null, group.AddBatch(values, buffer.bufferHandle, 16, 256));
                    Assert.Equal(1u, group.AddBatch(values, buffer.bufferHandle, 0, 32768).value);
                    Assert.Equal(BatchID.Null, group.AddBatch(values, buffer.bufferHandle, 0, 65536 + 256));
                    Assert.Equal(new[]
                    {
                        "BatchRendererGroup.AddBatch requires a non-zero windowSize parameter when BufferTarget is ConstantBuffer.",
                        "BatchRendererGroup.AddBatch windowOffset parameter (16) is not aligned according to the constant buffer alignment requirement of the Graphics API (constantBufferOffsetAlignment: 256).",
                        "BatchRendererGroup.AddBatch windowSize parameter (65792) is over the limit allowed by the Graphics API (maxConstantBufferSize: 65536).",
                    }, logged);
                }
                else
                {
                    Assert.Equal(BatchBufferTarget.RawBuffer, BatchRendererGroup.BufferTarget);
                    Assert.Equal(BatchID.Null, group.AddBatch(values, buffer.bufferHandle, 0, 256));
                    Assert.Equal(BatchID.Null, group.AddBatch(values, buffer.bufferHandle, 256, 0));
                    Assert.Equal(1u, group.AddBatch(values, buffer.bufferHandle).value);
                    Assert.Equal(new[]
                    {
                        "BatchRendererGroup.AddBatch had non-zero ConstantBuffer mode parameters when BufferTarget is RawBuffer. Pass zero for both windowOffset and windowSize in RawBuffer mode. (got windowOffset: 0, windowSize: 256)",
                        "BatchRendererGroup.AddBatch had non-zero ConstantBuffer mode parameters when BufferTarget is RawBuffer. Pass zero for both windowOffset and windowSize in RawBuffer mode. (got windowOffset: 256, windowSize: 0)",
                    }, logged);
                }
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                values.Dispose();
                GraphicsDeviceProfile.Select(previousDevice.Type.ToString());
            }
        }

        [Fact]
        public void RegistrationsAreOneBasedAndCountTheirOwners()
        {
            var group = Group(Nothing);
            var other = new Mesh();
            try
            {
                var mesh = group.RegisterMesh(m_Mesh);
                Assert.Equal(1u, mesh.value);
                Assert.Equal(2u, group.RegisterMesh(other).value);
                // The same mesh again: its ID, one more owner.
                Assert.Equal(mesh, group.RegisterMesh(m_Mesh));
                group.UnregisterMesh(mesh);
                Assert.Same(m_Mesh, group.GetRegisteredMesh(mesh));
                group.UnregisterMesh(mesh);
                Assert.Null(group.GetRegisteredMesh(mesh));
                Assert.Equal(1u, group.RegisterMaterial(m_Material).value);
                Assert.Same(m_Material, group.GetRegisteredMaterial(new BatchMaterialID { value = 1 }));
                Assert.Equal(BatchMeshID.Null, group.RegisterMesh(null));
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Fact]
        public void BatchesCarryTheirMetadataExactlyUntilRemoved()
        {
            var group = Group(Nothing);
            var batch = AddBatch(group, ("unity_ObjectToWorld", 0x80000060u), ("_BaseColor", 0x8000ffffu));
            Assert.Equal(1u, batch.value);
            var properties = group.GetBatch(batch).Properties;
            // Above 2^24: a float would round it.
            Assert.Equal(unchecked((int)0x80000060u), properties[Shader.PropertyToID("<DOTS>unity_ObjectToWorld")]);
            Assert.Equal(unchecked((int)0x8000ffffu), properties[Shader.PropertyToID("<DOTS>_BaseColor")]);
            Assert.Equal(m_Buffers[0].bufferHandle, properties[BatchCulling.InstanceDataId]);
            var larger = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 128, 4);
            m_Buffers.Add(larger);
            group.SetBatchBuffer(batch, larger.bufferHandle);
            Assert.Equal(larger.bufferHandle, group.GetBatch(batch).Properties[BatchCulling.InstanceDataId]);
            group.RemoveBatch(batch);
            Assert.Null(group.GetBatch(batch));
            Assert.Equal(2u, AddBatch(group).value);
        }

        [Fact]
        public void PackedViewIdsHoldTheInstanceAndSlice()
        {
            var id = new BatchPackedCullingViewID(-42, 3);
            Assert.Equal(-42, id.GetInstanceID());
            Assert.Equal(3, id.GetSliceIndex());
            Assert.Equal(id, new BatchPackedCullingViewID(-42, 3));
            Assert.NotEqual(id, new BatchPackedCullingViewID(-42, 2));
        }

        [Fact]
        public void FilterSettingsKeepTheirFieldsAndEverySceneUntilSet()
        {
            var filter = new BatchFilterSettings { layer = 5, shadowCastingMode = ShadowCastingMode.ShadowsOnly, motionMode = MotionVectorGenerationMode.ForceNoMotion, receiveShadows = true };
            Assert.Equal(ulong.MaxValue, filter.sceneCullingMask);
            filter.sceneCullingMask = 0;
            Assert.Equal(0ul, filter.sceneCullingMask);
            Assert.Equal(ShadowCastingMode.ShadowsOnly, filter.shadowCastingMode);
            Assert.Equal(MotionVectorGenerationMode.ForceNoMotion, filter.motionMode);
            Assert.True(filter.receiveShadows);
            Assert.False(filter.staticShadowCaster);
        }

        [Fact]
        public void ACameraCullingInvokesTheCallbackWithItsContext()
        {
            var contexts = new List<(BatchCullingViewType View, int Planes, CullingSplit Split, ulong Scene, uint Layers, int View0, bool Zeroed, IntPtr User)>();
            BatchRendererGroup seen = null;
            var group = Group((g, c, o, u) =>
            {
                seen = g;
                var d = (BatchCullingOutputDrawCommands*)o.drawCommands.GetUnsafePtr();
                contexts.Add((c.viewType, c.cullingPlanes.Length, c.cullingSplits[0], c.sceneCullingMask, c.cullingLayerMask, c.viewID.GetInstanceID(),
                    o.drawCommands.Length == 1 && d->drawCommandCount == 0 && d->drawCommands == null, u));
                return default;
            }, (IntPtr)7);
            Cull();
            var (view, planes, split, scene, layers, camera, zeroed, user) = Assert.Single(contexts);
            Assert.Same(group, seen);
            Assert.Equal(BatchCullingViewType.Camera, view);
            Assert.Equal(6, planes);
            Assert.Equal(6, split.cullingPlaneCount);
            Assert.Equal(1f, split.cascadeBlendCullingFactor);
            Assert.Equal(m_Camera.nearClipPlane, split.nearPlane);
            Assert.Equal(1UL << 63, scene);
            Assert.Equal((uint)m_Camera.cullingMask, layers);
            Assert.Equal(m_Camera.GetInstanceID(), camera);
            Assert.True(zeroed);
            Assert.Equal((IntPtr)7, user);
        }

        [Fact]
        public void EnabledViewTypesAndDisposalDecideWhichGroupsAreInvoked()
        {
            int calls = 0;
            var lightOnly = Group((g, c, o, u) => { calls++; return default; });
            lightOnly.SetEnabledViewTypes(new[] { BatchCullingViewType.Light });
            var disposed = Group((g, c, o, u) => { calls++; return default; });
            disposed.Dispose();
            Cull();
            Assert.Equal(0, calls);
            lightOnly.SetEnabledViewTypes(new[] { BatchCullingViewType.Camera });
            Cull();
            Assert.Equal(1, calls);
        }

        [Fact]
        public void ACallbacksExceptionIsLoggedAndTheNextGroupStillRuns()
        {
            bool second = false;
            Group((g, c, o, u) => throw new InvalidOperationException("brg test"));
            Group((g, c, o, u) => { second = true; return default; });
            var logged = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new CapturingHandler(logged, previous);
            try { Cull(); }
            finally { Debug.unityLogger.logHandler = previous; }
            Assert.True(second);
            Assert.Contains(logged, m => m.Contains("brg test"));
        }

        sealed class CapturingHandler : ILogHandler
        {
            readonly List<string> m_Into;
            readonly ILogHandler m_Next;
            internal CapturingHandler(List<string> into, ILogHandler next) { m_Into = into; m_Next = next; }
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => m_Into.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) => m_Into.Add(exception.Message);
        }

        /// <summary>A callback emitting one draw range of the given commands over instances 0..n.</summary>
        static BatchRendererGroup.OnPerformCulling Emit(BatchFilterSettings filter, int instances, params BatchDrawCommand[] commands) => (g, c, o, u) =>
        {
            var d = (BatchCullingOutputDrawCommands*)o.drawCommands.GetUnsafePtr();
            int alignment = UnsafeUtility.AlignOf<long>();
            d->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>() * commands.Length, alignment, Allocator.TempJob);
            d->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), alignment, Allocator.TempJob);
            d->visibleInstances = (int*)UnsafeUtility.Malloc(instances * sizeof(int), alignment, Allocator.TempJob);
            for (int i = 0; i < commands.Length; i++) d->drawCommands[i] = commands[i];
            for (int i = 0; i < instances; i++) d->visibleInstances[i] = i;
            d->drawCommandCount = commands.Length;
            d->visibleInstanceCount = instances;
            d->drawRangeCount = 1;
            d->drawRanges[0] = new BatchDrawRange { drawCommandsType = BatchDrawCommandType.Direct, drawCommandsBegin = 0, drawCommandsCount = (uint)commands.Length, filterSettings = filter };
            return default;
        };

        List<FrameBuilder.DrawItem> Collect(CullingData data, FilteringSettings filtering, int mainLight = -1)
        {
            var items = new List<FrameBuilder.DrawItem>();
            long sequence = 0;
            BatchCulling.Collect(data, filtering, mainLight, items, ref sequence);
            return items;
        }

        [Fact]
        public void DrawCommandsBecomeItemsOfTheirMeshAndMaterialThroughTheListsFilters()
        {
            BatchRendererGroup group = null;
            BatchID batch = default;
            BatchMeshID mesh = default;
            BatchMaterialID material = default;
            var filter = new BatchFilterSettings { renderingLayerMask = 2, layer = 4, motionMode = MotionVectorGenerationMode.Camera };
            group = Group((g, c, o, u) => Emit(filter, 3,
                new BatchDrawCommand { batchID = batch, meshID = mesh, materialID = material, visibleOffset = 0, visibleCount = 2, flags = BatchDrawCommandFlags.FlipWinding | BatchDrawCommandFlags.LODCrossFadeValuePacked | BatchDrawCommandFlags.HasMotion },
                // An unregistered mesh, and an empty command: skipped.
                new BatchDrawCommand { batchID = batch, meshID = new BatchMeshID { value = 9 }, materialID = material, visibleOffset = 2, visibleCount = 1 },
                new BatchDrawCommand { batchID = batch, meshID = mesh, materialID = material, visibleOffset = 2, visibleCount = 0 })(g, c, o, u));
            mesh = group.RegisterMesh(m_Mesh);
            material = group.RegisterMaterial(m_Material);
            batch = AddBatch(group, ("_BaseColor", 0x80000040u));
            var data = Cull();
            var all = new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = uint.MaxValue };
            var item = Assert.Single(Collect(data, all));
            Assert.Same(m_Material, item.Material);
            Assert.Equal(m_Mesh.Sync(), item.Mesh);
            Assert.Same(group.GetBatch(batch).Properties, item.Properties);
            var header = item.Dots.Value.Header;
            Assert.Equal(2u, header.RenderingLayer);
            // FlipWinding and the cross-fade flag pass through, camera motion from the range; no
            // main light without one.
            Assert.Equal(1u | 32u | 4u, header.Flags);
            Assert.Equal(m_Mesh.bounds.center, header.Center);
            Assert.Equal(m_Mesh.bounds.extents, header.Extent);
            // The list's layer and rendering layer masks.
            Assert.Empty(Collect(data, new FilteringSettings(RenderQueueRange.all, 1 << 3) { renderingLayerMask = uint.MaxValue }));
            Assert.Empty(Collect(data, new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = 1 }));
            // A removed batch draws nothing.
            group.RemoveBatch(batch);
            Assert.Empty(Collect(data, all));
        }

        /// <summary>A command of an unregistered mesh is not drawn and logs the Editor's error,
        /// each distinct message once (tests/projects/brg errors, the Editor's log).</summary>
        [Fact]
        public void ACommandOfAnUnregisteredMeshLogsTheEditorsErrorOnce()
        {
            BatchCulling.s_InvalidIds.Clear();
            BatchID batch = default;
            BatchMaterialID material = default;
            var filter = new BatchFilterSettings { renderingLayerMask = 1 };
            var group = Group((g, c, o, u) => Emit(filter, 2,
                new BatchDrawCommand { batchID = batch, meshID = new BatchMeshID { value = 1 }, materialID = material, visibleOffset = 0, visibleCount = 1 },
                new BatchDrawCommand { batchID = batch, meshID = new BatchMeshID { value = 9 }, materialID = material, visibleOffset = 1, visibleCount = 1 })(g, c, o, u));
            Assert.Equal(1u, group.RegisterMesh(m_Mesh).value);
            material = group.RegisterMaterial(m_Material);
            batch = AddBatch(group, ("_BaseColor", 0x80000040u));
            var logged = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new CapturingHandler(logged, previous);
            try
            {
                var all = new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = uint.MaxValue };
                Assert.Single(Collect(Cull(), all));
                Assert.Single(Collect(Cull(), all));
            }
            finally { Debug.unityLogger.logHandler = previous; }
            var message = "A BatchDrawCommand was submitted with an invalid Batch, Mesh, or Material ID. This is not supported when rendering with a BatchRendererGroup or anything based on it like GPU Resident Drawer or Entities Graphics.\n"
                + $"Shader pass: <unknown>, BatchDrawCommand index: 1, (index in range: 1), BatchDrawRange index: 0 MaterialID: {material.value} (\"{m_Material.name}\"), MeshID: 9 (\"<null>\"), BatchID: {batch.value}.";
            Assert.Single(logged, l => l.Contains(message));
        }

        /// <summary>An indirect command (the GPU Resident Drawer's under occlusion culling) is an
        /// item of its mesh and material whose properties add its visible instance buffer as
        /// unity_DOTSInstancing_IndirectInstanceVisibility; the batch's own stay as they are.</summary>
        [Fact]
        public void IndirectCommandsBecomeItemsWithTheirVisibleInstanceBuffer()
        {
            var visible = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 4, 4);
            var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, 4);
            BatchRendererGroup group = null;
            var filter = new BatchFilterSettings { renderingLayerMask = uint.MaxValue };
            group = Group((g, c, o, u) =>
            {
                var d = (BatchCullingOutputDrawCommands*)o.drawCommands.GetUnsafePtr();
                int alignment = UnsafeUtility.AlignOf<long>();
                d->indirectDrawCommands = (BatchDrawCommandIndirect*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommandIndirect>(), alignment, Allocator.TempJob);
                d->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), alignment, Allocator.TempJob);
                d->indirectDrawCommands[0] = new BatchDrawCommandIndirect
                {
                    batchID = new BatchID { value = 1 }, meshID = new BatchMeshID { value = 1 }, materialID = new BatchMaterialID { value = 1 },
                    topology = MeshTopology.Triangles, visibleOffset = 3, visibleInstancesBufferHandle = visible.bufferHandle,
                    indirectArgsBufferHandle = args.bufferHandle, flags = BatchDrawCommandFlags.LODCrossFadeValuePacked,
                };
                d->indirectDrawCommandCount = 1;
                d->drawRangeCount = 1;
                d->drawRanges[0] = new BatchDrawRange { drawCommandsType = BatchDrawCommandType.Indirect, drawCommandsBegin = 0, drawCommandsCount = 1, filterSettings = filter };
                return default;
            });
            group.RegisterMesh(m_Mesh);
            group.RegisterMaterial(m_Material);
            var batch = AddBatch(group);
            try
            {
                var item = Assert.Single(Collect(Cull(), new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = uint.MaxValue }));
                Assert.True(item.Dots.Value.Indirect);
                Assert.Same(m_Material, item.Material);
                Assert.Equal(visible.bufferHandle, item.Properties[BatchCulling.IndirectVisibilityId]);
                Assert.False(group.GetBatch(batch).Properties.ContainsKey(BatchCulling.IndirectVisibilityId));
                // The cross-fade flag, and camera motion from the range's default motion mode.
                Assert.Equal(32u | 4u, item.Dots.Value.Header.Flags);
                // Kept between cullings: the same dictionary while the batch is unchanged.
                var again = Assert.Single(Collect(Cull(), new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = uint.MaxValue }));
                Assert.Same(item.Properties, again.Properties);
            }
            finally
            {
                visible.Release();
                args.Release();
            }
        }

        [Fact]
        public void ShadowOnlyRangesAreLeftOutOfCameraLists()
        {
            BatchRendererGroup group = null;
            var filter = new BatchFilterSettings { renderingLayerMask = uint.MaxValue, shadowCastingMode = ShadowCastingMode.ShadowsOnly };
            group = Group((g, c, o, u) => Emit(filter, 1, new BatchDrawCommand { batchID = new BatchID { value = 1 }, meshID = new BatchMeshID { value = 1 }, materialID = new BatchMaterialID { value = 1 }, visibleCount = 1 })(g, c, o, u));
            group.RegisterMesh(m_Mesh);
            group.RegisterMaterial(m_Material);
            AddBatch(group);
            Assert.Empty(Collect(Cull(), new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = uint.MaxValue }));
        }

        /// <summary>The main light flag follows unity_LightData.z's rule: the light's culling mask
        /// has the range's layer and, for a lightmapped command, its direct light is not baked.</summary>
        [Fact]
        public void TheMainLightFlagFollowsTheLightsCullingMask()
        {
            var lightObject = new GameObject("brg sun");
            m_Objects.Add(lightObject);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            BatchRendererGroup group = null;
            var filter = new BatchFilterSettings { renderingLayerMask = uint.MaxValue, layer = 6, motionMode = MotionVectorGenerationMode.Object };
            group = Group((g, c, o, u) => Emit(filter, 1, new BatchDrawCommand { batchID = new BatchID { value = 1 }, meshID = new BatchMeshID { value = 1 }, materialID = new BatchMaterialID { value = 1 }, visibleCount = 1 })(g, c, o, u));
            group.RegisterMesh(m_Mesh);
            group.RegisterMaterial(m_Material);
            AddBatch(group);
            var all = new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = uint.MaxValue };
            var data = Cull();
            int sun = Array.FindIndex(data.Lights.ToArray(), l => l.light == light);
            Assert.True(sun >= 0);
            Assert.Equal(16u, Assert.Single(Collect(data, all, sun)).Dots.Value.Header.Flags);
            Assert.Equal(0u, Assert.Single(Collect(data, all, -1)).Dots.Value.Header.Flags);
            light.cullingMask = ~(1 << 6);
            Assert.Equal(0u, Assert.Single(Collect(Cull(), all, sun)).Dots.Value.Header.Flags);
        }

        /// <summary>What is not drawn as Unity draws it is said once: a lightmapped command draws
        /// without its lightmap, a cross-fade keyword command without the keyword, and an output
        /// that counts commands it did not allocate is not drawn at all.</summary>
        [Fact]
        public void UnsupportedCommandPartsAreWarnedOnce()
        {
            uint warned = BatchCulling.s_Warned;
            BatchCulling.s_Warned = 0;
            int mode = 0;
            var group = Group((g, c, o, u) =>
            {
                if (mode == 1)
                {
                    var d = (BatchCullingOutputDrawCommands*)o.drawCommands.GetUnsafePtr();
                    d->drawCommandCount = 1;
                    d->visibleInstanceCount = 1;
                    return default;
                }
                return Emit(new BatchFilterSettings { renderingLayerMask = uint.MaxValue }, 1, new BatchDrawCommand
                {
                    batchID = new BatchID { value = 1 }, meshID = new BatchMeshID { value = 1 }, materialID = new BatchMaterialID { value = 1 }, visibleCount = 1,
                    flags = BatchDrawCommandFlags.IsLightMapped | BatchDrawCommandFlags.LODCrossFadeKeyword,
                })(g, c, o, u);
            });
            group.RegisterMesh(m_Mesh);
            group.RegisterMaterial(m_Material);
            AddBatch(group);
            var all = new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = uint.MaxValue };
            var logged = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new CapturingHandler(logged, previous);
            try
            {
                Assert.Single(Collect(Cull(), all));
                Assert.Single(Collect(Cull(), all));
                Assert.Equal(1, logged.FindAll(m => m.Contains("IsLightMapped")).Count);
                Assert.Equal(1, logged.FindAll(m => m.Contains("LODCrossFadeKeyword")).Count);
                mode = 1;
                Assert.Empty(Collect(Cull(), all));
                Assert.Contains(logged, m => m.Contains("without allocating"));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                BatchCulling.s_Warned = warned;
            }
        }

        /// <summary>A culling's shadow caster culling (CullShadowCasters) invokes the groups that
        /// take light views once per light with splits, as the Editor gives the context
        /// (tests/projects/brg shadows): every split's planes in order, then the camera's frustum
        /// as receiver planes with the far one at the shadow distance; a light whose every split
        /// is excluded is not culled, and one that does not cull by the view frustum has no
        /// receiver planes.</summary>
        [Fact]
        public void ShadowCullingInvokesLightViewsWithTheSplitsAndReceiverPlanes()
        {
            var lightObject = new GameObject("brg shadow sun");
            m_Objects.Add(lightObject);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Hard;
            light.cullingMask = ~(1 << 3);
            var contexts = new List<(BatchCullingViewType View, Plane[] Planes, CullingSplit[] Splits, int ReceiverOffset, int Receivers, uint Layers, int ViewId, Matrix4x4 LocalToWorld, BatchCullingFlags Flags, ushort Excluded, BatchCullingProjectionType Projection)>();
            Group((g, c, o, u) =>
            {
                if (c.viewType == BatchCullingViewType.Light)
                    contexts.Add((c.viewType, c.cullingPlanes.ToArray(), c.cullingSplits.ToArray(), c.receiverPlaneOffset, c.receiverPlaneCount, c.cullingLayerMask,
                        c.viewID.GetInstanceID(), c.localToWorldMatrix, c.cullingFlags, c.splitExclusionMask, c.projectionType));
                return default;
            });
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            parameters.shadowDistance = 50f;
            var data = Culling.Get(Culling.Cull(ref parameters));
            int sun = Array.FindIndex(data.Lights.ToArray(), l => l.light == light);
            Assert.True(sun >= 0);
            ShadowSplitData Split(int planes, float offset)
            {
                var split = new ShadowSplitData { cullingPlaneCount = planes, cullingSphere = new Vector4(1f, 2f, 3f, 4f + offset), cullingNearPlane = 0.5f + offset, shadowCascadeBlendCullingFactor = 0.75f };
                for (int i = 0; i < planes; i++) split.SetCullingPlane(i, new Plane(Vector3.up, offset + i));
                return split;
            }
            var splits = new NativeArray<ShadowSplitData>(2, Allocator.Temp);
            splits[0] = Split(3, 0f);
            splits[1] = Split(2, 10f);
            var infos = new NativeArray<LightShadowCasterCullingInfo>(data.Lights.Length, Allocator.Temp);
            infos[sun] = new LightShadowCasterCullingInfo { splitRange = new RangeInt(0, 2), projectionType = BatchCullingProjectionType.Orthographic, splitExclusionMask = 1 };
            try
            {
                new ScriptableRenderContext().CullShadowCasters(new CullingResults { ptr = Culling.Cull(ref parameters) }, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = infos });
                var c = Assert.Single(contexts);
                Assert.Equal(3 + 2 + 6, c.Planes.Length);
                Assert.Equal(new[] { 0, 3 }, Array.ConvertAll(c.Splits, s => s.cullingPlaneOffset));
                Assert.Equal(new[] { 3, 2 }, Array.ConvertAll(c.Splits, s => s.cullingPlaneCount));
                Assert.Equal(new Vector3(1f, 2f, 3f), c.Splits[1].sphereCenter);
                Assert.Equal(14f, c.Splits[1].sphereRadius);
                Assert.Equal(10.5f, c.Splits[1].nearPlane);
                Assert.Equal(0.75f, c.Splits[0].cascadeBlendCullingFactor);
                Assert.Equal(11f, c.Planes[4].distance);
                Assert.Equal((5, 6), (c.ReceiverOffset, c.Receivers));
                // The camera's planes, the far one pulled in to the shadow distance.
                for (int i = 0; i < 5; i++) Assert.Equal(parameters.GetCullingPlane(i), c.Planes[5 + i]);
                var far = parameters.GetCullingPlane(5);
                Assert.Equal(far.normal, c.Planes[10].normal);
                Assert.Equal(far.distance - (m_Camera.farClipPlane - 50f), c.Planes[10].distance, 3);
                Assert.Equal((uint)m_Camera.cullingMask & (uint)light.cullingMask, c.Layers);
                Assert.Equal(light.GetInstanceID(), c.ViewId);
                Assert.Equal(light.transform.localToWorldMatrix, c.LocalToWorld);
                Assert.Equal(BatchCullingFlags.None, c.Flags);
                Assert.Equal((ushort)1, c.Excluded);
                Assert.Equal(BatchCullingProjectionType.Orthographic, c.Projection);

                // Without the view frustum: no receivers. Every split excluded: no culling.
                contexts.Clear();
                light.useViewFrustumForShadowCasterCull = false;
                new ScriptableRenderContext().CullShadowCasters(new CullingResults { ptr = Culling.Cull(ref parameters) }, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = infos });
                Assert.Equal((0, 0, 5), (Assert.Single(contexts).ReceiverOffset, contexts[0].Receivers, contexts[0].Planes.Length));
                contexts.Clear();
                infos[sun] = new LightShadowCasterCullingInfo { splitRange = new RangeInt(0, 2), splitExclusionMask = 3 };
                new ScriptableRenderContext().CullShadowCasters(new CullingResults { ptr = Culling.Cull(ref parameters) }, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = infos });
                Assert.Empty(contexts);

                // A light whose direct light is in the lightmaps leaves lightmapped casters out.
                infos[sun] = new LightShadowCasterCullingInfo { splitRange = new RangeInt(0, 1) };
                light.bakingOutput = new LightBakingOutput { isBaked = true, lightmapBakeType = LightmapBakeType.Mixed, mixedLightingMode = MixedLightingMode.Subtractive };
                new ScriptableRenderContext().CullShadowCasters(new CullingResults { ptr = Culling.Cull(ref parameters) }, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = infos });
                Assert.Equal(BatchCullingFlags.CullLightmappedShadowCasters, Assert.Single(contexts).Flags);
            }
            finally
            {
                splits.Dispose();
                infos.Dispose();
            }
        }

        /// <summary>A light's shadow caster list draws its light culling's commands of the ranges
        /// that cast shadows (ShadowsOnly ones too), none of a camera culling's or another light's.</summary>
        [Fact]
        public void ShadowListsTakeTheirLightsCastingRanges()
        {
            var lightObject = new GameObject("brg caster sun");
            m_Objects.Add(lightObject);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Hard;
            var casting = ShadowCastingMode.ShadowsOnly;
            var group = Group((g, c, o, u) => Emit(new BatchFilterSettings { renderingLayerMask = uint.MaxValue, shadowCastingMode = casting }, 1,
                new BatchDrawCommand { batchID = new BatchID { value = 1 }, meshID = new BatchMeshID { value = 1 }, materialID = new BatchMaterialID { value = 1 }, visibleCount = 1 })(g, c, o, u));
            group.RegisterMesh(m_Mesh);
            group.RegisterMaterial(m_Material);
            AddBatch(group);
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            var results = new CullingResults { ptr = Culling.Cull(ref parameters) };
            var data = results.Data;
            int sun = Array.FindIndex(data.Lights.ToArray(), l => l.light == light);
            var splits = new NativeArray<ShadowSplitData>(1, Allocator.Temp);
            var infos = new NativeArray<LightShadowCasterCullingInfo>(data.Lights.Length, Allocator.Temp);
            infos[sun] = new LightShadowCasterCullingInfo { splitRange = new RangeInt(0, 1) };
            try
            {
                new ScriptableRenderContext().CullShadowCasters(results, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = infos });
                var all = new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = uint.MaxValue };
                var items = new List<FrameBuilder.DrawItem>();
                long sequence = 0;
                BatchCulling.CollectShadows(data, all, sun, items, ref sequence);
                Assert.Single(items);
                Assert.Equal(ShadowCastingMode.ShadowsOnly, items[0].Lighting.Cast);
                items.Clear();
                BatchCulling.CollectShadows(data, all, sun + 1, items, ref sequence);
                Assert.Empty(items);
                // The camera list leaves the shadow-only range out.
                Assert.Empty(Collect(data, all));
                casting = ShadowCastingMode.Off;
                var off = new CullingResults { ptr = Culling.Cull(ref parameters) };
                new ScriptableRenderContext().CullShadowCasters(off, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = infos });
                BatchCulling.CollectShadows(off.Data, all, sun, items, ref sequence);
                Assert.Empty(items);
            }
            finally
            {
                splits.Dispose();
                infos.Dispose();
            }
        }

        /// <summary>The Scripting API (BatchCullingContext.cullingLayerMask): a draw command on a
        /// layer a camera culling's mask leaves out is discarded; a light culling's casters are
        /// drawn whatever their layer (measured, tests/projects/brg shadows).</summary>
        [Fact]
        public void CameraCullingsDiscardRangesOnLayersTheyLeaveOut()
        {
            var lightObject = new GameObject("brg masked sun");
            m_Objects.Add(lightObject);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Hard;
            var filter = new BatchFilterSettings { renderingLayerMask = uint.MaxValue, layer = 3, shadowCastingMode = ShadowCastingMode.On };
            var group = Group((g, c, o, u) => Emit(filter, 1,
                new BatchDrawCommand { batchID = new BatchID { value = 1 }, meshID = new BatchMeshID { value = 1 }, materialID = new BatchMaterialID { value = 1 }, visibleCount = 1 })(g, c, o, u));
            group.RegisterMesh(m_Mesh);
            group.RegisterMaterial(m_Material);
            AddBatch(group);
            var all = new FilteringSettings(RenderQueueRange.all) { renderingLayerMask = uint.MaxValue };
            Assert.Single(Collect(Cull(), all));
            m_Camera.cullingMask = ~(1 << 3);
            Assert.Empty(Collect(Cull(), all));
            m_Camera.cullingMask = -1;
            light.cullingMask = ~(1 << 3);
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            var results = new CullingResults { ptr = Culling.Cull(ref parameters) };
            int sun = Array.FindIndex(results.Data.Lights.ToArray(), l => l.light == light);
            var splits = new NativeArray<ShadowSplitData>(1, Allocator.Temp);
            var infos = new NativeArray<LightShadowCasterCullingInfo>(results.Data.Lights.Length, Allocator.Temp);
            infos[sun] = new LightShadowCasterCullingInfo { splitRange = new RangeInt(0, 1) };
            try
            {
                new ScriptableRenderContext().CullShadowCasters(results, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = infos });
                var items = new List<FrameBuilder.DrawItem>();
                long sequence = 0;
                BatchCulling.CollectShadows(results.Data, all, sun, items, ref sequence);
                Assert.Single(items);
                // The camera draws it too; a camera culling that leaves the layer out does not, but
                // its light culling still casts it.
                Assert.Single(Collect(results.Data, all));
                m_Camera.cullingMask = ~(1 << 3);
                Assert.True(m_Camera.TryGetCullingParameters(out parameters));
                results = new CullingResults { ptr = Culling.Cull(ref parameters) };
                new ScriptableRenderContext().CullShadowCasters(results, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = infos });
                Assert.Empty(Collect(results.Data, all));
                items.Clear();
                BatchCulling.CollectShadows(results.Data, all, sun, items, ref sequence);
                Assert.Single(items);
            }
            finally
            {
                splits.Dispose();
                infos.Dispose();
            }
        }

        [Fact]
        public void TheFramesEndFreesTheArraysTheCallbackAllocated()
        {
            BatchCulling.Output kept = null;
            var group = Group((g, c, o, u) => Emit(new BatchFilterSettings(), 4, new BatchDrawCommand { visibleCount = 4 })(g, c, o, u));
            var data = Cull();
            kept = Assert.Single(data.Batches);
            Assert.True(kept.Drawn.drawCommands != null);
            Culling.EndFrame();
            Assert.True(kept.Drawn.drawCommands == null && kept.Drawn.visibleInstances == null && kept.Drawn.drawRanges == null);
            Assert.Null(kept.Group);
            Assert.Empty(data.Batches);
        }

        /// <summary>A camera's near and far culling planes lie at its clip distances along its
        /// forward axis (the Editor's far plane is exact: tests/projects/brg plane5), the side planes
        /// from its matrix.</summary>
        [Fact]
        public void TheCamerasNearAndFarPlanesAreExact()
        {
            Assert.True(m_Camera.TryGetCullingParameters(out var parameters));
            var forward = m_Camera.transform.forward;
            var far = parameters.GetCullingPlane(5);
            var near = parameters.GetCullingPlane(4);
            var farPoint = m_Camera.transform.position + forward * m_Camera.farClipPlane;
            var nearPoint = m_Camera.transform.position + forward * m_Camera.nearClipPlane;
            Assert.Equal(0f, far.GetDistanceToPoint(farPoint), 3);
            Assert.Equal(0f, near.GetDistanceToPoint(nearPoint), 4);
            Assert.True(far.GetDistanceToPoint(m_Camera.transform.position) > 0f);
            Assert.True(near.GetDistanceToPoint(farPoint) > 0f);
            // A custom culling matrix keeps the planes it gives.
            m_Camera.cullingMatrix = Matrix4x4.Ortho(-1, 1, -1, 1, 0.5f, 20f) * m_Camera.worldToCameraMatrix;
            Assert.True(m_Camera.TryGetCullingParameters(out parameters));
            var ortho = parameters.GetCullingPlane(5);
            Assert.Equal(0f, ortho.GetDistanceToPoint(m_Camera.transform.position + forward * 20f), 2);
        }
    }
}
