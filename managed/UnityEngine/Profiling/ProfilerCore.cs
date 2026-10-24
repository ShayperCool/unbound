using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Unbound.Runtime.Profiling
{
    /// <summary>
    /// The engine side of Unity's profiler API: categories, markers and counters (stats), the
    /// samples of markers per thread, and the recorders that read stats back frame by frame
    /// (ProfilerRecorder, Recorder). There is no profiler window: what is recorded is what scripts
    /// and packages ask recorders for.
    /// </summary>
    internal static unsafe class ProfilerCore
    {
        // ------------------------------------------------------------------ categories

        internal sealed class Category
        {
            public ushort Id;
            public string Name;
            public Color32 Color;
            public ushort Flags;
            public byte* NameUtf8;
            public int NameUtf8Length;
        }

        static readonly object s_Lock = new object();
        static readonly List<Category> s_Categories = new List<Category>();
        static readonly Dictionary<string, Category> s_CategoriesByName = new Dictionary<string, Category>();

        /// <summary>The colors of <see cref="ProfilerCategoryColor"/>, as the Editor shows them.</summary>
        static readonly Color32[] s_Colors =
        {
            new Color32(123, 158, 5, 255), new Color32(52, 136, 167, 255), new Color32(46, 204, 113, 255), new Color32(122, 123, 30, 255),
            new Color32(204, 112, 0, 255), new Color32(90, 178, 188, 255), new Color32(80, 160, 93, 255), new Color32(113, 66, 183, 255),
            new Color32(162, 66, 183, 255), new Color32(136, 40, 7, 255), new Color32(114, 104, 12, 255), new Color32(197, 162, 6, 255),
            new Color32(178, 25, 76, 255), new Color32(100, 100, 100, 191), new Color32(97, 73, 133, 255), new Color32(78, 201, 176, 255),
            new Color32(78, 201, 240, 255),
        };

        /// <summary>Unity 6000.0's built-in categories, in id order, with their colors.</summary>
        static readonly (string Name, ProfilerCategoryColor Color)[] s_Builtin =
        {
            ("Render", ProfilerCategoryColor.Render), ("Scripts", ProfilerCategoryColor.Scripts), ("Managed Jobs", ProfilerCategoryColor.Scripts),
            ("Burst Jobs", ProfilerCategoryColor.BurstJobs), ("Gui", ProfilerCategoryColor.Other), ("Physics", ProfilerCategoryColor.Physics),
            ("Animation", ProfilerCategoryColor.Animation), ("Ai", ProfilerCategoryColor.Other), ("Audio", ProfilerCategoryColor.Audio),
            ("Audio Job", ProfilerCategoryColor.AudioJob), ("Audio Update Job", ProfilerCategoryColor.AudioUpdateJob), ("Video", ProfilerCategoryColor.Other),
            ("Particles", ProfilerCategoryColor.Other), ("Lighting", ProfilerCategoryColor.Lighting), ("Network", ProfilerCategoryColor.Other),
            ("Loading", ProfilerCategoryColor.Other), ("Other", ProfilerCategoryColor.Other), ("GC", ProfilerCategoryColor.GC),
            ("VSync", ProfilerCategoryColor.VSync), ("Overhead", ProfilerCategoryColor.Other), ("PlayerLoop", ProfilerCategoryColor.Other),
            ("Director", ProfilerCategoryColor.Other), ("Vr", ProfilerCategoryColor.Other), ("Memory", ProfilerCategoryColor.Memory),
            ("Internal", ProfilerCategoryColor.Internal), ("FileIO", ProfilerCategoryColor.Other), ("UI Layout", ProfilerCategoryColor.UI),
            ("UI Render", ProfilerCategoryColor.UI), ("VFX", ProfilerCategoryColor.Render), ("Build Interface", ProfilerCategoryColor.Build),
            ("Input", ProfilerCategoryColor.Input), ("Virtual Texturing", ProfilerCategoryColor.Other), ("GPU", ProfilerCategoryColor.Other),
            ("Physics2D", ProfilerCategoryColor.Other), ("Network Operations", ProfilerCategoryColor.Other), ("UI Details", ProfilerCategoryColor.Other),
            ("Debug", ProfilerCategoryColor.Other), ("Jobs", ProfilerCategoryColor.Other), ("Text", ProfilerCategoryColor.UI),
        };

        static ProfilerCore()
        {
            foreach (var (name, color) in s_Builtin) AddCategory(name, color);
            RegisterBuiltinStats();
        }

        internal static Color32 ColorOf(ProfilerCategoryColor color) => (int)color < s_Colors.Length ? s_Colors[(int)color] : s_Colors[(int)ProfilerCategoryColor.Other];

        static Category AddCategory(string name, ProfilerCategoryColor color)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            var utf8 = (byte*)NativeMemory.Alloc((nuint)bytes.Length + 1);
            bytes.CopyTo(new Span<byte>(utf8, bytes.Length));
            utf8[bytes.Length] = 0;
            var category = new Category
            {
                Id = (ushort)s_Categories.Count,
                Name = name,
                Color = ColorOf(color),
                NameUtf8 = utf8,
                NameUtf8Length = bytes.Length,
            };
            s_Categories.Add(category);
            s_CategoriesByName[name] = category;
            return category;
        }

        /// <summary>A category by name: the existing one, or a new one with the given color.</summary>
        internal static ushort CreateCategory(string name, ProfilerCategoryColor color)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Category name must not be null or empty");
            lock (s_Lock)
                return s_CategoriesByName.TryGetValue(name, out var existing) ? existing.Id : AddCategory(name, color).Id;
        }

        internal static ushort CategoryByName(string name)
        {
            lock (s_Lock)
                return s_CategoriesByName.TryGetValue(name, out var category) ? category.Id : ushort.MaxValue;
        }

        internal static Category CategoryOf(ushort id)
        {
            lock (s_Lock)
                return id < s_Categories.Count ? s_Categories[id] : null;
        }

        internal static int CategoryCount
        {
            get
            {
                lock (s_Lock) return s_Categories.Count;
            }
        }

        // ------------------------------------------------------------------ stats: markers and counters

        /// <summary>A marker (samples timed per thread) or a counter (a value scripts set).</summary>
        internal sealed class Stat
        {
            public int Index;
            public string Name;
            public ushort Category;
            public MarkerFlags Flags;
            public ProfilerMarkerDataType DataType = ProfilerMarkerDataType.Int64;
            public ProfilerMarkerDataUnit Unit = ProfilerMarkerDataUnit.TimeNanoseconds;
            public byte* NameUtf8;
            public int NameUtf8Length;
            public bool IsCounter;
            /// <summary>A counter's value in native memory.</summary>
            public byte* Value;
            public int ValueSize;
            public ProfilerCounterOptions CounterOptions;
            public bool Flushed;
            /// <summary>An engine stat sampled at the end of each frame.</summary>
            public Func<long> Sampler;
            /// <summary>The recorders attached; samples are only taken while there are running ones.</summary>
            public volatile Recorder[] Recorders = Array.Empty<Recorder>();
            public string[] MetadataNames;

            public IntPtr Handle => (IntPtr)(Index + 1);
        }

        static readonly List<Stat> s_Stats = new List<Stat>();
        static readonly Dictionary<string, Stat> s_StatsByName = new Dictionary<string, Stat>();

        static Stat NewStat(string name, ushort category, MarkerFlags flags)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            var utf8 = (byte*)NativeMemory.Alloc((nuint)bytes.Length + 1);
            bytes.CopyTo(new Span<byte>(utf8, bytes.Length));
            utf8[bytes.Length] = 0;
            var stat = new Stat
            {
                Index = s_Stats.Count,
                Name = name,
                Category = category,
                Flags = flags,
                NameUtf8 = utf8,
                NameUtf8Length = bytes.Length,
            };
            s_Stats.Add(stat);
            s_StatsByName[name] = stat;
            // Recorders made before the stat existed start receiving its samples now.
            foreach (var recorder in s_Recorders.Values)
                if (recorder.Stat == null && recorder.PendingName == name && (recorder.PendingCategory == ushort.MaxValue || recorder.PendingCategory == category))
                    Attach(recorder, stat);
            return stat;
        }

        /// <summary>The marker of a name (the same one for every call with that name).</summary>
        internal static IntPtr CreateMarker(string name, ushort category, MarkerFlags flags, int metadataCount)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            lock (s_Lock)
            {
                if (s_StatsByName.TryGetValue(name, out var existing)) return existing.Handle;
                var stat = NewStat(name, category, flags);
                if (metadataCount > 0) stat.MetadataNames = new string[metadataCount];
                return stat.Handle;
            }
        }

        internal static IntPtr GetMarker(string name)
        {
            lock (s_Lock)
                return name != null && s_StatsByName.TryGetValue(name, out var stat) && !stat.IsCounter ? stat.Handle : IntPtr.Zero;
        }

        internal static Stat StatOf(IntPtr handle) => StatOf((ulong)(long)handle);

        internal static Stat StatOf(ulong handle)
        {
            if (handle == 0 || handle > int.MaxValue) return null;
            lock (s_Lock)
                return (int)handle - 1 < s_Stats.Count ? s_Stats[(int)handle - 1] : null;
        }

        internal static void SetMarkerMetadata(IntPtr marker, int index, string name)
        {
            var stat = StatOf(marker);
            if (stat == null) return;
            lock (s_Lock)
            {
                if (stat.MetadataNames == null || index >= stat.MetadataNames.Length) Array.Resize(ref stat.MetadataNames, index + 1);
                stat.MetadataNames[index] = name;
            }
        }

        /// <summary>A counter's value memory (shared by every counter of the same name).</summary>
        internal static void* CreateCounterValue(out IntPtr counterPtr, string name, ushort category, MarkerFlags flags, byte dataType, byte dataUnit, int dataSize, ProfilerCounterOptions options)
        {
            lock (s_Lock)
            {
                if (!s_StatsByName.TryGetValue(name, out var stat))
                {
                    // Counters made by scripts are script counters, as the Editor reports them.
                    stat = NewStat(name, category, flags | MarkerFlags.Script | MarkerFlags.Counter);
                    stat.IsCounter = true;
                    stat.DataType = (ProfilerMarkerDataType)dataType;
                    stat.Unit = (ProfilerMarkerDataUnit)dataUnit;
                    stat.ValueSize = Math.Max(dataSize, 8);
                    stat.Value = (byte*)NativeMemory.AllocZeroed((nuint)stat.ValueSize);
                    stat.CounterOptions = options;
                }
                counterPtr = stat.Handle;
                return stat.Value;
            }
        }

        /// <summary>A counter's value is final for the frame (ProfilerCounter.Sample).</summary>
        internal static void FlushCounterValue(void* value)
        {
            lock (s_Lock)
            {
                foreach (var stat in s_Stats)
                {
                    if (stat.Value != value) continue;
                    stat.Flushed = true;
                    return;
                }
            }
        }

        internal static List<Stat> AllStats()
        {
            lock (s_Lock) return new List<Stat>(s_Stats);
        }

        internal static Stat StatByName(ushort category, string name)
        {
            lock (s_Lock)
                // Stat names are unique: the category does not narrow the lookup.
                return s_StatsByName.TryGetValue(name, out var stat) ? stat : null;
        }

        // ------------------------------------------------------------------ samples

        [ThreadStatic] static List<(Stat Stat, long Start)> t_Open;

        internal static void BeginSample(IntPtr marker)
        {
            var stat = StatOf(marker);
            if (stat == null || stat.Recorders.Length == 0) return;
            (t_Open ??= new List<(Stat, long)>()).Add((stat, Stopwatch.GetTimestamp()));
        }

        internal static void EndSample(IntPtr marker)
        {
            var open = t_Open;
            if (open == null || open.Count == 0) return;
            long now = Stopwatch.GetTimestamp();
            var stat = StatOf(marker);
            for (int i = open.Count - 1; i >= 0; i--)
            {
                if (open[i].Stat != stat) continue;
                long nanoseconds = (now - open[i].Start) * 1_000_000_000L / Stopwatch.Frequency;
                open.RemoveAt(i);
                Record(stat, nanoseconds);
                return;
            }
        }

        /// <summary>A sample with no duration (SingleSampleWithMetadata).</summary>
        internal static void SingleSample(IntPtr marker)
        {
            var stat = StatOf(marker);
            if (stat != null && stat.Recorders.Length != 0) Record(stat, 0);
        }

        static void Record(Stat stat, long value)
        {
            int thread = Environment.CurrentManagedThreadId;
            foreach (var recorder in stat.Recorders)
            {
                if (!recorder.Running || (recorder.CurrentThreadOnly && recorder.Thread != thread)) continue;
                lock (recorder)
                {
                    if (recorder.Sum)
                    {
                        recorder.FrameValue += value;
                        recorder.FrameCount++;
                    }
                    else
                    {
                        recorder.Push(value, 1);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ recorders

        internal sealed class Recorder
        {
            public ulong Handle;
            public Stat Stat;
            public string PendingName;
            public ushort PendingCategory;
            public bool Running;
            public bool Sum;
            public bool Wrap;
            public bool CurrentThreadOnly;
            public int Thread;
            public long FrameValue;
            public long FrameCount;
            public ProfilerRecorderSample[] Samples;
            public int Start;
            public int Count;
            public bool Wrapped;

            public void Push(long value, long count)
            {
                if (Samples.Length == 0) return;
                if (Count == Samples.Length)
                {
                    if (!Wrap) return;
                    Samples[Start] = MakeSample(value, count);
                    Start = (Start + 1) % Samples.Length;
                    Wrapped = true;
                    return;
                }
                Samples[(Start + Count) % Samples.Length] = MakeSample(value, count);
                Count++;
            }

            public ProfilerRecorderSample Sample(int index) => Samples[(Start + index) % Samples.Length];
        }

        static ProfilerRecorderSample MakeSample(long value, long count)
        {
            var sample = new ProfilerRecorderSample();
            var raw = (long*)&sample;
            raw[0] = value;
            raw[1] = count;
            return sample;
        }

        static readonly Dictionary<ulong, Recorder> s_Recorders = new Dictionary<ulong, Recorder>();
        static ulong s_NextRecorder;

        internal static ulong CreateRecorder(ulong statHandle, ushort pendingCategory, string pendingName, int capacity, ProfilerRecorderOptions options)
        {
            lock (s_Lock)
            {
                var recorder = new Recorder
                {
                    Handle = ++s_NextRecorder,
                    Sum = (options & ProfilerRecorderOptions.SumAllSamplesInFrame) != 0,
                    Wrap = (options & ProfilerRecorderOptions.WrapAroundWhenCapacityReached) != 0,
                    CurrentThreadOnly = (options & ProfilerRecorderOptions.CollectOnlyOnCurrentThread) != 0,
                    Thread = Environment.CurrentManagedThreadId,
                    Samples = new ProfilerRecorderSample[Math.Max(0, capacity)],
                    PendingName = pendingName,
                    PendingCategory = pendingCategory,
                };
                s_Recorders[recorder.Handle] = recorder;
                var stat = StatOf(statHandle) ?? (pendingName != null && s_StatsByName.TryGetValue(pendingName, out var named) ? named : null);
                if (stat != null) Attach(recorder, stat);
                if ((options & ProfilerRecorderOptions.StartImmediately) != 0) recorder.Running = true;
                if (stat?.Name == "GC.Alloc") WarnGcAlloc();
                return recorder.Handle;
            }
        }

        static void Attach(Recorder recorder, Stat stat)
        {
            recorder.Stat = stat;
            var recorders = new List<Recorder>(stat.Recorders) { recorder };
            stat.Recorders = recorders.ToArray();
        }

        static int s_WarnedGcAlloc;

        static void WarnGcAlloc()
        {
            if (Interlocked.Exchange(ref s_WarnedGcAlloc, 1) == 0)
                UnityEngine.Debug.LogWarning("Unbound: the GC.Alloc marker records no samples (the runtime does not report each managed allocation); recorders of it read 0");
        }

        internal static Recorder RecorderOf(ulong handle)
        {
            lock (s_Lock) return s_Recorders.TryGetValue(handle, out var recorder) ? recorder : null;
        }

        internal static void ReleaseRecorder(ulong handle)
        {
            lock (s_Lock)
            {
                if (!s_Recorders.Remove(handle, out var recorder) || recorder.Stat == null) return;
                var recorders = new List<Recorder>(recorder.Stat.Recorders);
                recorders.Remove(recorder);
                recorder.Stat.Recorders = recorders.ToArray();
            }
        }

        /// <summary>A recorder's current value: a counter's value, or what its marker summed so far
        /// this frame.</summary>
        internal static long CurrentValue(Recorder recorder)
        {
            var stat = recorder.Stat;
            if (stat == null) return 0;
            if (stat.IsCounter) return ReadValue(stat);
            if (stat.Sampler != null) return stat.Sampler();
            lock (recorder) return recorder.FrameValue;
        }

        static long ReadValue(Stat stat)
        {
            switch (stat.DataType)
            {
                case ProfilerMarkerDataType.Int32: return *(int*)stat.Value;
                case ProfilerMarkerDataType.UInt32: return *(uint*)stat.Value;
                case ProfilerMarkerDataType.Float: return (long)*(float*)stat.Value;
                case ProfilerMarkerDataType.Double: return (long)*(double*)stat.Value;
                default: return *(long*)stat.Value;
            }
        }

        internal static double ReadValueAsDouble(Recorder recorder)
        {
            var stat = recorder.Stat;
            if (stat != null && stat.IsCounter)
            {
                if (stat.DataType == ProfilerMarkerDataType.Float) return *(float*)stat.Value;
                if (stat.DataType == ProfilerMarkerDataType.Double) return *(double*)stat.Value;
            }
            return CurrentValue(recorder);
        }

        internal static double AsDouble(Recorder recorder, long value)
        {
            var stat = recorder.Stat;
            if (stat != null && stat.IsCounter && (stat.DataType == ProfilerMarkerDataType.Float || stat.DataType == ProfilerMarkerDataType.Double))
                return BitConverter.Int64BitsToDouble(value);
            return value;
        }

        // ------------------------------------------------------------------ frames

        static long s_FrameStart = Stopwatch.GetTimestamp();
        static long s_LastFrameNanoseconds;
        static long s_AllocatedAtFrameStart = GC.GetTotalAllocatedBytes();
        static long s_AllocatedInLastFrame;

        static void RegisterBuiltinStats()
        {
            lock (s_Lock)
            {
                AddEngineStat("Main Thread", ProfilerUnsafeUtility.CategoryInternal, ProfilerMarkerDataUnit.TimeNanoseconds, () => s_LastFrameNanoseconds);
                AddEngineStat("CPU Main Thread Frame Time", ProfilerUnsafeUtility.CategoryInternal, ProfilerMarkerDataUnit.TimeNanoseconds, () => s_LastFrameNanoseconds);
                AddEngineStat("CPU Total Frame Time", ProfilerUnsafeUtility.CategoryInternal, ProfilerMarkerDataUnit.TimeNanoseconds, () => s_LastFrameNanoseconds);
                AddEngineStat("GC Reserved Memory", ProfilerUnsafeUtility.CategoryAllocation, ProfilerMarkerDataUnit.Bytes, () => GC.GetGCMemoryInfo().TotalCommittedBytes);
                AddEngineStat("GC Used Memory", ProfilerUnsafeUtility.CategoryAllocation, ProfilerMarkerDataUnit.Bytes, () => GC.GetTotalMemory(false));
                AddEngineStat("GC Allocated In Frame", ProfilerUnsafeUtility.CategoryAllocation, ProfilerMarkerDataUnit.Bytes, () => s_AllocatedInLastFrame);
                AddEngineStat("System Used Memory", ProfilerUnsafeUtility.CategoryAllocation, ProfilerMarkerDataUnit.Bytes, () => Environment.WorkingSet);
                AddEngineStat("Total Used Memory", ProfilerUnsafeUtility.CategoryAllocation, ProfilerMarkerDataUnit.Bytes, () => Environment.WorkingSet);
                AddEngineStat("Total Reserved Memory", ProfilerUnsafeUtility.CategoryAllocation, ProfilerMarkerDataUnit.Bytes, () => Process.GetCurrentProcess().VirtualMemorySize64);
                var gcAlloc = NewStat("GC.Alloc", ProfilerUnsafeUtility.CategoryAllocation, MarkerFlags.Default);
                gcAlloc.Unit = ProfilerMarkerDataUnit.TimeNanoseconds;
            }
        }

        static void AddEngineStat(string name, ushort category, ProfilerMarkerDataUnit unit, Func<long> sampler)
        {
            var stat = NewStat(name, category, unit == ProfilerMarkerDataUnit.Bytes ? MarkerFlags.Counter : MarkerFlags.Default);
            stat.Unit = unit;
            stat.Sampler = sampler;
        }

        /// <summary>The end of a frame: running recorders take the frame's sample.</summary>
        internal static void EndFrame()
        {
            long now = Stopwatch.GetTimestamp();
            s_LastFrameNanoseconds = (now - s_FrameStart) * 1_000_000_000L / Stopwatch.Frequency;
            s_FrameStart = now;
            long allocated = GC.GetTotalAllocatedBytes();
            s_AllocatedInLastFrame = allocated - s_AllocatedAtFrameStart;
            s_AllocatedAtFrameStart = allocated;
            Recorder[] recorders;
            lock (s_Lock)
            {
                recorders = new Recorder[s_Recorders.Count];
                s_Recorders.Values.CopyTo(recorders, 0);
            }
            foreach (var recorder in recorders)
            {
                var stat = recorder.Stat;
                if (!recorder.Running || stat == null) continue;
                lock (recorder)
                {
                    if (stat.Sampler != null) recorder.Push(stat.Sampler(), 1);
                    else if (stat.IsCounter) recorder.Push(ReadValue(stat), 1);
                    else if (recorder.Sum) recorder.Push(recorder.FrameValue, recorder.FrameCount);
                    recorder.FrameValue = 0;
                    recorder.FrameCount = 0;
                }
            }
            lock (s_Lock)
            {
                foreach (var stat in s_Stats)
                {
                    if (!stat.IsCounter) continue;
                    if ((stat.CounterOptions & ProfilerCounterOptions.ResetToZeroOnFlush) != 0) NativeMemory.Clear(stat.Value, (nuint)stat.ValueSize);
                    stat.Flushed = false;
                }
            }
        }
    }
}
