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
        readonly ThreadPriority m_Priority = Application.backgroundLoadingPriority;
        readonly SceneOperationQueue m_Queue;
        readonly List<string> m_Events = new List<string>();
        // The queue's clock: integration work advances it by the time it is given.
        double m_Now;

        public SceneOperationQueueTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;
            m_Queue = new SceneOperationQueue(() => m_Now);
        }

        public void Dispose()
        {
            Application.backgroundLoadingPriority = m_Priority;
            ProjectInfo.UnityVersion = m_Release;
        }

        AsyncOperation Load(string name, int frame, bool async = true, double milliseconds = 0)
        {
            var operation = new AsyncOperation();
            m_Queue.Enqueue(operation, null, () => { m_Events.Add(name + ".objects"); m_Now += milliseconds; },
                SceneOperationQueue.Kind.Load, async, frame);
            operation.completed += _ => m_Events.Add(name + ".completed");
            return operation;
        }

        AsyncOperation Unload(string name, int frame, SceneOperationQueue.Kind kind = SceneOperationQueue.Kind.Unload)
        {
            var operation = new AsyncOperation();
            var scene = new SceneData { IsLoaded = true };
            m_Queue.Enqueue(operation, scene, () => { scene.IsLoaded = false; m_Events.Add(name); }, kind, true, frame);
            return operation;
        }

        /// <summary>Frames from <paramref name="first"/> on, preparation then the preloading pass,
        /// until every operation is done; the frame each completed in.</summary>
        int[] Run(int first, params AsyncOperation[] operations)
        {
            var frames = new int[operations.Length];
            for (int frame = first; frame < first + 40; frame++)
            {
                m_Queue.Prepare(frame);
                m_Queue.Process(frame);
                bool pending = false;
                for (int i = 0; i < operations.Length; i++)
                {
                    if (operations[i].isDone && frames[i] == 0) frames[i] = frame;
                    pending |= !operations[i].isDone;
                }
                if (!pending) return frames;
            }
            throw new InvalidOperationException("the queue did not finish");
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
            m_Queue.Enqueue(unload, scene, () => { scene.IsLoaded = false; m_Events.Add("unload"); }, SceneOperationQueue.Kind.Unload, true, 0);
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
            // The second load reached the head prepared in the first pass: it integrates in the
            // next frame, and the unload behind it in the same pass.
            m_Queue.Process(6);
            Assert.True(second.isDone);
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
        public void ACallbackLoadCannotPostponeAnEarlierSynchronousRequest()
        {
            var first = new AsyncOperation();
            AsyncOperation callbackLoad = null;
            m_Queue.Enqueue(first, null, () => {
                m_Events.Add("first.objects");
                // Scene loading invokes sceneLoaded inside its work, before completion.
                callbackLoad = Load("callback", 1, async: false);
            }, SceneOperationQueue.Kind.Load, false, 0);
            var second = Load("second", 0, async: false);
            m_Queue.Process(0);
            Assert.False(first.isDone || second.isDone);
            m_Queue.Process(1);
            Assert.True(first.isDone && second.isDone);
            Assert.NotNull(callbackLoad);
            Assert.False(callbackLoad.isDone);
            Assert.Equal(new[] { "first.objects", "second.objects", "second.completed" }, m_Events);
            m_Queue.Process(2);
            Assert.True(callbackLoad.isDone);
            Assert.Equal(new[] { "first.objects", "second.objects", "second.completed", "callback.objects", "callback.completed" }, m_Events);
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
            m_Queue.Process(1);
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
            m_Queue.Enqueue(first, null, () => m_Events.Add("first.objects"), SceneOperationQueue.Kind.Load, true, 0,
                prepare: () => { second = Load("second", 1); m_Queue.Prepare(1); });
            m_Queue.Prepare(1);
            m_Queue.Process(1);
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
            }, SceneOperationQueue.Kind.Unload, true, 0);
            m_Queue.Prepare(1);
            m_Queue.Process(1);
            Assert.Equal(true, during);
            Assert.Equal(false, after);
            Assert.True(operation.isDone);
            Assert.False(m_Queue.IsUnloading);
        }

        [Theory]
        // Integration below the limit: the next load is armed in the same pass.
        [InlineData(1.0, 2, 3, 4)]
        [InlineData(3.4, 2, 3, 4)]
        // Rounded to 4 ms, the BelowNormal limit: the pass stops and arming takes a pass of its own.
        [InlineData(3.5, 2, 4, 6)]
        [InlineData(70.0, 2, 4, 6)]
        public void ALoadIsArmedInThePassOfThePreviousOneOnlyBelowTheTimeLimit(double milliseconds, int first, int second, int third)
        {
            var a = Load("a", 0, milliseconds: milliseconds);
            var b = Load("b", 0, milliseconds: milliseconds);
            var c = Load("c", 0, milliseconds: milliseconds);
            Assert.Equal(new[] { first, second, third }, Run(1, a, b, c));
        }

        [Fact]
        public void TheLimitFollowsTheReadBackPriority()
        {
            // 3 ms: below BelowNormal's 4 ms, beyond Low's 2 ms.
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            Assert.Equal(new[] { 2, 4 }, Run(1, Load("a", 0, milliseconds: 3), Load("b", 0, milliseconds: 3)));
            Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;
            Assert.Equal(new[] { 8, 9 }, Run(7, Load("c", 6, milliseconds: 3), Load("d", 6, milliseconds: 3)));
        }

        [Fact]
        public void AnUnloadBehindALoadSharesItsPassBelowTheLimitAndArmsNothing()
        {
            var fast = Load("fast", 0, milliseconds: 1);
            var fastUnload = Unload("fast.unload", 0);
            var fastNext = Load("fast.next", 0);
            Assert.Equal(new[] { 2, 2, 3 }, Run(1, fast, fastUnload, fastNext));
            var slow = Load("slow", 10, milliseconds: 70);
            var slowUnload = Unload("slow.unload", 10);
            var slowNext = Load("slow.next", 10);
            Assert.Equal(new[] { 12, 13, 14 }, Run(11, slow, slowUnload, slowNext));
        }

        [Fact]
        public void ALoadHeldBehindAnotherIntegratesInThePassAfterItsRelease()
        {
            var first = Load("first", 0);
            var held = Load("held", 0);
            held.allowSceneActivation = false;
            for (int frame = 1; frame <= 5; frame++) { m_Queue.Prepare(frame); m_Queue.Process(frame); }
            Assert.True(first.isDone);
            Assert.False(held.isDone);
            held.allowSceneActivation = true;
            Assert.Equal(new[] { 6 }, Run(6, held));
        }

        [Fact]
        public void PriorityOrdersRequestsTheLoaderHasNotTaken()
        {
            // The first request finds the loader idle and is taken at once; the others wait for
            // its next pass, where the highest priority goes first and equal ones keep their order.
            var first = Load("first", 0);
            var low = Load("low", 0);
            var high = Load("high", 0);
            var same = Load("same", 0);
            first.priority = 5;
            low.priority = 1;
            high.priority = 2;
            same.priority = 1;
            Run(1, first, low, high, same);
            Assert.Equal(new[] { "first.objects", "first.completed", "high.objects", "high.completed",
                "low.objects", "low.completed", "same.objects", "same.completed" }, m_Events);
        }

        [Fact]
        public void APriorityChangedAfterTheLoaderTookTheRequestHasNoEffect()
        {
            var first = Load("first", 0);
            var second = Load("second", 0);
            var third = Load("third", 0);
            m_Queue.Prepare(1);
            third.priority = 2;
            second.priority = 1;
            Run(1, first, second, third);
            Assert.Equal(new[] { "first.objects", "first.completed", "second.objects", "second.completed",
                "third.objects", "third.completed" }, m_Events);
        }

        [Fact]
        public void AnUnusedAssetUnloadStopsTheLoaderUntilItIsIntegrated()
        {
            var unused = Unload("unused", 0, SceneOperationQueue.Kind.UnloadUnused);
            var load = Load("load", 0);
            m_Queue.Prepare(1);
            Assert.Equal(0f, load.progress);
            m_Queue.Process(1);
            Assert.True(unused.isDone);
            // Taken at the loader's next pass, armed there, integrated in the frame after.
            Assert.Equal(new[] { 3 }, Run(2, load));
            Assert.Equal(new[] { "unused", "load.objects", "load.completed" }, m_Events);
        }

        [Fact]
        public void ALaterRequestWithAHigherPriorityDoesNotOvertakeALoadScene()
        {
            // The loader is busy with the first request; the LoadScene request and the held,
            // higher-priority request behind it wait for its next pass.
            var first = Load("first", 0);
            var sync = Load("sync", 0, async: false);
            var later = Load("later", 0);
            later.priority = 1;
            later.allowSceneActivation = false;
            m_Queue.Prepare(1);
            m_Queue.Process(1);
            Assert.True(first.isDone && sync.isDone);
            Assert.False(later.isDone);
            Assert.Equal(new[] { "first.objects", "first.completed", "sync.objects", "sync.completed" }, m_Events);
        }

        [Fact]
        public void AnUnusedAssetUnloadQueuedBehindALoadSceneIsForcedWithIt()
        {
            var sync = Load("sync", 0, async: false);
            var unused = new AsyncOperation();
            m_Queue.EnqueueAfterLast(unused, () => m_Events.Add("unused"), SceneOperationQueue.Kind.UnloadUnused, 0);
            m_Queue.Process(1);
            Assert.True(sync.isDone && unused.isDone);
            Assert.Equal(new[] { "sync.objects", "sync.completed", "unused" }, m_Events);
        }

        [Theory]
        [InlineData(ThreadPriority.Low, 2)]
        [InlineData(ThreadPriority.BelowNormal, 4)]
        [InlineData(ThreadPriority.Normal, 10)]
        [InlineData(ThreadPriority.High, 50)]
        [InlineData((ThreadPriority)3, 4)]
        [InlineData((ThreadPriority)(-1), 4)]
        public void TheLimitOfEachPriorityIsTheDocumentedOne(ThreadPriority priority, int milliseconds) =>
            Assert.Equal(milliseconds, SceneOperationQueue.LoadingLimitMilliseconds(priority));

        [Fact]
        public void TheLinuxEditorReadsRaisedPrioritiesBackAsLow()
        {
            foreach (var (set, read) in new[] {
                (ThreadPriority.Low, ThreadPriority.Low), (ThreadPriority.BelowNormal, ThreadPriority.BelowNormal),
                (ThreadPriority.Normal, ThreadPriority.Low), (ThreadPriority.High, ThreadPriority.Low),
                ((ThreadPriority)3, ThreadPriority.Low) })
            {
                Application.backgroundLoadingPriority = set;
                Assert.Equal(OperatingSystem.IsLinux() ? read : set, Application.backgroundLoadingPriority);
            }
        }

        [Fact]
        public void OtherReleasesKeepTheValueSetAndTheOnePassTiming()
        {
            ProjectInfo.UnityVersion = UnityEngine.Tests.TestRelease.Unmeasured;
            Application.backgroundLoadingPriority = ThreadPriority.High;
            Assert.Equal(ThreadPriority.High, Application.backgroundLoadingPriority);
            var a = Load("a", 0, milliseconds: 70);
            var b = Load("b", 0, milliseconds: 70);
            m_Queue.Prepare(1);
            m_Queue.Process(1);
            Assert.True(a.isDone && b.isDone);
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

        [Fact]
        public void AnAssetLoadQueuedBehindAHeldSceneLoadWaitsForIt()
        {
            // tests/projects/sceneunload queue.sceneFirst: a Resources or AssetBundle load requested
            // after a held scene load completes only after the scene's activation.
            var scene = Load("scene", 0);
            scene.allowSceneActivation = false;
            var asset = new AsyncOperation();
            m_Queue.EnqueueAfterLast(asset, () => m_Events.Add("asset"), SceneOperationQueue.Kind.Asset, 0);
            for (int frame = 1; frame <= 10; frame++) { m_Queue.Prepare(frame); m_Queue.Process(frame); }
            Assert.False(asset.isDone);
            scene.allowSceneActivation = true;
            var frames = Run(11, scene, asset);
            Assert.Equal(frames[0], frames[1]);
            Assert.Equal(new[] { "scene.objects", "scene.completed", "asset" }, m_Events);
        }

        [Fact]
        public void TheUnloadAfterASingleLoadCompletesTheFrameAfterTheLoad()
        {
            // tests/projects/sceneunload singleasync/done: when the load reports done the unload has
            // not run; it is prepared only once the load has integrated.
            var load = Load("load", 0);
            var unload = new AsyncOperation();
            m_Queue.EnqueueAfterLast(unload, () => m_Events.Add("unused"), SceneOperationQueue.Kind.UnloadUnused, 0);
            var frames = Run(1, load, unload);
            Assert.Equal(frames[0] + 1, frames[1]);
        }

        [Fact]
        public void AnUnusedAssetUnloadAloneCompletesAtTheNextPass()
        {
            var unload = new AsyncOperation();
            m_Queue.EnqueueAfterLast(unload, () => m_Events.Add("unused"), SceneOperationQueue.Kind.UnloadUnused, 0);
            Assert.Equal(new[] { 1 }, Run(1, unload));
        }
    }
}
