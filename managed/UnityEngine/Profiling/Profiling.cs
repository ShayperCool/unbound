using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using Unbound.Runtime.Profiling;
using UnityEngine;

namespace Unity.Profiling
{
    /// <summary>A profiler category (see <see cref="ProfilerCore"/>): Unity's built-in ones by id, and
    /// categories made by name.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 2)]
    public readonly partial struct ProfilerCategory
    {
        [FieldOffset(0)]
        readonly ushort m_CategoryId;

        public ProfilerCategory(string categoryName)
        {
            m_CategoryId = ProfilerUnsafeUtility.CreateCategory(categoryName, ProfilerCategoryColor.Scripts);
        }

        public ProfilerCategory(string categoryName, ProfilerCategoryColor color)
        {
            m_CategoryId = ProfilerUnsafeUtility.CreateCategory(categoryName, color);
        }

        internal ProfilerCategory(ushort category)
        {
            m_CategoryId = category;
        }

        public string Name => ProfilerUnsafeUtility.GetCategoryDescription(m_CategoryId).Name;

        public Color32 Color => ProfilerUnsafeUtility.GetCategoryDescription(m_CategoryId).Color;

        public override string ToString() => Name;

        public static implicit operator ushort(ProfilerCategory category) => category.m_CategoryId;

        public static ProfilerCategory Render => new ProfilerCategory(0);
        public static ProfilerCategory Scripts => new ProfilerCategory(1);
        public static ProfilerCategory Gui => new ProfilerCategory(4);
        public static ProfilerCategory Physics => new ProfilerCategory(5);
        public static ProfilerCategory Physics2D => new ProfilerCategory(33);
        public static ProfilerCategory Animation => new ProfilerCategory(6);
        public static ProfilerCategory Ai => new ProfilerCategory(7);
        public static ProfilerCategory Audio => new ProfilerCategory(8);
        public static ProfilerCategory Video => new ProfilerCategory(11);
        public static ProfilerCategory Particles => new ProfilerCategory(12);
        public static ProfilerCategory Lighting => new ProfilerCategory(13);
        public static ProfilerCategory Network => new ProfilerCategory(14);
        public static ProfilerCategory Loading => new ProfilerCategory(15);
        public static ProfilerCategory Vr => new ProfilerCategory(22);
        public static ProfilerCategory Input => new ProfilerCategory(30);
        public static ProfilerCategory Memory => new ProfilerCategory(23);
        public static ProfilerCategory VirtualTexturing => new ProfilerCategory(31);
        public static ProfilerCategory FileIO => new ProfilerCategory(25);
        public static ProfilerCategory Internal => new ProfilerCategory(24);
        internal static ProfilerCategory Any => new ProfilerCategory(ushort.MaxValue);
        internal static ProfilerCategory GPU => new ProfilerCategory(32);
    }

    /// <summary>A named profiler sample: Begin/End time it for the recorders that listen.</summary>
    public partial struct ProfilerMarker
    {
        [NonSerialized]
        [NativeDisableUnsafePtrRestriction]
        internal readonly IntPtr m_Ptr;

        public IntPtr Handle => m_Ptr;

        public ProfilerMarker(string name)
        {
            m_Ptr = ProfilerUnsafeUtility.CreateMarker(name, ProfilerUnsafeUtility.CategoryScripts, MarkerFlags.Default, 0);
        }

        public unsafe ProfilerMarker(char* name, int nameLen)
        {
            m_Ptr = ProfilerUnsafeUtility.CreateMarker(name, nameLen, ProfilerUnsafeUtility.CategoryScripts, MarkerFlags.Default, 0);
        }

        public ProfilerMarker(ProfilerCategory category, string name)
        {
            m_Ptr = ProfilerUnsafeUtility.CreateMarker(name, category, MarkerFlags.Default, 0);
        }

        public unsafe ProfilerMarker(ProfilerCategory category, char* name, int nameLen)
        {
            m_Ptr = ProfilerUnsafeUtility.CreateMarker(name, nameLen, category, MarkerFlags.Default, 0);
        }

        public ProfilerMarker(ProfilerCategory category, string name, MarkerFlags flags)
        {
            m_Ptr = ProfilerUnsafeUtility.CreateMarker(name, category, flags, 0);
        }

        public unsafe ProfilerMarker(ProfilerCategory category, char* name, int nameLen, MarkerFlags flags)
        {
            m_Ptr = ProfilerUnsafeUtility.CreateMarker(name, nameLen, category, flags, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_PROFILER")]
        public void Begin() => ProfilerUnsafeUtility.BeginSample(m_Ptr);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_PROFILER")]
        public void Begin(UnityEngine.Object contextUnityObject) => ProfilerUnsafeUtility.Internal_BeginWithObject(m_Ptr, contextUnityObject);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("ENABLE_PROFILER")]
        public void End() => ProfilerUnsafeUtility.EndSample(m_Ptr);

        [Conditional("ENABLE_PROFILER")]
        internal void GetName(ref string name) => name = ProfilerUnsafeUtility.Internal_GetName(m_Ptr);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AutoScope Auto() => new AutoScope(m_Ptr);

        public partial struct AutoScope : IDisposable
        {
            [NativeDisableUnsafePtrRestriction]
            internal readonly IntPtr m_Ptr;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal AutoScope(IntPtr markerPtr)
            {
                m_Ptr = markerPtr;
                if (m_Ptr != IntPtr.Zero) ProfilerUnsafeUtility.BeginSample(markerPtr);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                if (m_Ptr != IntPtr.Zero) ProfilerUnsafeUtility.EndSample(m_Ptr);
            }
        }
    }

    internal partial struct ProfilerMarkerWithStringData
    {
        IntPtr _marker;

        public static ProfilerMarkerWithStringData Create(string name, string parameterName)
        {
            var marker = ProfilerUnsafeUtility.CreateMarker(name, 16, MarkerFlags.Default, 1);
            ProfilerUnsafeUtility.SetMarkerMetadata(marker, 0, parameterName, (byte)ProfilerMarkerDataType.String16, 0);
            return new ProfilerMarkerWithStringData { _marker = marker };
        }

        public AutoScope Auto(bool enabled, Func<string> parameterValue) => enabled ? Auto(parameterValue()) : new AutoScope(IntPtr.Zero);

        public unsafe AutoScope Auto(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            fixed (char* chars = value)
            {
                var data = new ProfilerMarkerData { Type = (byte)ProfilerMarkerDataType.String16, Size = (uint)(value.Length * 2 + 2), Ptr = chars };
                ProfilerUnsafeUtility.BeginSampleWithMetadata(_marker, 1, &data);
            }
            return new AutoScope(_marker);
        }

        public partial struct AutoScope : IDisposable
        {
            IntPtr _marker;

            internal AutoScope(IntPtr marker)
            {
                _marker = marker;
            }

            public void Dispose()
            {
                if (_marker != IntPtr.Zero) ProfilerUnsafeUtility.EndSample(_marker);
            }
        }
    }

    /// <summary>Reads a stat (a marker's time per frame, a counter's value) back, frame by frame.</summary>
    [DebuggerDisplay("Count = {Count}")]
    [DebuggerTypeProxy(typeof(ProfilerRecorderDebugView))]
    public partial struct ProfilerRecorder : IDisposable
    {
        internal enum ControlOptions
        {
            Start = 0,
            Stop = 1,
            Reset = 2,
            Release = 4,
            SetFilterToCurrentThread = 5,
            SetToCollectFromAllThreads = 6,
        }

        internal enum CountOptions
        {
            Count = 0,
            MaxCount = 1,
        }

        internal ulong handle;

        internal const ProfilerRecorderOptions SharedRecorder = (ProfilerRecorderOptions)128;

        public ProfilerRecorder(string statName, int capacity = 1, ProfilerRecorderOptions options = ProfilerRecorderOptions.Default)
            : this(ProfilerCategory.Any, statName, capacity, options)
        {
        }

        public ProfilerRecorder(string categoryName, string statName, int capacity = 1, ProfilerRecorderOptions options = ProfilerRecorderOptions.Default)
            : this(new ProfilerCategory(categoryName), statName, capacity, options)
        {
        }

        public ProfilerRecorder(ProfilerCategory category, string statName, int capacity = 1, ProfilerRecorderOptions options = ProfilerRecorderOptions.Default)
        {
            handle = ProfilerCore.CreateRecorder(ProfilerRecorderHandle.GetByName(category, statName).handle, category, statName, capacity, options);
        }

        public unsafe ProfilerRecorder(ProfilerCategory category, char* statName, int statNameLen, int capacity = 1, ProfilerRecorderOptions options = ProfilerRecorderOptions.Default)
            : this(category, new string(statName, 0, statNameLen), capacity, options)
        {
        }

        public ProfilerRecorder(ProfilerMarker marker, int capacity = 1, ProfilerRecorderOptions options = ProfilerRecorderOptions.Default)
        {
            handle = ProfilerCore.CreateRecorder(ProfilerRecorderHandle.Get(marker).handle, ushort.MaxValue, null, capacity, options);
        }

        public ProfilerRecorder(ProfilerRecorderHandle statHandle, int capacity = 1, ProfilerRecorderOptions options = ProfilerRecorderOptions.Default)
        {
            handle = ProfilerCore.CreateRecorder(statHandle.handle, ushort.MaxValue, null, capacity, options);
        }

        public static ProfilerRecorder StartNew(ProfilerCategory category, string statName, int capacity = 1, ProfilerRecorderOptions options = ProfilerRecorderOptions.Default) =>
            new ProfilerRecorder(category, statName, capacity, options | ProfilerRecorderOptions.StartImmediately);

        public static ProfilerRecorder StartNew(ProfilerMarker marker, int capacity = 1, ProfilerRecorderOptions options = ProfilerRecorderOptions.Default) =>
            new ProfilerRecorder(marker, capacity, options | ProfilerRecorderOptions.StartImmediately);

        internal static ProfilerRecorder StartNew() =>
            new ProfilerRecorder { handle = ProfilerCore.CreateRecorder(0, ushort.MaxValue, null, 0, ProfilerRecorderOptions.StartImmediately) };

        ProfilerCore.Recorder Recorder
        {
            get
            {
                CheckInitializedAndThrow();
                return ProfilerCore.RecorderOf(handle) ?? throw new InvalidOperationException("ProfilerRecorder object is not initialized or has been disposed.");
            }
        }

        public bool Valid => handle != 0 && ProfilerCore.RecorderOf(handle)?.Stat != null;

        public ProfilerMarkerDataType DataType => Recorder.Stat?.DataType ?? 0;

        public ProfilerMarkerDataUnit UnitType => Recorder.Stat?.Unit ?? ProfilerMarkerDataUnit.Undefined;

        public long CurrentValue => ProfilerCore.CurrentValue(Recorder);

        public double CurrentValueAsDouble => ProfilerCore.ReadValueAsDouble(Recorder);

        public long LastValue
        {
            get
            {
                var recorder = Recorder;
                lock (recorder) return recorder.Count == 0 ? 0 : recorder.Sample(recorder.Count - 1).Value;
            }
        }

        public double LastValueAsDouble
        {
            get
            {
                var recorder = Recorder;
                lock (recorder) return recorder.Count == 0 ? 0 : ProfilerCore.AsDouble(recorder, recorder.Sample(recorder.Count - 1).Value);
            }
        }

        public int Capacity => Recorder.Samples.Length;

        public int Count
        {
            get
            {
                var recorder = Recorder;
                lock (recorder) return recorder.Count;
            }
        }

        public bool IsRunning => Recorder.Running;

        public bool WrappedAround => Recorder.Wrapped;

        public void Start() => Recorder.Running = true;

        public void Stop() => Recorder.Running = false;

        public void Reset()
        {
            var recorder = Recorder;
            lock (recorder)
            {
                recorder.Count = 0;
                recorder.Start = 0;
                recorder.Wrapped = false;
                recorder.FrameValue = 0;
                recorder.FrameCount = 0;
            }
        }

        public ProfilerRecorderSample GetSample(int index)
        {
            var recorder = Recorder;
            lock (recorder)
            {
                if (index < 0 || index >= recorder.Count)
                    throw new IndexOutOfRangeException($"Index {index} is out of range of '{recorder.Count}' Length.");
                return recorder.Sample(index);
            }
        }

        public void CopyTo(List<ProfilerRecorderSample> outSamples, bool reset = false)
        {
            if (outSamples == null) throw new ArgumentNullException(nameof(outSamples));
            var recorder = Recorder;
            lock (recorder)
            {
                outSamples.Clear();
                for (int i = 0; i < recorder.Count; i++) outSamples.Add(recorder.Sample(i));
            }
            if (reset) Reset();
        }

        public unsafe int CopyTo(ProfilerRecorderSample* dest, int destSize, bool reset = false)
        {
            CheckInitializedWithParamsAndThrow(dest);
            var recorder = Recorder;
            int count;
            lock (recorder)
            {
                count = Math.Min(destSize, recorder.Count);
                for (int i = 0; i < count; i++) dest[i] = recorder.Sample(i);
            }
            if (reset) Reset();
            return count;
        }

        public unsafe ProfilerRecorderSample[] ToArray()
        {
            var recorder = Recorder;
            lock (recorder)
            {
                var samples = new ProfilerRecorderSample[recorder.Count];
                for (int i = 0; i < samples.Length; i++) samples[i] = recorder.Sample(i);
                return samples;
            }
        }

        internal void FilterToCurrentThread()
        {
            var recorder = Recorder;
            recorder.CurrentThreadOnly = true;
            recorder.Thread = Environment.CurrentManagedThreadId;
        }

        internal void CollectFromAllThreads() => Recorder.CurrentThreadOnly = false;

        public void Dispose()
        {
            if (handle == 0) return;
            ProfilerCore.ReleaseRecorder(handle);
            handle = 0;
        }

        unsafe void CheckInitializedWithParamsAndThrow(ProfilerRecorderSample* dest)
        {
            if (handle == 0) throw new InvalidOperationException("ProfilerRecorder object is not initialized or has been disposed.");
            if (dest == null) throw new ArgumentNullException(nameof(dest));
        }

        void CheckInitializedAndThrow()
        {
            if (handle == 0) throw new InvalidOperationException("ProfilerRecorder object is not initialized or has been disposed.");
        }
    }

    internal sealed partial class ProfilerRecorderDebugView
    {
        ProfilerRecorder m_Recorder;

        public ProfilerRecorderDebugView(ProfilerRecorder r)
        {
            m_Recorder = r;
        }

        public ProfilerRecorderSample[] Items => m_Recorder.ToArray();
    }

    [DebuggerDisplay("Value = {Value}; Count = {Count}")]
    public partial struct ProfilerRecorderSample
    {
        long value;
        long count;
        long refValue;

        public long Value => value;

        public long Count => count;
    }
}

namespace Unity.Profiling.LowLevel.Unsafe
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public partial struct ProfilerMarkerData
    {
        [FieldOffset(0)]
        public byte Type;
        [FieldOffset(1)]
        readonly byte reserved0;
        [FieldOffset(2)]
        readonly ushort reserved1;
        [FieldOffset(4)]
        public uint Size;
        [FieldOffset(8)]
        public unsafe void* Ptr;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public readonly unsafe partial struct ProfilerCategoryDescription
    {
        [FieldOffset(0)]
        public readonly ushort Id;
        [FieldOffset(2)]
        public readonly ushort Flags;
        [FieldOffset(4)]
        public readonly Color32 Color;
        [FieldOffset(8)]
        readonly int reserved0;
        [FieldOffset(12)]
        public readonly int NameUtf8Len;
        [FieldOffset(16)]
        public readonly byte* NameUtf8;

        internal ProfilerCategoryDescription(ushort id, ushort flags, Color32 color, int nameUtf8Len, byte* nameUtf8)
        {
            Id = id;
            Flags = flags;
            Color = color;
            reserved0 = 0;
            NameUtf8Len = nameUtf8Len;
            NameUtf8 = nameUtf8;
        }

        public string Name => ProfilerUnsafeUtility.Utf8ToString(NameUtf8, NameUtf8Len);
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public readonly unsafe partial struct ProfilerRecorderDescription
    {
        [FieldOffset(0)]
        readonly ProfilerCategory category;
        [FieldOffset(2)]
        readonly MarkerFlags flags;
        [FieldOffset(4)]
        readonly ProfilerMarkerDataType dataType;
        [FieldOffset(5)]
        readonly ProfilerMarkerDataUnit unitType;
        [FieldOffset(8)]
        readonly int reserved0;
        [FieldOffset(12)]
        readonly int nameUtf8Len;
        [FieldOffset(16)]
        readonly byte* nameUtf8;

        internal ProfilerRecorderDescription(ProfilerCategory category, MarkerFlags flags, ProfilerMarkerDataType dataType, ProfilerMarkerDataUnit unitType, int nameUtf8Len, byte* nameUtf8)
        {
            this.category = category;
            this.flags = flags;
            this.dataType = dataType;
            this.unitType = unitType;
            reserved0 = 0;
            this.nameUtf8Len = nameUtf8Len;
            this.nameUtf8 = nameUtf8;
        }

        public ProfilerCategory Category => category;
        public MarkerFlags Flags => flags;
        public ProfilerMarkerDataType DataType => dataType;
        public ProfilerMarkerDataUnit UnitType => unitType;
        public int NameUtf8Len => nameUtf8Len;
        public byte* NameUtf8 => nameUtf8;
        public string Name => ProfilerUnsafeUtility.Utf8ToString(nameUtf8, nameUtf8Len);
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public readonly partial struct ProfilerRecorderHandle
    {
        const ulong k_InvalidHandle = ulong.MaxValue;

        [FieldOffset(0)]
        internal readonly ulong handle;

        internal ProfilerRecorderHandle(ulong handle)
        {
            this.handle = handle;
        }

        public bool Valid => handle != 0 && handle != ulong.MaxValue;

        internal static ProfilerRecorderHandle Get(ProfilerMarker marker) => new ProfilerRecorderHandle((ulong)marker.Handle.ToInt64());

        internal static ProfilerRecorderHandle Get(ProfilerCategory category, string statName)
        {
            if (string.IsNullOrEmpty(statName)) throw new ArgumentException("String must be not null or empty", nameof(statName));
            return GetByName(category, statName);
        }

        internal static ProfilerRecorderHandle GetByName(ProfilerCategory category, string name)
        {
            var stat = name == null ? null : ProfilerCore.StatByName(category, name);
            return new ProfilerRecorderHandle(stat == null ? k_InvalidHandle : (ulong)stat.Handle);
        }

        internal static unsafe ProfilerRecorderHandle GetByName__Unmanaged(ProfilerCategory category, byte* name, int nameLen) =>
            GetByName(category, ProfilerUnsafeUtility.Utf8ToString(name, nameLen));

        internal static unsafe ProfilerRecorderHandle GetByName(ProfilerCategory category, char* name, int nameLen) =>
            GetByName(category, new string(name, 0, nameLen));

        public static ProfilerRecorderDescription GetDescription(ProfilerRecorderHandle handle)
        {
            if (!handle.Valid) throw new ArgumentException("ProfilerRecorderHandle is not initialized or is not available", nameof(handle));
            var stat = ProfilerCore.StatOf(handle.handle);
            if (stat == null) throw new ArgumentException("ProfilerRecorderHandle is not initialized or is not available", nameof(handle));
            unsafe
            {
                return new ProfilerRecorderDescription(new ProfilerCategory(stat.Category), stat.Flags, stat.DataType, stat.Unit, stat.NameUtf8Length, stat.NameUtf8);
            }
        }

        public static void GetAvailable(List<ProfilerRecorderHandle> outRecorderHandleList)
        {
            if (outRecorderHandleList == null) throw new ArgumentNullException(nameof(outRecorderHandleList));
            outRecorderHandleList.Clear();
            foreach (var stat in ProfilerCore.AllStats()) outRecorderHandleList.Add(new ProfilerRecorderHandle((ulong)stat.Handle));
        }
    }

    /// <summary>The profiler's low-level API over <see cref="ProfilerCore"/>.</summary>
    public static unsafe partial class ProfilerUnsafeUtility
    {
        public partial struct TimestampConversionRatio
        {
            public long Numerator;
            public long Denominator;
        }

        public const ushort CategoryRender = 0;
        public const ushort CategoryScripts = 1;
        public const ushort CategoryGUI = 4;
        public const ushort CategoryPhysics = 5;
        public const ushort CategoryAnimation = 6;
        public const ushort CategoryAi = 7;
        public const ushort CategoryAudio = 8;
        public const ushort CategoryVideo = 11;
        public const ushort CategoryParticles = 12;
        public const ushort CategoryLighting = 13;
        [Obsolete("CategoryLightning has been renamed. Use CategoryLighting instead (UnityUpgradable) -> CategoryLighting", false)]
        public const ushort CategoryLightning = 13;
        public const ushort CategoryNetwork = 14;
        public const ushort CategoryLoading = 15;
        public const ushort CategoryOther = 16;
        public const ushort CategoryVr = 22;
        public const ushort CategoryAllocation = 23;
        public const ushort CategoryInternal = 24;
        public const ushort CategoryFileIO = 25;
        public const ushort CategoryInput = 30;
        public const ushort CategoryVirtualTexturing = 31;
        internal const ushort CategoryGPU = 32;
        public const ushort CategoryPhysics2D = 33;
        internal const ushort CategoryAny = ushort.MaxValue;

        public static long Timestamp => Stopwatch.GetTimestamp();

        public static TimestampConversionRatio TimestampToNanosecondsConversionRatio
        {
            get
            {
                long numerator = 1_000_000_000L, denominator = Stopwatch.Frequency;
                long gcd = Gcd(numerator, denominator);
                return new TimestampConversionRatio { Numerator = numerator / gcd, Denominator = denominator / gcd };
            }
        }

        static long Gcd(long a, long b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }

        internal static ushort CreateCategory(string name, ProfilerCategoryColor colorIndex) => ProfilerCore.CreateCategory(name, colorIndex);

        internal static ushort CreateCategory__Unmanaged(byte* name, int nameLen, ProfilerCategoryColor colorIndex) =>
            ProfilerCore.CreateCategory(Utf8ToString(name, nameLen), colorIndex);

        public static ushort CreateCategory(char* name, int nameLen, ProfilerCategoryColor colorIndex) =>
            ProfilerCore.CreateCategory(new string(name, 0, nameLen), colorIndex);

        public static ushort GetCategoryByName(char* name, int nameLen) => ProfilerCore.CategoryByName(new string(name, 0, nameLen));

        public static ProfilerCategoryDescription GetCategoryDescription(ushort categoryId)
        {
            var category = ProfilerCore.CategoryOf(categoryId);
            if (category == null) return default;
            return new ProfilerCategoryDescription(category.Id, category.Id < 39 ? (ushort)ProfilerCategoryFlags.Builtin : (ushort)0, category.Color, category.NameUtf8Length, category.NameUtf8);
        }

        internal static Color32 GetCategoryColor(ProfilerCategoryColor colorIndex) => ProfilerCore.ColorOf(colorIndex);

        public static IntPtr CreateMarker(string name, ushort categoryId, MarkerFlags flags, int metadataCount) => ProfilerCore.CreateMarker(name, categoryId, flags, metadataCount);

        internal static IntPtr CreateMarker__Unmanaged(byte* name, int nameLen, ushort categoryId, MarkerFlags flags, int metadataCount) =>
            ProfilerCore.CreateMarker(Utf8ToString(name, nameLen), categoryId, flags, metadataCount);

        public static IntPtr CreateMarker(char* name, int nameLen, ushort categoryId, MarkerFlags flags, int metadataCount) =>
            ProfilerCore.CreateMarker(new string(name, 0, nameLen), categoryId, flags, metadataCount);

        internal static IntPtr GetMarker(string name) => ProfilerCore.GetMarker(name);

        public static void SetMarkerMetadata(IntPtr markerPtr, int index, string name, byte type, byte unit) => ProfilerCore.SetMarkerMetadata(markerPtr, index, name);

        internal static void SetMarkerMetadata__Unmanaged(IntPtr markerPtr, int index, byte* name, int nameLen, byte type, byte unit) =>
            ProfilerCore.SetMarkerMetadata(markerPtr, index, Utf8ToString(name, nameLen));

        public static void SetMarkerMetadata(IntPtr markerPtr, int index, char* name, int nameLen, byte type, byte unit) =>
            ProfilerCore.SetMarkerMetadata(markerPtr, index, new string(name, 0, nameLen));

        public static void BeginSample(IntPtr markerPtr) => ProfilerCore.BeginSample(markerPtr);

        public static void BeginSampleWithMetadata(IntPtr markerPtr, int metadataCount, void* metadata) => ProfilerCore.BeginSample(markerPtr);

        public static void EndSample(IntPtr markerPtr) => ProfilerCore.EndSample(markerPtr);

        public static void SingleSampleWithMetadata(IntPtr markerPtr, int metadataCount, void* metadata) => ProfilerCore.SingleSample(markerPtr);

        public static void* CreateCounterValue(out IntPtr counterPtr, string name, ushort categoryId, MarkerFlags flags, byte dataType, byte dataUnit, int dataSize, ProfilerCounterOptions counterOptions) =>
            ProfilerCore.CreateCounterValue(out counterPtr, name, categoryId, flags, dataType, dataUnit, dataSize, counterOptions);

        internal static void* CreateCounterValue__Unmanaged(out IntPtr counterPtr, byte* name, int nameLen, ushort categoryId, MarkerFlags flags, byte dataType, byte dataUnit, int dataSize, ProfilerCounterOptions counterOptions) =>
            ProfilerCore.CreateCounterValue(out counterPtr, Utf8ToString(name, nameLen), categoryId, flags, dataType, dataUnit, dataSize, counterOptions);

        public static void* CreateCounterValue(out IntPtr counterPtr, char* name, int nameLen, ushort categoryId, MarkerFlags flags, byte dataType, byte dataUnit, int dataSize, ProfilerCounterOptions counterOptions) =>
            ProfilerCore.CreateCounterValue(out counterPtr, new string(name, 0, nameLen), categoryId, flags, dataType, dataUnit, dataSize, counterOptions);

        public static void FlushCounterValue(void* counterValuePtr) => ProfilerCore.FlushCounterValue(counterValuePtr);

        internal static string Utf8ToString(byte* chars, int charsLen) => chars == null ? null : Encoding.UTF8.GetString(chars, charsLen);

        static uint s_NextFlow;

        public static uint CreateFlow(ushort categoryId) => System.Threading.Interlocked.Increment(ref s_NextFlow);

        [Unbound.EditorFaithful("only the Profiler window shows it; nothing a game can read in Play mode depends on it")]
        public static void FlowEvent(uint flowId, ProfilerFlowEventType flowEventType) { }

        internal static void Internal_BeginWithObject(IntPtr markerPtr, UnityEngine.Object contextUnityObject) => ProfilerCore.BeginSample(markerPtr);

        internal static string Internal_GetName(IntPtr markerPtr) => ProfilerCore.StatOf(markerPtr)?.Name;
    }
}

namespace UnityEngine.Profiling
{
    /// <summary>The profiler: samples go to the recorders that listen; memory figures from the runtime.</summary>
    public sealed partial class Profiler
    {
        [Unbound.EditorFaithful("the Editor has the profiler")]
        public static bool supported => true;
        public static bool enabled { get; set; }
        public static bool enableBinaryLog { get; set; }
        public static string logFile { get; set; }
        public static int maxUsedMemory { get; set; }
        public static long usedHeapSizeLong => GC.GetTotalMemory(false);
        public static uint usedHeapSize => (uint)Math.Min(uint.MaxValue, GC.GetTotalMemory(false));

        [ThreadStatic] static Stack<IntPtr> t_Samples;

        /// <summary>A sample under a marker named <paramref name="name"/>.</summary>
        [Conditional("ENABLE_PROFILER")]
        public static void BeginSample(string name) => BeginSampleNamed(name);

        [Conditional("ENABLE_PROFILER")]
        public static void BeginSample(string name, Object targetObject) => BeginSampleNamed(name);

        static void BeginSampleNamed(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            var marker = ProfilerCore.CreateMarker(name, ProfilerUnsafeUtility.CategoryScripts, MarkerFlags.Script, 0);
            (t_Samples ??= new Stack<IntPtr>()).Push(marker);
            ProfilerCore.BeginSample(marker);
        }

        [Conditional("ENABLE_PROFILER")]
        public static void EndSample()
        {
            var samples = t_Samples;
            if (samples == null || samples.Count == 0) return;
            ProfilerCore.EndSample(samples.Pop());
        }

        [Unbound.EditorFaithful("only the Profiler window shows it; nothing a game can read in Play mode depends on it")]
        [Conditional("ENABLE_PROFILER")]
        public static void BeginThreadProfiling(string threadGroupName, string threadName) { }

        [Unbound.EditorFaithful("only the Profiler window shows it; nothing a game can read in Play mode depends on it")]
        public static void EndThreadProfiling() { }

        public static long GetRuntimeMemorySizeLong(Object o) { Unbound.Runtime.Stubs.Ignored("Profiler.GetRuntimeMemorySizeLong", "is not measured yet: it answers 0"); return 0; }
        public static long GetMonoHeapSizeLong() => GC.GetGCMemoryInfo().HeapSizeBytes;
        public static long GetMonoUsedSizeLong() => GC.GetTotalMemory(false);
        public static long GetTotalAllocatedMemoryLong() => Environment.WorkingSet;
        public static long GetTotalReservedMemoryLong() => System.Diagnostics.Process.GetCurrentProcess().VirtualMemorySize64;
        public static long GetTotalUnusedReservedMemoryLong() => Math.Max(0, GetTotalReservedMemoryLong() - GetTotalAllocatedMemoryLong());
        public static long GetAllocatedMemoryForGraphicsDriver() { Unbound.Runtime.Stubs.Ignored("Profiler.GetAllocatedMemoryForGraphicsDriver", "is not measured yet: it answers 0"); return 0; }
        public static uint GetTempAllocatorSize() { Unbound.Runtime.Stubs.Ignored("Profiler.GetTempAllocatorSize", "answers 0: Unbound has no native temp allocator"); return 0; }
        public static bool SetTempAllocatorRequestedSize(uint size) { Unbound.Runtime.Stubs.Ignored("Profiler.SetTempAllocatorRequestedSize", "changes nothing: Unbound has no native temp allocator"); return true; }

        internal const uint invalidProfilerArea = 4294967295U;

        public static int areaCount => Enum.GetNames(typeof(ProfilerArea)).Length;

        public static bool enableAllocationCallstacks { get; set; }

        [Unbound.EditorFaithful("obsolete: Unity made it a no-op answering 0")]
        [Obsolete("maxNumberOfSamplesPerFrame has been depricated. Use maxUsedMemory instead")]
        public static int maxNumberOfSamplesPerFrame
        {
            get => 0;
            set { }
        }

        static readonly HashSet<ProfilerArea> s_DisabledAreas = new HashSet<ProfilerArea>();
        static readonly HashSet<ushort> s_DisabledCategories = new HashSet<ushort>();

        public static void SetAreaEnabled(ProfilerArea area, bool enabled)
        {
            lock (s_DisabledAreas)
            {
                if (enabled) s_DisabledAreas.Remove(area);
                else s_DisabledAreas.Add(area);
            }
        }

        public static bool GetAreaEnabled(ProfilerArea area)
        {
            lock (s_DisabledAreas) return !s_DisabledAreas.Contains(area);
        }

        public static void SetCategoryEnabled(Unity.Profiling.ProfilerCategory category, bool enabled)
        {
            lock (s_DisabledCategories)
            {
                if (enabled) s_DisabledCategories.Remove(category);
                else s_DisabledCategories.Add(category);
            }
        }

        public static bool IsCategoryEnabled(Unity.Profiling.ProfilerCategory category)
        {
            lock (s_DisabledCategories) return !s_DisabledCategories.Contains(category);
        }

        public static uint GetCategoriesCount() => (uint)ProfilerCore.CategoryCount;

        public static void GetAllCategories(Unity.Profiling.ProfilerCategory[] categories)
        {
            for (int i = 0; i < categories.Length && i < ProfilerCore.CategoryCount; i++) categories[i] = new Unity.Profiling.ProfilerCategory((ushort)i);
        }

        public static void GetAllCategories(Unity.Collections.NativeArray<Unity.Profiling.ProfilerCategory> categories)
        {
            for (int i = 0; i < categories.Length && i < ProfilerCore.CategoryCount; i++) categories[i] = new Unity.Profiling.ProfilerCategory((ushort)i);
        }

        /// <summary>Frames of a capture file go to the Profiler window, which Unbound does not have.</summary>
        public static void AddFramesFromFile(string file) =>
            throw new NotSupportedException("Unbound has no Profiler window to load captured frames into");

        [Obsolete("GetRuntimeMemorySize has been deprecated since it is limited to 2GB. Please use GetRuntimeMemorySizeLong() instead.")]
        public static int GetRuntimeMemorySize(Object o) => (int)GetRuntimeMemorySizeLong(o);

        [Obsolete("GetMonoHeapSize has been deprecated since it is limited to 4GB. Please use GetMonoHeapSizeLong() instead.")]
        public static uint GetMonoHeapSize() => (uint)GetMonoHeapSizeLong();

        [Obsolete("GetMonoUsedSize has been deprecated since it is limited to 4GB. Please use GetMonoUsedSizeLong() instead.")]
        public static uint GetMonoUsedSize() => (uint)GetMonoUsedSizeLong();

        [Obsolete("GetTotalAllocatedMemory has been deprecated since it is limited to 4GB. Please use GetTotalAllocatedMemoryLong() instead.")]
        public static uint GetTotalAllocatedMemory() => (uint)GetTotalAllocatedMemoryLong();

        [Obsolete("GetTotalUnusedReservedMemory has been deprecated since it is limited to 4GB. Please use GetTotalUnusedReservedMemoryLong() instead.")]
        public static uint GetTotalUnusedReservedMemory() => (uint)GetTotalUnusedReservedMemoryLong();

        [Obsolete("GetTotalReservedMemory has been deprecated since it is limited to 4GB. Please use GetTotalReservedMemoryLong() instead.")]
        public static uint GetTotalReservedMemory() => (uint)GetTotalReservedMemoryLong();

        /// <summary>The native allocator's free-block histogram: Unbound's managed engine has none.</summary>
        public static long GetTotalFragmentationInfo(Unity.Collections.NativeArray<int> stats)
        {
            for (int i = 0; i < stats.Length; i++) stats[i] = 0;
            return 0;
        }

        // Frame and session metadata go into the profiler's capture stream: validated as Unity
        // validates them, then dropped (nothing captures).

        public static void EmitFrameMetaData(Guid id, int tag, Array data) => CheckMetaData(data);

        public static void EmitFrameMetaData<T>(Guid id, int tag, List<T> data) where T : struct => CheckMetaData<T>(data);

        public static unsafe void EmitFrameMetaData<T>(Guid id, int tag, Unity.Collections.NativeArray<T> data) where T : struct =>
            Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);

        [Conditional("ENABLE_PROFILER")]
        public static void EmitSessionMetaData(Guid id, int tag, Array data) => CheckMetaData(data);

        [Conditional("ENABLE_PROFILER")]
        public static void EmitSessionMetaData<T>(Guid id, int tag, List<T> data) where T : struct => CheckMetaData<T>(data);

        [Conditional("ENABLE_PROFILER")]
        public static unsafe void EmitSessionMetaData<T>(Guid id, int tag, Unity.Collections.NativeArray<T> data) where T : struct =>
            Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);

        static void CheckMetaData(Array data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var elementType = data.GetType().GetElementType();
            if (!Unity.Collections.LowLevel.Unsafe.UnsafeUtility.IsBlittable(elementType))
                throw new ArgumentException($"{elementType} type must be blittable");
        }

        static void CheckMetaData<T>(List<T> data) where T : struct
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!Unity.Collections.LowLevel.Unsafe.UnsafeUtility.IsBlittable(typeof(T)))
                throw new ArgumentException($"{typeof(T)} type must be blittable");
        }
    }

    /// <summary>A marker's last-frame time and sample count (the legacy recorder API).</summary>
    public sealed partial class Recorder
    {
        const Unity.Profiling.ProfilerRecorderOptions s_RecorderDefaultOptions = (Unity.Profiling.ProfilerRecorderOptions)153;

        internal static Recorder s_InvalidRecorder = new Recorder();

        Unity.Profiling.ProfilerRecorder m_RecorderCPU;
        Unity.Profiling.ProfilerRecorder m_RecorderGPU;

        internal Recorder()
        {
        }

        internal Recorder(ProfilerRecorderHandle handle)
        {
            if (!handle.Valid) return;
            m_RecorderCPU = new Unity.Profiling.ProfilerRecorder(handle, 1, s_RecorderDefaultOptions);
            if ((ProfilerRecorderHandle.GetDescription(handle).Flags & MarkerFlags.SampleGPU) != 0)
                m_RecorderGPU = new Unity.Profiling.ProfilerRecorder(handle, 1, (Unity.Profiling.ProfilerRecorderOptions)217);
        }

        ~Recorder()
        {
            m_RecorderCPU.Dispose();
            m_RecorderGPU.Dispose();
        }

        public bool isValid => m_RecorderCPU.handle != 0;

        public bool enabled
        {
            get => m_RecorderCPU.handle != 0 && m_RecorderCPU.IsRunning;
            set
            {
                if (m_RecorderCPU.handle == 0) return;
                if (value)
                {
                    m_RecorderCPU.Start();
                    if (m_RecorderGPU.Valid) m_RecorderGPU.Start();
                }
                else
                {
                    m_RecorderCPU.Stop();
                    if (m_RecorderGPU.Valid) m_RecorderGPU.Stop();
                }
            }
        }

        public long elapsedNanoseconds => m_RecorderCPU.Valid ? m_RecorderCPU.LastValue : 0;

        public long gpuElapsedNanoseconds => m_RecorderGPU.Valid ? m_RecorderGPU.LastValue : 0;

        public int sampleBlockCount => !m_RecorderCPU.Valid || m_RecorderCPU.Count != 1 ? 0 : (int)m_RecorderCPU.GetSample(0).Count;

        public int gpuSampleBlockCount => !m_RecorderGPU.Valid || m_RecorderGPU.Count != 1 ? 0 : (int)m_RecorderGPU.GetSample(0).Count;

        public static Recorder Get(string samplerName)
        {
            var handle = ProfilerRecorderHandle.Get(Unity.Profiling.ProfilerCategory.Any, samplerName);
            return handle.Valid ? new Recorder(handle) : s_InvalidRecorder;
        }

        public void FilterToCurrentThread()
        {
            if (m_RecorderCPU.Valid) m_RecorderCPU.FilterToCurrentThread();
        }

        public void CollectFromAllThreads()
        {
            if (m_RecorderCPU.Valid) m_RecorderCPU.CollectFromAllThreads();
        }
    }

    /// <summary>A marker, by name.</summary>
    public partial class Sampler
    {
        internal IntPtr m_Ptr;

        internal static Sampler s_InvalidSampler = new Sampler();

        internal Sampler()
        {
        }

        internal Sampler(IntPtr ptr)
        {
            m_Ptr = ptr;
        }

        public bool isValid => m_Ptr != IntPtr.Zero;

        public string name => ProfilerUnsafeUtility.Internal_GetName(m_Ptr);

        public Recorder GetRecorder() => new Recorder(new ProfilerRecorderHandle((ulong)m_Ptr.ToInt64()));

        public static Sampler Get(string name)
        {
            var marker = ProfilerUnsafeUtility.GetMarker(name);
            return marker == IntPtr.Zero ? s_InvalidSampler : new Sampler(marker);
        }

        public static int GetNames(List<string> names)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            if (names != null)
            {
                while (names.Count < handles.Count) names.Add(null);
                for (int i = 0; i < handles.Count; i++) names[i] = ProfilerRecorderHandle.GetDescription(handles[i]).Name;
            }
            return handles.Count;
        }
    }

    /// <summary>A marker scripts sample with Begin/End.</summary>
    public sealed partial class CustomSampler : Sampler
    {
        internal static partial class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(CustomSampler customSampler) => customSampler.m_Ptr;
        }

        internal static CustomSampler s_InvalidCustomSampler = new CustomSampler();

        internal CustomSampler()
        {
        }

        CustomSampler(IntPtr ptr) : base(ptr)
        {
        }

        public static CustomSampler Create(string name, bool collectGpuData = false)
        {
            var marker = ProfilerUnsafeUtility.CreateMarker(name, ProfilerUnsafeUtility.CategoryScripts, (MarkerFlags)(8 | (collectGpuData ? 256 : 0)), 0);
            return marker == IntPtr.Zero ? s_InvalidCustomSampler : new CustomSampler(marker);
        }

        [Conditional("ENABLE_PROFILER")]
        public void Begin() => ProfilerUnsafeUtility.BeginSample(m_Ptr);

        [Conditional("ENABLE_PROFILER")]
        public void Begin(Object targetObject) => ProfilerUnsafeUtility.Internal_BeginWithObject(m_Ptr, targetObject);

        [Conditional("ENABLE_PROFILER")]
        public void End() => ProfilerUnsafeUtility.EndSample(m_Ptr);
    }
}

namespace Unity.Profiling
{
    public enum ProfilerCategoryColor : ushort
    {
        Render = 0,
        Scripts = 1,
        BurstJobs = 2,
        Other = 3,
        Physics = 4,
        Animation = 5,
        Audio = 6,
        AudioJob = 7,
        AudioUpdateJob = 8,
        Lighting = 9,
        GC = 10,
        VSync = 11,
        Memory = 12,
        Internal = 13,
        UI = 14,
        Build = 15,
        Input = 16,
    }

    [Flags]
    public enum ProfilerCategoryFlags : ushort
    {
        None = 0,
        Builtin = 1,
    }

    public enum ProfilerFlowEventType : byte
    {
        Begin = 0,
        ParallelNext = 1,
        End = 2,
        Next = 3,
    }

    public enum ProfilerMarkerDataUnit : byte
    {
        Undefined = 0,
        TimeNanoseconds = 1,
        Bytes = 2,
        Count = 3,
        Percent = 4,
        FrequencyHz = 5,
    }

    [Flags]
    public enum ProfilerRecorderOptions
    {
        None = 0,
        StartImmediately = 1,
        KeepAliveDuringDomainReload = 2,
        CollectOnlyOnCurrentThread = 4,
        WrapAroundWhenCapacityReached = 8,
        SumAllSamplesInFrame = 16,
        GpuRecorder = 64,
        Default = 24,
    }
}

namespace Unity.Profiling.LowLevel
{
    public enum ProfilerMarkerDataType : byte
    {
        InstanceId = 1,
        Int32 = 2,
        UInt32 = 3,
        Int64 = 4,
        UInt64 = 5,
        Float = 6,
        Double = 7,
        String16 = 9,
        Blob8 = 11,
        GfxResourceId = 12,
    }
}

namespace UnityEngine.Profiling
{
    public enum ProfilerArea
    {
        CPU = 0,
        GPU = 1,
        Rendering = 2,
        Memory = 3,
        Audio = 4,
        Video = 5,
        Physics = 6,
        Physics2D = 7,
        NetworkMessages = 8,
        NetworkOperations = 9,
        UI = 10,
        UIDetails = 11,
        GlobalIllumination = 12,
        VirtualTexturing = 13,
    }
}

namespace Unity.Profiling
{
    /// <summary>A screenshot taken with a memory snapshot.</summary>
    public partial struct DebugScreenCapture
    {
        public Unity.Collections.NativeArray<byte> RawImageDataReference { get; set; }
        public UnityEngine.TextureFormat ImageFormat { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
