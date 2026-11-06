using System;
using System.Reflection;
using System.Threading;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The engine API's main-thread check (tests/projects/mainthread compares it with the
    /// Editor): other threads and jobs, even jobs the main thread runs, get Unity's exception;
    /// thread-safe members and the job system's own transform access keep working.</summary>
    [Collection("Frames")]
    public sealed class MainThreadCheckTests : IDisposable
    {
        static readonly FieldInfo MainThread = typeof(PlayerLoopDriver).GetField("s_MainThreadId", BindingFlags.Static | BindingFlags.NonPublic);
        readonly object m_PreviousMainThread = MainThread.GetValue(null);

        public MainThreadCheckTests() => MainThread.SetValue(null, Environment.CurrentManagedThreadId);

        public void Dispose() => MainThread.SetValue(null, m_PreviousMainThread);

        static Exception OnOtherThread(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception e) { failure = e; }
            });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            return failure;
        }

        const string Tail = " can only be called from the main thread.\nConstructors and field initializers will be executed from the loading thread when loading a scene.\nDon't use this function in the constructor or field initializers, instead move initialization code to the Awake or Start function.";

        [Fact]
        public void EngineApiThrowsOnAnotherThreadWithTheBindingName()
        {
            var go = new GameObject("main");
            try
            {
                var e = Assert.IsType<UnityException>(OnOtherThread(() => _ = Time.time));
                Assert.Equal("get_time" + Tail, e.Message);
                Assert.Equal("GetName" + Tail, OnOtherThread(() => _ = go.name).Message);
                var transform = go.transform;
                Assert.Equal("get_position" + Tail, OnOtherThread(() => _ = transform.position).Message);
                Assert.Equal("Internal_CreateGameObject" + Tail, OnOtherThread(() => new GameObject("other")).Message);
                var id = Assert.IsType<InvalidOperationException>(OnOtherThread(() => go.GetInstanceID()));
                Assert.Equal("EnsureRunningOnMainThread can only be called from the main thread", id.Message);
                // Thread-safe members.
                Assert.Null(OnOtherThread(() =>
                {
                    _ = Mathf.PerlinNoise(0.5f, 0.5f);
                    _ = JsonUtility.ToJson(new Vector3(1, 2, 3));
                    _ = go.GetHashCode();
                    Assert.True(go != null);
                }));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void AnObjectCreatedOffTheMainThreadIsNotRegistered()
        {
            GameObject made = null;
            int before = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
            Assert.IsType<UnityException>(OnOtherThread(() => made = new GameObject("other")));
            Assert.Null(made);
            Assert.Equal(before, Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length);
        }

        [Serializable]
        class WithReference
        {
            public GameObject target;
        }

        [Fact]
        public void JsonUtilityWritesObjectReferencesOnAnotherThread()
        {
            var go = new GameObject("referenced");
            var dead = new GameObject("dead");
            Object.DestroyImmediate(dead);
            try
            {
                string live = null;
                Assert.Null(OnOtherThread(() =>
                {
                    live = JsonUtility.ToJson(new WithReference { target = go });
                    JsonUtility.ToJson(new WithReference { target = dead });
                }));
                Assert.Equal(JsonUtility.ToJson(new WithReference { target = go }), live);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void ReinitializeOffTheMainThreadLeavesTheTextureAlive()
        {
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.Equal("Reinitialize" + Tail, Assert.IsType<UnityException>(OnOtherThread(() => texture.Reinitialize(4, 4))).Message);
                Assert.True(texture != null);
                Assert.Equal(2, texture.width);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        struct ReadTimeJob : IJob
        {
            public NativeArray<int> Outcome;

            public void Execute()
            {
                try
                {
                    _ = Time.frameCount;
                    Outcome[0] = 1;
                }
                catch (UnityException e) when (e.Message == "get_frameCount" + Tail)
                {
                    Outcome[0] = 2;
                }
            }
        }

        [Fact]
        public void AJobRunOnTheMainThreadIsOffTheMainThread()
        {
            var outcome = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new ReadTimeJob { Outcome = outcome }.Run();
                Assert.Equal(2, outcome[0]);
                outcome[0] = 0;
                new ReadTimeJob { Outcome = outcome }.Schedule().Complete();
                Assert.Equal(2, outcome[0]);
            }
            finally
            {
                outcome.Dispose();
            }
        }

        struct MoveJob : IJobParallelForTransform
        {
            public void Execute(int index, TransformAccess transform) => transform.position += Vector3.up;
        }

        [Fact]
        public void TransformJobsStillAccessTheirTransforms()
        {
            var go = new GameObject("moved");
            var transforms = new TransformAccessArray(new[] { go.transform });
            try
            {
                new MoveJob().Schedule(transforms).Complete();
                Assert.Equal(Vector3.up, go.transform.position);
            }
            finally
            {
                transforms.Dispose();
                Object.DestroyImmediate(go);
            }
        }
    }
}
