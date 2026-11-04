using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Awaitables completed in one Update pass. Unity 6000.0 Manual, "Event function execution order"
    /// (https://docs.unity.com/en-us/engine/6000.0/manual/scripting/managing-update-order/execution-order):
    /// "The exact order of execution between resuming coroutines and asynchronous tasks is not
    /// guaranteed. Awaitables are grouped together and executed in the order they were awaited."
    ///
    /// These guards pin the await order within each kind of wait, in both registration orders and
    /// against due times. They do not pin the order between NextFrameAsync and WaitForSecondsAsync.
    /// In Unity's public 6000.0 reference source the two are separate queues: NextFrameAsync is a
    /// managed list completed by one Update callback, WaitForSecondsAsync a native awaitable. Their
    /// relative order is therefore not established, and no Unity measurement covers it.
    /// </summary>
    [Collection("Frames")]
    public sealed class AwaitableUpdateOrderTests : IDisposable
    {
        readonly double m_PreviousTime = Time.s_Time;

        public void Dispose() => Time.s_Time = m_PreviousTime;

        static async Awaitable Logs(Awaitable wait, string name, List<string> log)
        {
            await wait;
            log.Add(name);
        }

        static void AssertBefore(List<string> log, string first, string second) =>
            Assert.True(log.IndexOf(first) < log.IndexOf(second), $"{first} after {second}: {string.Join(", ", log)}");

        static void AssertOnePassInAwaitOrder(List<string> log)
        {
            Assert.Equal(4, log.Count);
            AssertBefore(log, "seconds 1", "seconds 2");
            AssertBefore(log, "frame 1", "frame 2");
        }

        // Green on old and new Core. "seconds 2" is due before "seconds 1" but was awaited later.
        [Fact]
        public void TimedWaitsAwaitedFirstKeepTheirAwaitOrderAmongTheirKind()
        {
            var log = new List<string>();
            Time.s_Time = 10;
            _ = Logs(Awaitable.WaitForSecondsAsync(0.5f), "seconds 1", log);
            _ = Logs(Awaitable.NextFrameAsync(), "frame 1", log);
            _ = Logs(Awaitable.WaitForSecondsAsync(0.25f), "seconds 2", log);
            _ = Logs(Awaitable.NextFrameAsync(), "frame 2", log);
            AwaitableScheduler.RunUpdate();
            Assert.Empty(log);
            Time.s_FrameCount++;
            Time.s_Time = 11;
            AwaitableScheduler.RunUpdate();
            AssertOnePassInAwaitOrder(log);
        }

        // Green on old and new Core: the same with each next-frame wait awaited first.
        [Fact]
        public void NextFrameWaitsAwaitedFirstKeepTheirAwaitOrderAmongTheirKind()
        {
            var log = new List<string>();
            Time.s_Time = 10;
            _ = Logs(Awaitable.NextFrameAsync(), "frame 1", log);
            _ = Logs(Awaitable.WaitForSecondsAsync(0.5f), "seconds 1", log);
            _ = Logs(Awaitable.NextFrameAsync(), "frame 2", log);
            _ = Logs(Awaitable.WaitForSecondsAsync(0.25f), "seconds 2", log);
            AwaitableScheduler.RunUpdate();
            Assert.Empty(log);
            Time.s_FrameCount++;
            Time.s_Time = 11;
            AwaitableScheduler.RunUpdate();
            AssertOnePassInAwaitOrder(log);
        }

        // Green on old and new Core: a cancelled wait leaves its group. The others still complete
        // in await order in the same pass.
        [Fact]
        public void ACancelledWaitLeavesTheGroupWithoutReorderingIt()
        {
            var log = new List<string>();
            Time.s_Time = 10;
            _ = Logs(Awaitable.NextFrameAsync(), "frame 1", log);
            var cancelled = Awaitable.NextFrameAsync();
            _ = Logs(Awaitable.NextFrameAsync(), "frame 2", log);
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => cancelled.GetAwaiter().GetResult());
            Time.s_FrameCount++;
            AwaitableScheduler.RunUpdate();
            Assert.Equal(new[] { "frame 1", "frame 2" }, log);
        }
    }
}
