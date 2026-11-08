using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xunit;

namespace Unbound.Tests
{
    [Collection("Frames")]
    public sealed class SceneOperationQueueTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        readonly SceneOperationQueue m_Queue = new SceneOperationQueue();
        readonly List<string> m_Events = new List<string>();

        public SceneOperationQueueTests() => ProjectInfo.UnityVersion = "6000.0.71f1";
        public void Dispose() => ProjectInfo.UnityVersion = m_Release;

        AsyncOperation Load(string name, int frame, bool async = true)
        {
            var operation = new AsyncOperation();
            m_Queue.Enqueue(operation, null, () => m_Events.Add(name + ".objects"), true, async, frame);
            operation.completed += _ => m_Events.Add(name + ".completed");
            return operation;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(37)]
        public void EveryAsyncLoadSeparatesPreparationFromActivation(int frame)
        {
            var operation = Load("load", frame);
            Assert.Equal(0f, operation.progress);
            m_Queue.Prepare(frame + 1);
            Assert.Equal(.9f, operation.progress);
            m_Queue.Process(frame + 1);
            Assert.False(operation.isDone);
            m_Queue.Process(frame + 2);
            Assert.True(operation.isDone);
            Assert.Equal(1f, operation.progress);
            Assert.Equal(new[] { "load.objects", "load.completed" }, m_Events);
        }

        [Fact]
        public void HeldActivationStallsIntegrationAndUnloadButNotPreparation()
        {
            var first = Load("first", 0);
            first.allowSceneActivation = false;
            var second = Load("second", 0);
            var unload = new AsyncOperation();
            var scene = new SceneData { IsLoaded = true };
            m_Queue.Enqueue(unload, scene, () => { scene.IsLoaded = false; m_Events.Add("unload"); }, false, true, 0);
            for (int frame = 1; frame <= 4; frame++)
            {
                m_Queue.Prepare(frame);
                m_Queue.Process(frame);
                Assert.Equal(.9f, first.progress);
                Assert.Equal(.9f, second.progress);
                Assert.False(first.isDone || second.isDone || unload.isDone);
                Assert.True(m_Queue.IsUnloading);
            }
            first.allowSceneActivation = true;
            first.allowSceneActivation = false;
            first.allowSceneActivation = true;
            m_Queue.Process(5);
            m_Queue.Process(5);
            Assert.True(first.isDone);
            Assert.False(second.isDone || unload.isDone);
            m_Queue.Process(6);
            Assert.True(second.isDone);
            m_Queue.Process(7);
            Assert.True(unload.isDone);
            Assert.False(m_Queue.IsUnloading);
            Assert.Equal(new[] { "first.objects", "first.completed", "second.objects", "second.completed", "unload" }, m_Events);
        }

        [Fact]
        public void SynchronousSceneLoadForcesEarlierHeldWorkOnTheNextFrame()
        {
            var held = Load("held", 0);
            held.allowSceneActivation = false;
            m_Queue.Prepare(1);
            m_Queue.Process(1);
            var sync = Load("sync", 1, async: false);
            m_Queue.Process(1);
            Assert.False(held.isDone || sync.isDone);
            m_Queue.Process(2);
            Assert.True(held.isDone && sync.isDone);
            Assert.False(held.allowSceneActivation);
            Assert.Equal(new[] { "held.objects", "held.completed", "sync.objects", "sync.completed" }, m_Events);
        }

        [Fact]
        public void CompletionReentryCannotConsumeNewRequestsInTheSamePass()
        {
            var first = Load("first", 0);
            AsyncOperation second = null;
            first.completed += _ => {
                second = Load("second", 2);
                m_Queue.Process(2);
            };
            m_Queue.Prepare(1);
            m_Queue.Process(2);
            Assert.True(first.isDone);
            Assert.NotNull(second);
            Assert.False(second.isDone);
            m_Queue.Prepare(3);
            m_Queue.Process(3);
            Assert.False(second.isDone);
            m_Queue.Process(4);
            Assert.True(second.isDone);
        }

        [Fact]
        public void PreparationCallbacksCannotInvalidateTheQueueOrPrepareNewWorkInThatPass()
        {
            var first = new AsyncOperation();
            AsyncOperation second = null;
            m_Queue.Enqueue(first, null, () => m_Events.Add("first.objects"), true, true, 0,
                prepare: () => { second = Load("second", 1); m_Queue.Prepare(1); });
            m_Queue.Prepare(1);
            Assert.Equal(.9f, first.progress);
            Assert.NotNull(second);
            Assert.Equal(0f, second.progress);
            m_Queue.Prepare(2);
            m_Queue.Process(2);
            Assert.True(first.isDone);
            Assert.False(second.isDone);
            m_Queue.Process(3);
            Assert.True(second.isDone);
        }

        [Fact]
        public void UnloadingStaysVisibleDuringTeardownUntilTheSceneIsRemoved()
        {
            var scene = new SceneData { IsLoaded = true };
            var operation = new AsyncOperation();
            bool? during = null, after = null;
            m_Queue.Enqueue(operation, scene, () => {
                during = m_Queue.IsUnloading;
                scene.IsLoaded = false;
                after = m_Queue.IsUnloading;
            }, false, true, 0);
            m_Queue.Process(1);
            Assert.Equal(true, during);
            Assert.Equal(false, after);
            Assert.True(operation.isDone);
            Assert.False(m_Queue.IsUnloading);
        }

        [Fact]
        public void LeavingTheSessionDiscardsWorkWithoutCompletingOldOperations()
        {
            var old = Load("old", 0);
            m_Queue.Clear();
            m_Queue.Prepare(1);
            m_Queue.Process(2);
            Assert.False(old.isDone);
            Assert.Empty(m_Events);
        }
    }
}
