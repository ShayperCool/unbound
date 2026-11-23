using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

sealed class StreamVelocityProbe : MonoBehaviour
{
    [Serializable]
    struct Motion
    {
        public Vector3 velocity, angular, position;
        public Quaternion rotation;

        public static Motion Read(AnimationStream stream) => new Motion
        {
            velocity = stream.velocity,
            angular = stream.angularVelocity,
            position = stream.rootMotionPosition,
            rotation = stream.rootMotionRotation
        };
    }

    [Serializable]
    struct Sample
    {
        public int rootCalls, animationCalls;
        public Motion before, written, above, animation;
        public Vector3 parentPosition, actorPosition, deltaPosition, velocity, angularVelocity;
        public Quaternion actorRotation, deltaRotation;
    }

    [Serializable]
    struct Row
    {
        public string k;
        public Sample v;
    }

    [Serializable]
    struct Callbacks
    {
        public int calls;
        public Vector3 delta;
        public Vector3 velocity;
    }

    [Serializable]
    struct CallbackRow
    {
        public string k;
        public Callbacks v;
    }

    struct SetMotion : IAnimationJob
    {
        public NativeArray<Sample> result;
        public bool duringAnimation;

        public void ProcessRootMotion(AnimationStream stream)
        {
            var row = result[0];
            row.rootCalls++;
            row.before = Motion.Read(stream);
            if (!duringAnimation)
            {
                Write(stream);
                row.written = Motion.Read(stream);
            }
            result[0] = row;
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            var row = result[0];
            row.animationCalls++;
            if (duringAnimation)
            {
                Write(stream);
                row.written = Motion.Read(stream);
            }
            result[0] = row;
        }

        void Write(AnimationStream stream)
        {
            stream.velocity = new Vector3(1f, 2f, 4f);
            stream.angularVelocity = new Vector3(.35f, 1.5f, -.4f);
        }
    }

    struct ReadMotion : IAnimationJob
    {
        public NativeArray<Sample> result;
        public bool replaceVelocity;

        public void ProcessRootMotion(AnimationStream stream)
        {
            var row = result[0];
            if (replaceVelocity) stream.velocity = new Vector3(2f, 3f, -5f);
            row.above = Motion.Read(stream);
            result[0] = row;
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            var row = result[0];
            row.animation = Motion.Read(stream);
            result[0] = row;
        }
    }

    const string Variable = "STREAM_VELOCITY_OUT";
    readonly List<string> rows = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable(Variable) != null)
            new GameObject("StreamVelocityProbe").AddComponent<StreamVelocityProbe>();
    }

    void Start()
    {
        Run("root", true, false, false);
        Run("scaledParent", true, false, true);
        Run("noRootMotion", false, false, false);
        Run("animation", true, true, false);
        Run("disabledAnimation", false, true, false);
        Run("phaseOrder", true, true, false, replaceVelocity: true);
        Run("rootPhaseKeepsLinear", true, false, false, replaceVelocity: true);
        Run("rootCallback", true, false, false, callback: true);
        Run("animationCallback", true, true, false, callback: true);
        Run("callbackMotionOff", false, false, false, callback: true);
        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    void Run(string name, bool applyMotion, bool duringAnimation, bool scaledParent, bool replaceVelocity = false,
        bool callback = false)
    {
        var parent = new GameObject(name + "Parent");
        if (scaledParent)
        {
            parent.transform.SetPositionAndRotation(new Vector3(10, 0, 4), Quaternion.Euler(0, 30, 0));
            parent.transform.localScale = new Vector3(2, 1, .5f);
        }
        var actor = new GameObject(name).transform;
        actor.SetParent(parent.transform, false);
        actor.localPosition = new Vector3(2, 0, 1);
        actor.localRotation = Quaternion.Euler(0, 20, 0);
        var animator = actor.gameObject.AddComponent<Animator>();
        animator.applyRootMotion = applyMotion;
        var receiver = callback ? actor.gameObject.AddComponent<StreamVelocityMoveObserver>() : null;

        var result = new NativeArray<Sample>(1, Allocator.Persistent);
        var readResult = new NativeArray<Sample>(1, Allocator.Persistent);
        var graph = PlayableGraph.Create(name);
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var source = AnimationMixerPlayable.Create(graph, 0);
            var edit = AnimationScriptPlayable.Create(graph, new SetMotion
            {
                result = result, duringAnimation = duringAnimation
            }, 1);
            var read = AnimationScriptPlayable.Create(graph, new ReadMotion { result = readResult, replaceVelocity = replaceVelocity }, 1);
            edit.ConnectInput(0, source, 0, 1);
            read.ConnectInput(0, edit, 0, 1);
            AnimationPlayableOutput.Create(graph, name, animator).SetSourcePlayable(read);
            graph.Play();

            var intervals = new[] { 0f, .05f, .2f, .1f };
            for (int i = 0; i < intervals.Length; i++)
            {
                result[0] = default;
                readResult[0] = default;
                if (receiver != null) receiver.calls = 0;
                graph.Evaluate(intervals[i]);
                var sample = result[0];
                sample.above = readResult[0].above;
                sample.animation = readResult[0].animation;
                sample.parentPosition = parent.transform.position;
                sample.actorPosition = actor.position;
                sample.actorRotation = actor.rotation;
                sample.deltaPosition = animator.deltaPosition;
                sample.deltaRotation = animator.deltaRotation;
                sample.velocity = animator.velocity;
                sample.angularVelocity = animator.angularVelocity;
                rows.Add(JsonUtility.ToJson(new Row { k = name + "." + i, v = sample }));
                if (receiver != null)
                    rows.Add(JsonUtility.ToJson(new CallbackRow
                    {
                        k = name + "." + i + ".callback",
                        v = new Callbacks { calls = receiver.calls, delta = receiver.delta, velocity = receiver.velocity }
                    }));
            }
        }
        finally
        {
            graph.Destroy();
            readResult.Dispose();
            result.Dispose();
            DestroyImmediate(parent);
        }
    }
}

sealed class StreamVelocityMoveObserver : MonoBehaviour
{
    public int calls;
    public Vector3 delta, velocity;

    void OnAnimatorMove()
    {
        calls++;
        var animator = GetComponent<Animator>();
        delta = animator.deltaPosition;
        velocity = animator.velocity;
        animator.ApplyBuiltinRootMotion();
    }
}
