using System;
using System.Collections.Generic;

namespace UnityEngine.Profiling
{
    /// <summary>A named profiler sampler. Unbound has no profiler: samplers are valid and record
    /// nothing (their recorders report zero time).</summary>
    public partial class Sampler
    {
        internal IntPtr m_Ptr;
        internal string m_Name = "";
        static readonly Dictionary<string, Sampler> s_ByName = new Dictionary<string, Sampler>();

        internal Sampler() { }

        public bool isValid => m_Ptr != IntPtr.Zero;

        public string name => m_Name;

        public Recorder GetRecorder() => new Recorder(m_Name);

        public static Sampler Get(string name)
        {
            lock (s_ByName) return s_ByName.TryGetValue(name, out var sampler) ? sampler : new Sampler();
        }

        public static int GetNames(List<string> names)
        {
            lock (s_ByName)
            {
                if (names != null)
                {
                    names.Clear();
                    names.AddRange(s_ByName.Keys);
                }
                return s_ByName.Count;
            }
        }

        internal static void Register(Sampler sampler)
        {
            lock (s_ByName) s_ByName[sampler.m_Name] = sampler;
        }
    }

    public sealed partial class CustomSampler : Sampler
    {
        static long s_Next;

        internal CustomSampler() { }

        public static CustomSampler Create(string name, bool collectGpuData = false)
        {
            var sampler = new CustomSampler { m_Name = name ?? "", m_Ptr = (IntPtr)System.Threading.Interlocked.Increment(ref s_Next) };
            Register(sampler);
            return sampler;
        }

        public void Begin() { }
        public void Begin(Object targetObject) { }
        public void End() { }
    }

    public sealed partial class Recorder
    {
        readonly string m_Name;

        internal Recorder() { }

        internal Recorder(string name) => m_Name = name;

        public static Recorder Get(string samplerName) => new Recorder(samplerName);

        public void FilterToCurrentThread() { }
        public void CollectFromAllThreads() { }

        public bool isValid => m_Name != null;
        public bool enabled { get; set; }
        public long elapsedNanoseconds => 0;
        public long gpuElapsedNanoseconds => 0;
        public int sampleBlockCount => 0;
        public int gpuSampleBlockCount => 0;
    }
}
