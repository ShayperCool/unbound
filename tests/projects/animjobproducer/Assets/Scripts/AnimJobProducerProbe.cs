using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// The animation job producer: the job producer IAnimationJob names (the release's internal
// ProcessAnimationJobStruct<T>, reached through reflection), its cached reflection data, and its
// Execute dispatching a job's ProcessRootMotion or ProcessAnimation by method index on a stream
// copied from a pointer, called from inside a running job. Also the order a graph evaluation calls
// a job's two methods in.
public class AnimJobProducerProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("AnimJobProducerProbe").AddComponent<AnimJobProducerProbe>(); }
    [Serializable] class Entry { public string key; public string value; }
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly List<(string key, string value)> rows = new List<(string, string)>();
    static readonly List<string> calls = new List<string>(), innerCalls = new List<string>();
    static readonly Type Producer = typeof(IAnimationJob).Assembly.GetType("UnityEngine.Animations.ProcessAnimationJobStruct`1");

    static void Put(string key, string value) { lock (rows) rows.Add((key, value)); }
    static void Call(string what) { lock (calls) calls.Add(what); }
    static void InnerCall(string what) { lock (innerCalls) innerCalls.Add(what); }
    static string N(float v) => v.ToString("R", Inv);

    struct Inner : IAnimationJob
    {
        public int count;
        public void ProcessAnimation(AnimationStream stream) { count += 1; InnerCall("inner.animation valid " + stream.isValid + " dt " + N(stream.deltaTime)); }
        public void ProcessRootMotion(AnimationStream stream) { count += 10; InnerCall("inner.rootMotion valid " + stream.isValid); }
    }

    struct Outer : IAnimationJob
    {
        public int evaluations;
        public void ProcessRootMotion(AnimationStream stream) => Call("outer.rootMotion");
        public void ProcessAnimation(AnimationStream stream)
        {
            Call("outer.animation");
            if (evaluations++ == 0) RunInner(stream);
        }
    }

    struct Other : IAnimationJob
    {
        public void ProcessAnimation(AnimationStream stream) { }
        public void ProcessRootMotion(AnimationStream stream) { }
    }

    // Execute on the inner job with each method index; the job's changes come back through its
    // by-reference argument.
    static void RunInner(AnimationStream stream)
    {
        var execute = Producer.MakeGenericType(typeof(Inner)).GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<AnimationStream>());
        try
        {
            Marshal.StructureToPtr(stream, pointer, false);
            var job = new Inner();
            foreach (int index in new[] { 1, 0, 2 })
            {
                var args = new object[] { job, pointer, (IntPtr)index, default(JobRanges), 0 };
                string result;
                lock (innerCalls) innerCalls.Clear();
                try { execute.Invoke(null, args); result = "ok"; }
                catch (TargetInvocationException ex) { result = ex.InnerException.GetType().Name + ": " + ex.InnerException.Message; }
                job = (Inner)args[0];
                lock (innerCalls) Put("execute." + index, result + " count " + job.count + " [" + string.Join("; ", innerCalls) + "]");
            }
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("ANIM_JOB_PRODUCER_OUT"); if (string.IsNullOrEmpty(path)) return;
        bool failed = false;
        try { Record(); }
        catch (Exception ex) { Debug.LogException(ex); failed = true; }
        using (var output = new StreamWriter(path))
        {
            foreach (var (key, value) in rows) output.WriteLine(JsonUtility.ToJson(new Entry { key = key, value = value }));
            if (!failed) output.WriteLine(JsonUtility.ToJson(new Entry { key = "complete", value = "1" }));
        }
        EditorApplication.Exit(failed ? 1 : 0);
    }

    static string ReflectionData(Type job) => ((IntPtr)Producer.MakeGenericType(job).GetMethod("GetJobReflectionData").Invoke(null, null)).ToString();

    void Record()
    {
        var markers = typeof(IAnimationJob).GetCustomAttributes(typeof(JobProducerTypeAttribute), false).Cast<JobProducerTypeAttribute>().ToList();
        Put("marker", string.Join(",", markers.Select(m => m.ProducerType.FullName)));
        Put("marker.onJobStruct", typeof(Inner).GetCustomAttributes(typeof(JobProducerTypeAttribute), true).Length.ToString(Inv));
        Put("producer", (Producer == null ? "none" : (Producer.IsPublic ? "public" : "internal") + " " + (Producer.IsValueType ? "struct" : "class")
            + " " + string.Join(",", Producer.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal))));
        string first = ReflectionData(typeof(Inner)), again = ReflectionData(typeof(Inner)), other = ReflectionData(typeof(Other));
        Put("reflectionData", (first != "0" ? "set" : "zero") + " " + (first == again ? "cached" : "new") + " " + (first != other ? "perType" : "shared"));

        var go = new GameObject("Animated");
        new GameObject("Child").transform.SetParent(go.transform, false);
        var animator = go.AddComponent<Animator>();
        var graph = PlayableGraph.Create("producer");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        var playable = AnimationScriptPlayable.Create(graph, new Outer());
        output.SetSourcePlayable(playable);
        Put("reflectionData.afterCreate", ReflectionData(typeof(Outer)) != "0" ? "set" : "zero");
        foreach (var step in new[] { "first", "second" })
        {
            lock (calls) calls.Clear();
            graph.Evaluate(0.1f);
            lock (calls) Put("evaluate." + step, "[" + string.Join("; ", calls) + "] evaluations " + playable.GetJobData<Outer>().evaluations);
        }
        animator.applyRootMotion = true;
        lock (calls) calls.Clear();
        graph.Evaluate(0.1f);
        lock (calls) Put("evaluate.rootMotion", "[" + string.Join("; ", calls) + "]");
        graph.Destroy();
        DestroyImmediate(go);
    }
}
