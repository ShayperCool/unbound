using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>Profiler markers, counters and recorders as scripts and packages read them back.</summary>
public static unsafe class ProfilingProbe
{
    static readonly ProfilerMarker s_Marker = new ProfilerMarker(ProfilerCategory.Scripts, "Probe.Marker");
    static readonly ProfilerMarker s_JobMarker = new ProfilerMarker("Probe.JobMarker");
    static readonly ProfilerCounterValue<int> s_Counter =
        new ProfilerCounterValue<int>(ProfilerCategory.Scripts, "Probe.Counter", ProfilerMarkerDataUnit.Count, ProfilerCounterOptions.FlushOnEndOfFrame);

    public struct MarkedJob : IJobParallelFor
    {
        public void Execute(int index)
        {
            s_JobMarker.Begin();
            s_JobMarker.End();
        }
    }

    public static void RunAll()
    {
        // Runtimes initialize the counter (a static field) at different times; make it exist now.
        s_Counter.Value = 0;
        ProbeLog.Begin("profiling.categories");
        ProbeLog.Value("render", ProfilerCategory.Render.Name);
        ProbeLog.Value("scripts", ProfilerCategory.Scripts.Name);
        ProbeLog.Value("gui", ProfilerCategory.Gui.Name);
        ProbeLog.Value("physics2d", ProfilerCategory.Physics2D.Name);
        ProbeLog.Value("vr", ProfilerCategory.Vr.Name);
        ProbeLog.Value("fileIO", ProfilerCategory.FileIO.Name);
        ProbeLog.Value("internal", ProfilerCategory.Internal.Name);
        ProbeLog.Value("render.color", ProfilerCategory.Render.Color.ToString());
        ProbeLog.Value("scripts.color", ProfilerCategory.Scripts.Color.ToString());
        ProbeLog.Value("memory.color", ProfilerCategory.Memory.Color.ToString());
        var custom = new ProfilerCategory("Probe.Category");
        ProbeLog.Value("custom.name", custom.Name);
        ProbeLog.Value("custom.color", custom.Color.ToString());
        ProbeLog.Value("custom.sameId", (ushort)custom == (ushort)new ProfilerCategory("Probe.Category"));
        var colored = new ProfilerCategory("Probe.Colored", ProfilerCategoryColor.Animation);
        ProbeLog.Value("colored.color", colored.Color.ToString());
        ProbeLog.Value("render.id", (int)(ushort)ProfilerCategory.Render);
        ProbeLog.Value("scripts.id", (int)(ushort)ProfilerCategory.Scripts);
        ProbeLog.Value("memory.id", (int)(ushort)ProfilerCategory.Memory);
        var description = ProfilerUnsafeUtility.GetCategoryDescription(ProfilerCategory.Loading);
        ProbeLog.Value("description.name", description.Name);
        ProbeLog.Value("description.id", (int)description.Id);
        fixed (char* audio = "Audio") ProbeLog.Value("byName", (int)ProfilerUnsafeUtility.GetCategoryByName(audio, 5));
        for (ushort id = 0; id < 40; id++)
        {
            var category = ProfilerUnsafeUtility.GetCategoryDescription(id);
            ProbeLog.Value("builtin" + id, category.Name + "|" + category.Color.ToString());
        }

        ProbeLog.Begin("profiling.lowlevel");
        long t0 = ProfilerUnsafeUtility.Timestamp;
        long t1 = ProfilerUnsafeUtility.Timestamp;
        ProbeLog.Value("timestampIncreases", t1 >= t0 && t0 > 0);
        var ratio = ProfilerUnsafeUtility.TimestampToNanosecondsConversionRatio;
        ProbeLog.Value("ratioPositive", ratio.Numerator > 0 && ratio.Denominator > 0);
        var marker = ProfilerUnsafeUtility.CreateMarker("Probe.Low", ProfilerUnsafeUtility.CategoryScripts, MarkerFlags.Default, 1);
        ProbeLog.Value("marker", marker != System.IntPtr.Zero);
        ProbeLog.Value("markerSame", ProfilerUnsafeUtility.CreateMarker("Probe.Low", ProfilerUnsafeUtility.CategoryScripts, MarkerFlags.Default, 1) == marker);
        ProfilerUnsafeUtility.SetMarkerMetadata(marker, 0, "value", (byte)ProfilerMarkerDataType.Int32, (byte)ProfilerMarkerDataUnit.Count);
        int metadata = 5;
        var data = new ProfilerMarkerData { Type = (byte)ProfilerMarkerDataType.Int32, Size = 4, Ptr = &metadata };
        ProfilerUnsafeUtility.BeginSampleWithMetadata(marker, 1, &data);
        ProfilerUnsafeUtility.EndSample(marker);
        ProfilerUnsafeUtility.SingleSampleWithMetadata(marker, 1, &data);
        var markerRecorder = new ProfilerRecorder(ProfilerCategory.Scripts, "Probe.Marker");
        var handle = FindHandle("Probe.Marker");
        ProbeLog.Value("handle.valid", handle.Valid);
        var markerDescription = ProfilerRecorderHandle.GetDescription(handle);
        ProbeLog.Value("handle.name", markerDescription.Name);
        ProbeLog.Value("handle.category", markerDescription.Category.Name);
        ProbeLog.Value("handle.dataType", markerDescription.DataType.ToString());
        ProbeLog.Value("handle.unit", markerDescription.UnitType.ToString());
        markerRecorder.Dispose();
        var counterHandle = FindHandle("Probe.Counter");
        var counterDescription = ProfilerRecorderHandle.GetDescription(counterHandle);
        ProbeLog.Value("counter.dataType", counterDescription.DataType.ToString());
        ProbeLog.Value("counter.unit", counterDescription.UnitType.ToString());
        ProbeLog.Value("counter.flags", counterDescription.Flags.ToString());
        var available = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(available);
        bool hasMarker = false;
        foreach (var h in available)
            if (ProfilerRecorderHandle.GetDescription(h).Name == "Probe.Marker") hasMarker = true;
        ProbeLog.Value("available.hasMarker", hasMarker);
        ProbeLog.Value("missing.valid", FindHandle("Probe.NoSuchMarker").Valid);

        ProbeLog.Begin("profiling.profiler");
        ProbeLog.Value("supported", Profiler.supported);
        ProbeLog.Value("enabled", Profiler.enabled);
        ProbeLog.Value("areaCount", System.Enum.GetValues(typeof(ProfilerArea)).Length);
        Profiler.BeginSample("Probe.Sample");
        Profiler.EndSample();
        ProbeLog.Value("monoHeapPositive", Profiler.GetMonoHeapSizeLong() > 0);
        ProbeLog.Value("totalAllocatedPositive", Profiler.GetTotalAllocatedMemoryLong() > 0);
    }

    public static IEnumerator AcrossFrames()
    {
        ProbeLog.Begin("profiling.recorders");
        var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Probe.Marker", 4);
        var jobRecorder = new ProfilerRecorder("Probe.JobMarker", 4, ProfilerRecorderOptions.Default);
        jobRecorder.Start();
        var counter = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Probe.Counter");
        var idle = new ProfilerRecorder(ProfilerCategory.Scripts, "Probe.Marker");
        var missing = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Probe.NoSuchMarker");
        var mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
        var gcReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
        var systemUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        var sampler = CustomSampler.Create("Probe.Sampler");
        var samplerRecorder = sampler.GetRecorder();
        samplerRecorder.enabled = true;
        var legacy = Recorder.Get("Probe.Marker");
        legacy.enabled = true;
        ProbeLog.Value("recorder.valid", recorder.Valid);
        ProbeLog.Value("recorder.isRunning", recorder.IsRunning);
        ProbeLog.Value("recorder.capacity", recorder.Capacity);
        ProbeLog.Value("recorder.dataType", recorder.DataType.ToString());
        ProbeLog.Value("recorder.unit", recorder.UnitType.ToString());
        ProbeLog.Value("idle.isRunning", idle.IsRunning);
        ProbeLog.Value("missing.valid", missing.Valid);
        ProbeLog.Value("missing.isRunning", missing.IsRunning);
        ProbeLog.Value("sampler.isValid", sampler.isValid);
        ProbeLog.Value("sampler.name", sampler.name);
        ProbeLog.Value("legacy.isValid", legacy.isValid);
        yield return null;
        for (int i = 0; i < 3; i++)
        {
            s_Marker.Begin();
            System.Threading.Thread.Sleep(1);
            s_Marker.End();
        }
        using (s_Marker.Auto()) { }
        sampler.Begin();
        sampler.End();
        sampler.Begin();
        sampler.End();
        s_Counter.Value = 17;
        new MarkedJob().Schedule(10, 1).Complete();
        ProbeLog.Value("counter.currentSameFrame", counter.CurrentValue);
        ProbeLog.Value("recorder.countSameFrame", recorder.Count);
        yield return null;
        yield return null;
        ProbeLog.Value("recorder.count", recorder.Count);
        ProbeLog.Value("recorder.lastPositive", recorder.LastValue > 0);
        ProbeLog.Value("recorder.lastAtLeast3ms", recorder.LastValue >= 3000000 || HasSampleAtLeast(recorder, 3000000));
        ProbeLog.Value("recorder.sampleCounts", SampleCounts(recorder));
        ProbeLog.Value("job.sampleCounts", SampleCounts(jobRecorder));
        ProbeLog.Value("counter.current", counter.CurrentValue);
        ProbeLog.Value("counter.last", counter.LastValue);
        ProbeLog.Value("counter.dataType", counter.DataType.ToString());
        ProbeLog.Value("mainThread.valid", mainThread.Valid);
        ProbeLog.Value("mainThread.lastPositive", mainThread.LastValue > 0);
        ProbeLog.Value("gcReserved.valid", gcReserved.Valid);
        ProbeLog.Value("gcReserved.positive", gcReserved.LastValue > 0);
        ProbeLog.Value("systemUsed.valid", systemUsed.Valid);
        ProbeLog.Value("systemUsed.positive", systemUsed.LastValue > 0);
        ProbeLog.Value("sampler.blocks", SamplerBlocksSeen);
        ProbeLog.Value("legacy.blocks", LegacyBlocksSeen);
        ProbeLog.Value("legacy.elapsedPositive", LegacyElapsedPositive);
        recorder.Stop();
        ProbeLog.Value("stopped.isRunning", recorder.IsRunning);
        recorder.Reset();
        ProbeLog.Value("reset.count", recorder.Count);
        recorder.Dispose();
        ProbeLog.Value("disposed.valid", recorder.Valid);
        jobRecorder.Dispose();
        counter.Dispose();
        idle.Dispose();
        missing.Dispose();
        mainThread.Dispose();
        gcReserved.Dispose();
        systemUsed.Dispose();
        legacy.enabled = false;
        samplerRecorder.enabled = false;
    }

    static ProfilerRecorderHandle FindHandle(string name)
    {
        var available = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(available);
        foreach (var h in available)
            if (ProfilerRecorderHandle.GetDescription(h).Name == name) return h;
        return default;
    }

    static int SamplerBlocksSeen => CustomSampler.Create("Probe.Sampler").GetRecorder().sampleBlockCount;
    static int LegacyBlocksSeen => Recorder.Get("Probe.Marker").sampleBlockCount;
    static bool LegacyElapsedPositive => Recorder.Get("Probe.Marker").elapsedNanoseconds > 0;

    static bool HasSampleAtLeast(ProfilerRecorder recorder, long value)
    {
        for (int i = 0; i < recorder.Count; i++)
            if (recorder.GetSample(i).Value >= value) return true;
        return false;
    }

    static string SampleCounts(ProfilerRecorder recorder)
    {
        var counts = new List<string>();
        var samples = new List<ProfilerRecorderSample>();
        recorder.CopyTo(samples);
        foreach (var s in samples) counts.Add(s.Count.ToString());
        return string.Join(",", counts);
    }
}
