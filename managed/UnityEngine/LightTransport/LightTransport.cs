using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;

namespace UnityEngine.LightTransport
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe partial class BakeProgressState : IDisposable
    {
        internal IntPtr m_Ptr;
        internal bool m_OwnsPtr;
        long m_TotalSteps;
        long m_CompletedSteps;
        int m_Cancelled;

        public BakeProgressState()
        {
            m_OwnsPtr = true;
        }

        public void Dispose()
        {
            m_Ptr = IntPtr.Zero;
            m_OwnsPtr = false;
        }

        public void Cancel() => Interlocked.Exchange(ref m_Cancelled, 1);

        public float Progress()
        {
            long total = Interlocked.Read(ref m_TotalSteps);
            if (total <= 0) return 0f;
            long done = Interlocked.Read(ref m_CompletedSteps);
            return Mathf.Clamp01((float)((double)done / total));
        }

        public void SetTotalWorkSteps(ulong total) =>
            Interlocked.Exchange(ref m_TotalSteps, (long)Math.Min(total, (ulong)long.MaxValue));

        public void IncrementCompletedWorkSteps(ulong steps) =>
            Interlocked.Add(ref m_CompletedSteps, (long)Math.Min(steps, (ulong)long.MaxValue));

        public bool WasCancelled() => Volatile.Read(ref m_Cancelled) != 0;
    }

    public unsafe partial struct BufferID : IEquatable<BufferID>
    {
        public ulong Value;

        public BufferID(ulong value)
        {
            Value = value;
        }

        public BufferSlice<T> Slice<T>(ulong offset = 0UL) where T : struct =>
            new BufferSlice<T>(this, offset);

        public override int GetHashCode() => Value.GetHashCode();
        public bool Equals(BufferID other) => Value == other.Value;
        public override bool Equals(object obj) => obj is BufferID other && Equals(other);
        public static bool operator ==(BufferID a, BufferID b) => a.Value == b.Value;
        public static bool operator !=(BufferID a, BufferID b) => a.Value != b.Value;
    }

    public unsafe partial struct BufferSlice<T> : IEquatable<BufferSlice<T>> where T : struct
    {
        public BufferID Id;
        public ulong Offset;

        public BufferSlice(BufferID id, ulong offset)
        {
            Id = id;
            Offset = offset;
        }

        public BufferSlice<U> SafeReinterpret<U>() where U : struct
        {
            ulong srcSize = (ulong)UnsafeUtility.SizeOf<T>();
            ulong dstSize = (ulong)UnsafeUtility.SizeOf<U>();
            ulong byteOffset = Offset * srcSize;
            if (dstSize == 0 || (byteOffset % dstSize) != 0)
                throw new InvalidOperationException($"Cannot safely reinterpret BufferSlice<{typeof(T).Name}> (offset {Offset}, size {srcSize}) as {typeof(U).Name} (size {dstSize}).");
            return new BufferSlice<U>(Id, byteOffset / dstSize);
        }

        public BufferSlice<U> UnsafeReinterpret<U>() where U : struct
        {
            ulong srcSize = (ulong)UnsafeUtility.SizeOf<T>();
            ulong dstSize = (ulong)UnsafeUtility.SizeOf<U>();
            ulong byteOffset = Offset * srcSize;
            return new BufferSlice<U>(Id, dstSize == 0 ? 0UL : byteOffset / dstSize);
        }

        public override int GetHashCode() => HashCode.Combine(Id, Offset);
        public bool Equals(BufferSlice<T> other) => Id == other.Id && Offset == other.Offset;
        public override bool Equals(object obj) => obj is BufferSlice<T> other && Equals(other);
        public static bool operator ==(BufferSlice<T> a, BufferSlice<T> b) => a.Equals(b);
        public static bool operator !=(BufferSlice<T> a, BufferSlice<T> b) => !a.Equals(b);
    }

    public unsafe partial struct EventID : IEquatable<EventID>
    {
        public ulong Value;

        public EventID(ulong value)
        {
            Value = value;
        }

        public override int GetHashCode() => Value.GetHashCode();
        public bool Equals(EventID other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EventID other && Equals(other);
        public static bool operator ==(EventID a, EventID b) => a.Value == b.Value;
        public static bool operator !=(EventID a, EventID b) => a.Value != b.Value;
    }

    public unsafe partial interface IDeviceContext : IDisposable
    {
        bool Initialize();
        BufferID CreateBuffer(ulong count, ulong stride);
        void DestroyBuffer(BufferID id);
        void WriteBuffer<T>(BufferSlice<T> dst, NativeArray<T> src) where T : struct;
        void ReadBuffer<T>(BufferSlice<T> src, NativeArray<T> dst) where T : struct;
        void WriteBuffer<T>(BufferSlice<T> dst, NativeArray<T> src, EventID id) where T : struct;
        void ReadBuffer<T>(BufferSlice<T> src, NativeArray<T> dst, EventID id) where T : struct;
        EventID CreateEvent();
        void DestroyEvent(EventID id);
        bool IsCompleted(EventID id);
        bool Wait(EventID id);
        bool Flush();
    }

    public unsafe partial interface IWorld : IDisposable
    {
    }

    public unsafe partial interface IProbeIntegrator : IDisposable
    {
        void Prepare(IDeviceContext context, IWorld world, BufferSlice<Vector3> positions, float pushoff, int bounceCount);
        void SetProgressReporter(BakeProgressState progress);
        Result IntegrateDirectRadiance(IDeviceContext context, int positionOffset, int positionCount, int sampleCount, bool ignoreEnvironment, BufferSlice<SphericalHarmonicsL2> radianceEstimateOut);
        Result IntegrateIndirectRadiance(IDeviceContext context, int positionOffset, int positionCount, int sampleCount, bool ignoreEnvironment, BufferSlice<SphericalHarmonicsL2> radianceEstimateOut);
        Result IntegrateValidity(IDeviceContext context, int positionOffset, int positionCount, int sampleCount, BufferSlice<float> validityEstimateOut);
        Result IntegrateOcclusion(IDeviceContext context, int positionOffset, int positionCount, int sampleCount, int maxLightsPerProbe, BufferSlice<int> perProbeLightIndices, BufferSlice<float> probeOcclusionEstimateOut);

        public enum ResultType : uint
        {
            Success = 0U,
            Cancelled = 1U,
            JobFailed = 2U,
            OutOfMemory = 3U,
            InvalidInput = 4U,
            LowLevelAPIFailure = 5U,
            IOFailed = 6U,
            Undefined = 7U,
        }

        public unsafe partial struct Result
        {
            public ResultType type;
            public string message;

            public Result(ResultType _type, string _message)
            {
                type = _type;
                message = _message;
            }

            public override string ToString() =>
                string.IsNullOrEmpty(message) ? type.ToString() : $"{type}: {message}";
        }
    }

    /// <summary>Shared CPU-backed buffer and event store for <see cref="RadeonRaysContext"/> and
    /// <see cref="ReferenceContext"/>.</summary>
    internal sealed class CpuDeviceBufferStore
    {
        readonly object m_Lock = new object();
        readonly Dictionary<ulong, byte[]> m_Buffers = new Dictionary<ulong, byte[]>();
        readonly HashSet<ulong> m_Events = new HashSet<ulong>();
        ulong m_NextBufferId = 1;
        ulong m_NextEventId = 1;
        bool m_Initialized;

        internal bool Initialize()
        {
            lock (m_Lock) m_Initialized = true;
            return true;
        }

        internal void Dispose()
        {
            lock (m_Lock)
            {
                m_Buffers.Clear();
                m_Events.Clear();
                m_Initialized = false;
            }
        }

        internal BufferID CreateBuffer(ulong count, ulong stride)
        {
            ulong bytes = checked(count * stride);
            if (bytes > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(count), $"Buffer size {bytes} bytes exceeds supported range.");
            var data = new byte[bytes];
            lock (m_Lock)
            {
                ulong id = m_NextBufferId++;
                m_Buffers[id] = data;
                return new BufferID(id);
            }
        }

        internal void DestroyBuffer(BufferID id)
        {
            lock (m_Lock) m_Buffers.Remove(id.Value);
        }

        internal bool TryGetBuffer(BufferID id, out byte[] data)
        {
            lock (m_Lock) return m_Buffers.TryGetValue(id.Value, out data);
        }

        internal byte[] GetBuffer(BufferID id)
        {
            if (!TryGetBuffer(id, out var data))
                throw new ArgumentException($"Unknown LightTransport buffer ID {id.Value}.", nameof(id));
            return data;
        }

        internal static bool TryByteRange(byte[] buf, ulong elemOffset, ulong elemCount, ulong elemBytes, out int startByte, out int lengthBytes)
        {
            startByte = 0;
            lengthBytes = 0;
            try
            {
                ulong start = checked(elemOffset * elemBytes);
                ulong length = checked(elemCount * elemBytes);
                ulong end = checked(start + length);
                if (end > (ulong)buf.Length) return false;
                startByte = (int)start;
                lengthBytes = (int)length;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        internal unsafe void WriteBuffer<T>(BufferSlice<T> dst, NativeArray<T> src) where T : struct
        {
            if (src.Length == 0) return;
            var data = GetBuffer(dst.Id);
            ulong elemSize = (ulong)UnsafeUtility.SizeOf<T>();
            ulong byteOffset = checked(dst.Offset * elemSize);
            ulong byteCount = checked((ulong)src.Length * elemSize);
            if (checked(byteOffset + byteCount) > (ulong)data.Length)
                throw new ArgumentOutOfRangeException(nameof(dst), $"Write of {byteCount} bytes at offset {byteOffset} exceeds buffer size {data.Length}.");
            void* srcPtr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(src);
            fixed (byte* dstPtr = &data[byteOffset])
                UnsafeUtility.MemCpy(dstPtr, srcPtr, (long)byteCount);
        }

        internal unsafe void ReadBuffer<T>(BufferSlice<T> src, NativeArray<T> dst) where T : struct
        {
            if (dst.Length == 0) return;
            var data = GetBuffer(src.Id);
            ulong elemSize = (ulong)UnsafeUtility.SizeOf<T>();
            ulong byteOffset = checked(src.Offset * elemSize);
            ulong byteCount = checked((ulong)dst.Length * elemSize);
            if (checked(byteOffset + byteCount) > (ulong)data.Length)
                throw new ArgumentOutOfRangeException(nameof(src), $"Read of {byteCount} bytes at offset {byteOffset} exceeds buffer size {data.Length}.");
            void* dstPtr = NativeArrayUnsafeUtility.GetUnsafePtr(dst);
            fixed (byte* srcPtr = &data[byteOffset])
                UnsafeUtility.MemCpy(dstPtr, srcPtr, (long)byteCount);
        }

        internal EventID CreateEvent()
        {
            lock (m_Lock)
            {
                ulong id = m_NextEventId++;
                m_Events.Add(id);
                return new EventID(id);
            }
        }

        internal void DestroyEvent(EventID id)
        {
            lock (m_Lock) m_Events.Remove(id.Value);
        }

        internal bool IsCompleted(EventID id)
        {
            lock (m_Lock) return m_Initialized && m_Events.Contains(id.Value);
        }

        internal bool Wait(EventID id) => IsCompleted(id);

        internal bool Flush()
        {
            lock (m_Lock) return m_Initialized;
        }
        internal static CpuDeviceBufferStore Of(IDeviceContext context) =>
            context is RadeonRaysContext rr ? rr.m_Store
            : context is ReferenceContext rc ? rc.m_Store
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe partial class RadeonRaysContext : IDeviceContext, IDisposable
    {
        internal IntPtr m_Ptr;
        internal bool m_OwnsPtr;
        internal readonly CpuDeviceBufferStore m_Store = new CpuDeviceBufferStore();

        public RadeonRaysContext() { m_OwnsPtr = true; }
        public RadeonRaysContext(IntPtr ptr) { m_Ptr = ptr; }
        public void Dispose() { m_Store.Dispose(); m_Ptr = IntPtr.Zero; m_OwnsPtr = false; }
        public bool Initialize() => m_Store.Initialize();
        public BufferID CreateBuffer(ulong count, ulong stride) => m_Store.CreateBuffer(count, stride);
        public void DestroyBuffer(BufferID id) => m_Store.DestroyBuffer(id);
        public void ReadBuffer<T>(BufferSlice<T> src, NativeArray<T> dst) where T : struct => m_Store.ReadBuffer(src, dst);
        public void ReadBuffer<T>(BufferSlice<T> src, NativeArray<T> dst, EventID id) where T : struct => m_Store.ReadBuffer(src, dst);
        public void WriteBuffer<T>(BufferSlice<T> dst, NativeArray<T> src) where T : struct => m_Store.WriteBuffer(dst, src);
        public void WriteBuffer<T>(BufferSlice<T> dst, NativeArray<T> src, EventID id) where T : struct => m_Store.WriteBuffer(dst, src);
        public EventID CreateEvent() => m_Store.CreateEvent();
        public void DestroyEvent(EventID id) => m_Store.DestroyEvent(id);
        public bool IsCompleted(EventID id) => m_Store.IsCompleted(id);
        public bool Wait(EventID id) => m_Store.Wait(id);
        public bool Flush() => m_Store.Flush();
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe partial class ReferenceContext : IDeviceContext, IDisposable
    {
        internal readonly CpuDeviceBufferStore m_Store = new CpuDeviceBufferStore();

        public ReferenceContext() { }
        public void Dispose() => m_Store.Dispose();
        public bool Initialize() => m_Store.Initialize();
        public BufferID CreateBuffer(ulong count, ulong stride) => m_Store.CreateBuffer(count, stride);
        public void DestroyBuffer(BufferID id) => m_Store.DestroyBuffer(id);
        public void ReadBuffer<T>(BufferSlice<T> src, NativeArray<T> dst) where T : struct => m_Store.ReadBuffer(src, dst);
        public void ReadBuffer<T>(BufferSlice<T> src, NativeArray<T> dst, EventID id) where T : struct => m_Store.ReadBuffer(src, dst);
        public void WriteBuffer<T>(BufferSlice<T> dst, NativeArray<T> src) where T : struct => m_Store.WriteBuffer(dst, src);
        public void WriteBuffer<T>(BufferSlice<T> dst, NativeArray<T> src, EventID id) where T : struct => m_Store.WriteBuffer(dst, src);
        public EventID CreateEvent() => m_Store.CreateEvent();
        public void DestroyEvent(EventID id) => m_Store.DestroyEvent(id);
        public bool IsCompleted(EventID id) => m_Store.IsCompleted(id);
        public bool Wait(EventID id) => m_Store.Wait(id);
        public bool Flush() => m_Store.Flush();
    }

    public unsafe partial class RadeonRaysWorld : IWorld, IDisposable
    {
        internal ulong WorldId;

        public RadeonRaysWorld() { }

        internal void SetWorldId(ulong id)
        {
            ReleaseWorld();
            WorldId = id;
        }

        void ReleaseWorld()
        {
            ulong id = Interlocked.Exchange(ref WorldId, 0);
            if (id != 0 && Native.IsReady)
                Native.Call("light-transport-release", JsonSerializer.Serialize(new { worldId = id }));
        }

        public void Dispose()
        {
            ReleaseWorld();
            GC.SuppressFinalize(this);
        }

        ~RadeonRaysWorld() => ReleaseWorld();
    }

    public unsafe static partial class InputExtraction
    {
        const uint ContributeGI = 1;
        const int MetaImageSize = 64;
        const int SkyFaceSize = 128;
        const string BlueNoiseData = "Resources/GI/SamplingData/bluenoisedata";

        static readonly Func<Material, bool> s_Emits = BakedAtlas.Emits;
        static readonly Action<string, string> s_Warn = (_, msg) => Debug.LogWarning("Unbound: " + msg);

        internal struct ExtractedLightMeta
        {
            public int Kind;
            public int Mode;
            public Vector3 Position;
            public float Range;
            public int ShadowmaskChannel;
        }

        public unsafe partial class BakeInput
        {
            internal string PopulateJson;
            internal ExtractedLightMeta[] Lights = Array.Empty<ExtractedLightMeta>();

            internal BakeInput() { }
        }

        public static bool ExtractFromScene(out BakeInput bakeInput)
        {
            bakeInput = null;
            if (!Native.IsReady) return false;
            string blueNoise = Path.Combine(UnityEditor.EditorApplication.applicationContentsPath ?? "", BlueNoiseData);
            if (!File.Exists(blueNoise))
            {
                Debug.LogError($"Unbound: Unity installation has no {BlueNoiseData} ({blueNoise}); LightTransport probe baking requires it.");
                return false;
            }
            var settings = UnityEditor.Lightmapping.GetLightingSettingsOrDefaultsFallback();
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            var meshes = new List<object>();
            var meshIndex = new Dictionary<Mesh, int>();
            var renderers = new List<object>();
            var withUVs = new Dictionary<Mesh, Mesh>();

            var allRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            try
            {
                using (Mesh.EngineReads())
                {
                    foreach (var renderer in allRenderers)
                    {
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        if ((renderer.gameObject.m_StaticEditorFlags & ContributeGI) == 0) continue;
                        var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null || mesh.vertexCount == 0 || mesh.normals.Length != mesh.vertexCount) continue;
                        bool allTriangles = true;
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                            if (mesh.GetTopology(sub) != MeshTopology.Triangles) { allTriangles = false; break; }
                        if (!allTriangles) continue;

                        if (!meshIndex.TryGetValue(mesh, out int index))
                        {
                            meshIndex[mesh] = index = meshes.Count;
                            var submeshes = new string[mesh.subMeshCount];
                            for (int sub = 0; sub < submeshes.Length; sub++)
                                submeshes[sub] = BakedAtlas.Encoded<int>(mesh.GetTriangles(sub));
                            meshes.Add(new
                            {
                                positions = BakedAtlas.Encoded<Vector3>(mesh.vertices),
                                normals = BakedAtlas.Encoded<Vector3>(mesh.normals),
                                uv = BakedAtlas.Encoded<Vector2>(mesh.uv),
                                uv2 = BakedAtlas.Encoded<Vector2>(mesh.uv2),
                                submeshes,
                            });
                        }

                        var uvMesh = BakedAtlas.BakedUVs(mesh, withUVs);
                        var uvs = uvMesh.uv2.Length == uvMesh.vertexCount ? uvMesh.uv2 : uvMesh.uv;
                        float minU = float.PositiveInfinity, minV = float.PositiveInfinity;
                        float maxU = float.NegativeInfinity, maxV = float.NegativeInfinity;
                        for (int i = 0; i < uvs.Length; i++)
                        {
                            float u = uvs[i].x, v = uvs[i].y;
                            if (float.IsFinite(u) && float.IsFinite(v))
                            {
                                if (u < minU) minU = u;
                                if (v < minV) minV = v;
                                if (u > maxU) maxU = u;
                                if (v > maxV) maxV = v;
                            }
                        }
                        var scaleOffset = new Vector4(1f, 1f, 0f, 0f);
                        if (float.IsFinite(minU) && maxU > minU && maxV > minV)
                        {
                            float sx = 1f / (maxU - minU), sy = 1f / (maxV - minV);
                            scaleOffset = new Vector4(sx, sy, -minU * sx, -minV * sy);
                        }

                        var albedoRt = RenderTexture.GetTemporary(MetaImageSize, MetaImageSize, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
                        var emissionRt = RenderTexture.GetTemporary(MetaImageSize, MetaImageSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                        byte[] albedoBytes, emissionBytes;
                        try
                        {
                            foreach (var (target, emissive) in new[] { (albedoRt, false), (emissionRt, true) })
                                GIMaterialImages.Draw(target, MetaImageSize, MetaImageSize, emissive, linear, settings.albedoBoost, realtimeUVs: false,
                                    () => GIMaterialImages.DrawRenderer(renderer, uvMesh, scaleOffset, emissive, s_Emits, "LightTransport probe bake", s_Warn, BakedAtlas.EmptySlotMaterial()));
                            albedoBytes = Native.ReadPixels(albedoRt.EnsureCreated(), 0, 0, MetaImageSize, MetaImageSize, false);
                            emissionBytes = Native.ReadPixels(emissionRt.EnsureCreated(), 0, 0, MetaImageSize, MetaImageSize, true);
                        }
                        finally
                        {
                            RenderTexture.ReleaseTemporary(albedoRt);
                            RenderTexture.ReleaseTemporary(emissionRt);
                        }
                        if (albedoBytes == null || emissionBytes == null)
                        {
                            Debug.LogError("Unbound: LightTransport probe baking requires a GPU for material meta passes.");
                            return false;
                        }

                        var shared = renderer.sharedMaterials;
                        var hasMaterial = new bool[shared.Length];
                        var doubleSided = new bool[shared.Length];
                        for (int k = 0; k < shared.Length; k++)
                        {
                            hasMaterial[k] = shared[k] != null;
                            doubleSided[k] = shared[k] != null && shared[k].doubleSidedGI;
                        }
                        var m = renderer.transform.localToWorldMatrix;
                        var matrix = new float[16];
                        for (int c = 0; c < 4; c++)
                            for (int r = 0; r < 4; r++) matrix[4 * c + r] = m[r, c];

                        renderers.Add(new
                        {
                            name = renderer.name,
                            mesh = index,
                            localToWorld = matrix,
                            hasMaterial,
                            castsShadows = renderer.shadowCastingMode != ShadowCastingMode.Off,
                            imageScaleOffset = new[] { scaleOffset.x, scaleOffset.y, scaleOffset.z, scaleOffset.w },
                            material = new
                            {
                                albedo = new { width = MetaImageSize, height = MetaImageSize, data = Convert.ToBase64String(albedoBytes) },
                                emissive = new { width = MetaImageSize, height = MetaImageSize, data = Convert.ToBase64String(emissionBytes) },
                                doubleSided,
                            },
                        });
                    }
                }
            }
            finally
            {
                foreach (var copy in withUVs.Values)
                    if (copy != null) Object.DestroyImmediate(copy);
            }

            var lightsJson = new List<object>();
            var lightMetas = new List<ExtractedLightMeta>();
            int nextShadowmaskChannel = 0;
            bool assignShadowmask = settings.mixedBakeMode == MixedLightingMode.Shadowmask;
            var allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var light in allLights)
            {
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy) continue;
                if (light.lightmapBakeType == LightmapBakeType.Realtime) continue;
                int kind = light.type == LightType.Directional ? 0 : light.type == LightType.Point ? 1 : light.type == LightType.Spot ? 2 : -1;
                if (kind < 0) continue;
                int mode = light.lightmapBakeType == LightmapBakeType.Mixed ? 1 : 2;
                int channel = -1;
                if (assignShadowmask && mode == 1 && light.shadows != LightShadows.None && nextShadowmaskChannel < 4)
                    channel = nextShadowmaskChannel++;

                var t = light.transform;
                Color direct = light.ScaledColor(light.intensity, linear: true);
                Color indirect = light.ScaledColor(light.intensity * light.bounceIntensity, linear: true);
                Vector3 pos = t.position;
                Quaternion rot = t.rotation;
                lightsJson.Add(new
                {
                    name = BakedAtlas.HierarchyPath(t),
                    fileId = (long)light.GetInstanceID(),
                    kind,
                    mode,
                    color = new[] { direct.r, direct.g, direct.b },
                    indirectColor = new[] { indirect.r, indirect.g, indirect.b },
                    position = new[] { pos.x, pos.y, pos.z },
                    rotation = new[] { rot.x, rot.y, rot.z, rot.w },
                    range = light.range,
                    spotAngle = light.spotAngle,
                    castsShadows = light.shadows != LightShadows.None,
                });
                lightMetas.Add(new ExtractedLightMeta
                {
                    Kind = kind,
                    Mode = mode,
                    Position = pos,
                    Range = light.range,
                    ShadowmaskChannel = channel,
                });
            }

            var sky = BakedAtlas.SkyOf(settings);
            var skyBytes = new byte[sky.Length * sizeof(float)];
            Buffer.BlockCopy(sky, 0, skyBytes, 0, skyBytes.Length);
            var payload = new
            {
                meshes,
                renderers,
                lights = lightsJson,
                blueNoise,
                environment = new
                {
                    samples = settings.environmentSampleCount,
                    importanceSampling = settings.environmentImportanceSampling,
                    side = SkyFaceSize,
                    faces = Convert.ToBase64String(skyBytes),
                },
            };

            bakeInput = new BakeInput
            {
                PopulateJson = JsonSerializer.Serialize(payload),
                Lights = lightMetas.ToArray(),
            };
            return true;
        }

        public static bool PopulateWorld(BakeInput bakeInput, BakeProgressState progress, IDeviceContext context, IWorld world)
        {
            if (bakeInput == null || string.IsNullOrEmpty(bakeInput.PopulateJson) || world is not RadeonRaysWorld rrWorld)
                return false;
            if (progress != null && progress.WasCancelled()) return false;
            if (!Native.IsReady) return false;
            try
            {
                string answer = Native.Call("light-transport-populate", bakeInput.PopulateJson);
                ulong worldId = JsonDocument.Parse(answer).RootElement.GetProperty("worldId").GetUInt64();
                rrWorld.SetWorldId(worldId);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Unbound: InputExtraction.PopulateWorld failed: {e.Message}");
                return false;
            }
        }

        public static int[] ComputeOcclusionLightIndicesFromBakeInput(BakeInput bakeInput, Vector3[] probePositions, uint maxLightsPerProbe = 4U)
        {
            int count = probePositions?.Length ?? 0;
            int maxLights = (int)maxLightsPerProbe;
            var result = new int[count * maxLights];
            Array.Fill(result, -1);
            if (bakeInput == null || bakeInput.Lights.Length == 0 || count == 0 || maxLights <= 0)
                return result;
            var lights = bakeInput.Lights;
            for (int p = 0; p < count; p++)
            {
                Vector3 pos = probePositions[p];
                int slot = 0;
                for (int li = 0; li < lights.Length && slot < maxLights; li++)
                {
                    if (lights[li].Mode != 1 || lights[li].ShadowmaskChannel < 0) continue;
                    if (lights[li].Kind != 0)
                    {
                        Vector3 d = lights[li].Position - pos;
                        if (d.sqrMagnitude >= lights[li].Range * lights[li].Range) continue;
                    }
                    result[p * maxLights + slot] = li;
                    slot++;
                }
            }
            return result;
        }

        public static int[] GetShadowmaskChannelsFromLightIndices(BakeInput bakeInput, int[] lightIndices)
        {
            if (lightIndices == null || lightIndices.Length == 0) return Array.Empty<int>();
            var channels = new int[lightIndices.Length];
            var lights = bakeInput?.Lights;
            for (int i = 0; i < lightIndices.Length; i++)
            {
                int idx = lightIndices[i];
                channels[i] = lights != null && idx >= 0 && idx < lights.Length ? lights[idx].ShadowmaskChannel : -1;
            }
            return channels;
        }
    }

    public unsafe partial class RadeonRaysProbeIntegrator : IProbeIntegrator, IDisposable
    {
        RadeonRaysWorld m_World;
        BufferSlice<Vector3> m_Positions;
        float m_Pushoff = 0.0001f;
        int m_BounceCount = 2;
        BakeProgressState m_Progress;

        public RadeonRaysProbeIntegrator() { }

        public void Prepare(IDeviceContext context, IWorld world, BufferSlice<Vector3> positions, float pushoff, int bounceCount)
        {
            m_World = world as RadeonRaysWorld;
            m_Positions = positions;
            m_Pushoff = pushoff;
            m_BounceCount = bounceCount;
        }

        public void SetProgressReporter(BakeProgressState progress) => m_Progress = progress;

        IProbeIntegrator.Result RunIntegration(
            IDeviceContext context,
            string kind,
            int positionOffset,
            int positionCount,
            int sampleCount,
            bool ignoreEnvironment,
            BufferID outBufferId,
            ulong outElemOffset,
            ulong outElemCount,
            ulong outElemBytes,
            int maxLightsPerProbe = 0,
            BufferSlice<int>? lightIndices = null)
        {
            if (m_Progress != null && m_Progress.WasCancelled())
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.Cancelled, "Cancelled");
            if (positionOffset < 0 || positionCount < 0)
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Position offset and count must be non-negative.");
            if (lightIndices.HasValue && maxLightsPerProbe <= 0)
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "maxLightsPerProbe must be positive.");
            var store = CpuDeviceBufferStore.Of(context);
            if (store == null || m_World == null || m_World.WorldId == 0)
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Integrator is not prepared with a valid context and world.");
            if (positionCount == 0)
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.Success, null);

            if (!store.TryGetBuffer(m_Positions.Id, out var posBuf))
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Unknown position buffer.");
            ulong posElemOffset;
            try { posElemOffset = checked(m_Positions.Offset + (ulong)positionOffset); }
            catch (OverflowException) { return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Position slice offset overflow."); }
            if (!CpuDeviceBufferStore.TryByteRange(posBuf, posElemOffset, (ulong)positionCount, 12UL, out int posStartByte, out int posByteCount))
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Position slice out of range.");

            if (!store.TryGetBuffer(outBufferId, out var outBuf))
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Unknown output buffer.");
            if (!CpuDeviceBufferStore.TryByteRange(outBuf, outElemOffset, outElemCount, outElemBytes, out int outStartByte, out int expectedOutBytes))
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Output slice out of range.");

            string indicesB64 = null;
            if (lightIndices.HasValue)
            {
                var li = lightIndices.Value;
                if (!store.TryGetBuffer(li.Id, out var idxBuf))
                    return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Unknown light indices buffer.");
                ulong idxElemOffset, idxElemCount;
                try
                {
                    idxElemOffset = checked(li.Offset + (ulong)positionOffset * (ulong)maxLightsPerProbe);
                    idxElemCount = checked((ulong)positionCount * (ulong)maxLightsPerProbe);
                }
                catch (OverflowException)
                {
                    return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Light indices slice overflow.");
                }
                if (!CpuDeviceBufferStore.TryByteRange(idxBuf, idxElemOffset, idxElemCount, 4UL, out int idxStartByte, out int idxByteCount))
                    return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.InvalidInput, "Light indices slice out of range.");
                indicesB64 = Convert.ToBase64String(idxBuf, idxStartByte, idxByteCount);
            }

            try
            {
                string req = JsonSerializer.Serialize(new
                {
                    worldId = m_World.WorldId,
                    kind,
                    positions = Convert.ToBase64String(posBuf, posStartByte, posByteCount),
                    sampleCount = Math.Max(sampleCount, 0),
                    maxBounces = Math.Max(m_BounceCount, 0),
                    pushoff = m_Pushoff,
                    ignoreEnvironment,
                    maxLightsPerProbe,
                    perProbeLightIndices = indicesB64,
                });
                string answer = Native.Call("light-transport-integrate", req);
                byte[] bytes = Convert.FromBase64String(JsonDocument.Parse(answer).RootElement.GetProperty("data").GetString());
                if (bytes.Length != expectedOutBytes)
                    return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.JobFailed, $"Unexpected result size {bytes.Length} (expected {expectedOutBytes}).");

                Buffer.BlockCopy(bytes, 0, outBuf, outStartByte, bytes.Length);
                m_Progress?.IncrementCompletedWorkSteps((ulong)positionCount);
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.Success, null);
            }
            catch (Exception e)
            {
                return new IProbeIntegrator.Result(IProbeIntegrator.ResultType.JobFailed, e.Message);
            }
        }

        public IProbeIntegrator.Result IntegrateDirectRadiance(IDeviceContext context, int positionOffset, int positionCount, int sampleCount, bool ignoreEnvironment, BufferSlice<SphericalHarmonicsL2> radianceEstimateOut) =>
            RunIntegration(context, "direct", positionOffset, positionCount, sampleCount, ignoreEnvironment,
                radianceEstimateOut.Id, radianceEstimateOut.Offset, positionCount > 0 ? (ulong)positionCount : 0UL, 108UL);

        public IProbeIntegrator.Result IntegrateIndirectRadiance(IDeviceContext context, int positionOffset, int positionCount, int sampleCount, bool ignoreEnvironment, BufferSlice<SphericalHarmonicsL2> radianceEstimateOut) =>
            RunIntegration(context, "indirect", positionOffset, positionCount, sampleCount, ignoreEnvironment,
                radianceEstimateOut.Id, radianceEstimateOut.Offset, positionCount > 0 ? (ulong)positionCount : 0UL, 108UL);

        public IProbeIntegrator.Result IntegrateValidity(IDeviceContext context, int positionOffset, int positionCount, int sampleCount, BufferSlice<float> validityEstimateOut) =>
            RunIntegration(context, "validity", positionOffset, positionCount, sampleCount, false,
                validityEstimateOut.Id, validityEstimateOut.Offset, positionCount > 0 ? (ulong)positionCount : 0UL, 4UL);

        public IProbeIntegrator.Result IntegrateOcclusion(IDeviceContext context, int positionOffset, int positionCount, int sampleCount, int maxLightsPerProbe, BufferSlice<int> perProbeLightIndices, BufferSlice<float> probeOcclusionEstimateOut) =>
            RunIntegration(context, "occlusion", positionOffset, positionCount, sampleCount, false,
                probeOcclusionEstimateOut.Id, probeOcclusionEstimateOut.Offset,
                positionCount > 0 && maxLightsPerProbe > 0 ? (ulong)positionCount * (ulong)maxLightsPerProbe : 0UL, 4UL,
                maxLightsPerProbe, perProbeLightIndices);

        public void Dispose()
        {
            m_World = null;
            m_Progress = null;
        }
    }
}

namespace UnityEngine.LightTransport.PostProcessing
{
    public unsafe partial interface IProbePostProcessor : IDisposable
    {
        bool Initialize(IDeviceContext context);
        bool ConvolveRadianceToIrradiance(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> radianceIn, BufferSlice<SphericalHarmonicsL2> irradianceOut, int probeCount);
        bool ConvertToUnityFormat(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> irradianceIn, BufferSlice<SphericalHarmonicsL2> irradianceOut, int probeCount);
        bool AddSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> A, BufferSlice<SphericalHarmonicsL2> B, BufferSlice<SphericalHarmonicsL2> sum, int probeCount);
        bool ScaleSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount, float scale);
        bool WindowSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount);
        bool DeringSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount);
    }

    /// <summary>Shared CPU spherical-harmonics post-processing operations for
    /// <see cref="RadeonRaysProbePostProcessor"/> and <see cref="ReferenceProbePostProcessor"/>.</summary>
    internal static class CpuProbePostProcess
    {
        const float AHat0 = 3.14159265358979323846f;
        const float AHat1 = 2.09439510239319549230f;
        const float AHat2 = 0.78539816339744830961f;

        const float ShL0Norm = 0.28209479177387814347f;
        const float ShL1Norm = 0.48860251190291992158f;
        const float ShL2_2Norm = 1.09254843059207907054f;
        const float ShL2_1Norm = 1.09254843059207907054f;
        const float ShL20Norm = 0.31539156525252000603f;
        const float ShL21Norm = 1.09254843059207907054f;
        const float ShL22Norm = 0.54627421529603953527f;

        const float WindowL1 = 0.922066f;
        const float WindowL2 = 0.731864f;

        static bool TrySpan(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> slice, int probeCount, out Span<float> floats)
        {
            floats = default;
            var store = CpuDeviceBufferStore.Of(context);
            if (store == null || probeCount < 0 || !store.TryGetBuffer(slice.Id, out var buf)) return false;
            if (!CpuDeviceBufferStore.TryByteRange(buf, slice.Offset, (ulong)probeCount, 108UL, out int startByte, out int lengthBytes))
                return false;
            floats = MemoryMarshal.Cast<byte, float>(buf.AsSpan(startByte, lengthBytes));
            return true;
        }

        internal static bool ConvolveRadianceToIrradiance(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> radianceIn, BufferSlice<SphericalHarmonicsL2> irradianceOut, int probeCount)
        {
            if (!TrySpan(context, radianceIn, probeCount, out var src) || !TrySpan(context, irradianceOut, probeCount, out var dst))
                return false;
            for (int p = 0; p < probeCount; p++)
            {
                int b = p * 27;
                for (int c = 0; c < 3; c++)
                {
                    int o = b + c * 9;
                    dst[o] = src[o] * AHat0;
                    for (int i = 1; i < 4; i++) dst[o + i] = src[o + i] * AHat1;
                    for (int i = 4; i < 9; i++) dst[o + i] = src[o + i] * AHat2;
                }
            }
            return true;
        }

        internal static bool ConvertToUnityFormat(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> irradianceIn, BufferSlice<SphericalHarmonicsL2> irradianceOut, int probeCount)
        {
            if (!TrySpan(context, irradianceIn, probeCount, out var src) || !TrySpan(context, irradianceOut, probeCount, out var dst))
                return false;
            const float invPi = 1f / Mathf.PI;
            for (int p = 0; p < probeCount; p++)
            {
                int b = p * 27;
                for (int c = 0; c < 3; c++)
                {
                    int o = b + c * 9;
                    float l00 = src[o];
                    float l1_1 = src[o + 1];
                    float l10 = src[o + 2];
                    float l11 = src[o + 3];
                    float l2_2 = src[o + 4];
                    float l2_1 = src[o + 5];
                    float l20 = src[o + 6];
                    float l21 = src[o + 7];
                    float l22 = src[o + 8];

                    dst[o] = l00 * ShL0Norm * invPi;
                    dst[o + 1] = l10 * ShL1Norm * invPi;
                    dst[o + 2] = l11 * ShL1Norm * invPi;
                    dst[o + 3] = l1_1 * ShL1Norm * invPi;
                    dst[o + 4] = l2_2 * ShL2_2Norm * invPi;
                    dst[o + 5] = l2_1 * ShL2_1Norm * invPi;
                    dst[o + 6] = l20 * ShL20Norm * invPi;
                    dst[o + 7] = l21 * ShL21Norm * invPi;
                    dst[o + 8] = l22 * ShL22Norm * invPi;
                }
            }
            return true;
        }

        internal static bool Add(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> a, BufferSlice<SphericalHarmonicsL2> b, BufferSlice<SphericalHarmonicsL2> sum, int probeCount)
        {
            if (!TrySpan(context, a, probeCount, out var sa) || !TrySpan(context, b, probeCount, out var sb) || !TrySpan(context, sum, probeCount, out var dst))
                return false;
            for (int i = 0; i < dst.Length; i++) dst[i] = sa[i] + sb[i];
            return true;
        }

        internal static bool Scale(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount, float scale)
        {
            if (!TrySpan(context, shIn, probeCount, out var src) || !TrySpan(context, shOut, probeCount, out var dst))
                return false;
            for (int i = 0; i < dst.Length; i++) dst[i] = src[i] * scale;
            return true;
        }

        internal static bool Window(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount)
        {
            if (!TrySpan(context, shIn, probeCount, out var src) || !TrySpan(context, shOut, probeCount, out var dst))
                return false;
            for (int p = 0; p < probeCount; p++)
            {
                int b = p * 27;
                for (int c = 0; c < 3; c++)
                {
                    int o = b + c * 9;
                    dst[o] = src[o];
                    for (int i = 1; i < 4; i++) dst[o + i] = src[o + i] * WindowL1;
                    for (int i = 4; i < 9; i++) dst[o + i] = src[o + i] * WindowL2;
                }
            }
            return true;
        }

        internal static bool Dering(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount)
        {
            var store = CpuDeviceBufferStore.Of(context);
            if (store == null || probeCount < 0) return false;
            if (!store.TryGetBuffer(shIn.Id, out var srcBuf) || !store.TryGetBuffer(shOut.Id, out var dstBuf))
                return false;
            if (!CpuDeviceBufferStore.TryByteRange(srcBuf, shIn.Offset, (ulong)probeCount, 108UL, out int srcByteOffset, out int byteLength))
                return false;
            if (!CpuDeviceBufferStore.TryByteRange(dstBuf, shOut.Offset, (ulong)probeCount, 108UL, out int dstByteOffset, out _))
                return false;
            if (probeCount == 0) return true;
            if (!Native.IsReady) return false;
            string req = JsonSerializer.Serialize(new { sh = Convert.ToBase64String(srcBuf, srcByteOffset, byteLength) });
            string answer = Native.Call("light-transport-dering", req);
            byte[] bytes = Convert.FromBase64String(JsonDocument.Parse(answer).RootElement.GetProperty("data").GetString());
            if (bytes.Length != byteLength) return false;
            Buffer.BlockCopy(bytes, 0, dstBuf, dstByteOffset, bytes.Length);
            return true;
        }
    }

    public unsafe partial class RadeonRaysProbePostProcessor : IProbePostProcessor, IDisposable
    {
        bool m_Initialized;

        public RadeonRaysProbePostProcessor() { }
        public bool Initialize(IDeviceContext context) => m_Initialized = CpuDeviceBufferStore.Of(context) != null;
        public bool ConvolveRadianceToIrradiance(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> radianceIn, BufferSlice<SphericalHarmonicsL2> irradianceOut, int probeCount) =>
            m_Initialized && CpuProbePostProcess.ConvolveRadianceToIrradiance(context, radianceIn, irradianceOut, probeCount);
        public bool ConvertToUnityFormat(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> irradianceIn, BufferSlice<SphericalHarmonicsL2> irradianceOut, int probeCount) =>
            m_Initialized && CpuProbePostProcess.ConvertToUnityFormat(context, irradianceIn, irradianceOut, probeCount);
        public bool AddSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> a, BufferSlice<SphericalHarmonicsL2> b, BufferSlice<SphericalHarmonicsL2> sum, int probeCount) =>
            m_Initialized && CpuProbePostProcess.Add(context, a, b, sum, probeCount);
        public bool ScaleSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount, float scale) =>
            m_Initialized && CpuProbePostProcess.Scale(context, shIn, shOut, probeCount, scale);
        public bool WindowSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount) =>
            m_Initialized && CpuProbePostProcess.Window(context, shIn, shOut, probeCount);
        public bool DeringSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount) =>
            m_Initialized && CpuProbePostProcess.Dering(context, shIn, shOut, probeCount);
        public void Dispose() => m_Initialized = false;
    }

    public unsafe partial class ReferenceProbePostProcessor : IProbePostProcessor, IDisposable
    {
        bool m_Initialized;

        public ReferenceProbePostProcessor() { }
        public bool Initialize(IDeviceContext context) => m_Initialized = CpuDeviceBufferStore.Of(context) != null;
        public bool ConvolveRadianceToIrradiance(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> radianceIn, BufferSlice<SphericalHarmonicsL2> irradianceOut, int probeCount) =>
            m_Initialized && CpuProbePostProcess.ConvolveRadianceToIrradiance(context, radianceIn, irradianceOut, probeCount);
        public bool ConvertToUnityFormat(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> irradianceIn, BufferSlice<SphericalHarmonicsL2> irradianceOut, int probeCount) =>
            m_Initialized && CpuProbePostProcess.ConvertToUnityFormat(context, irradianceIn, irradianceOut, probeCount);
        public bool AddSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> a, BufferSlice<SphericalHarmonicsL2> b, BufferSlice<SphericalHarmonicsL2> sum, int probeCount) =>
            m_Initialized && CpuProbePostProcess.Add(context, a, b, sum, probeCount);
        public bool ScaleSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount, float scale) =>
            m_Initialized && CpuProbePostProcess.Scale(context, shIn, shOut, probeCount, scale);
        public bool WindowSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount) =>
            m_Initialized && CpuProbePostProcess.Window(context, shIn, shOut, probeCount);
        public bool DeringSphericalHarmonicsL2(IDeviceContext context, BufferSlice<SphericalHarmonicsL2> shIn, BufferSlice<SphericalHarmonicsL2> shOut, int probeCount) =>
            m_Initialized && CpuProbePostProcess.Dering(context, shIn, shOut, probeCount);
        public void Dispose() => m_Initialized = false;
    }
}
