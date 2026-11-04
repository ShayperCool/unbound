using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Awaitables completed in one Update pass: preservation controls of Unbound's current queues.
    /// Unity6000.0 C# reference source shows NextFrame FIFO; timed waits use native scheduling,
    /// so the timed due-order inversion asserted here is unmeasured, not a Unity guarantee.
    /// The Manual describes await order within groups without defining every grouping boundary.
    /// Cross-kind order remains unresolved; these tests assert none and authorize no global sort.
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

        // Internal preservation, unmeasured for Unity: seconds 2 is due earlier but awaited later.
        // The source-backed NextFrame FIFO check is independent of that timed-queue choice.
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
