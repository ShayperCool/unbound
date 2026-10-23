using System;
using System.Collections.Generic;
using System.Threading;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class AwaitableTests
    {
        static readonly List<string> Log = new List<string>();

        static async Awaitable TwoFrames()
        {
            Log.Add("start");
            await Awaitable.NextFrameAsync();
            Log.Add("frame 1");
            await Awaitable.NextFrameAsync();
            Log.Add("frame 2");
        }

        static async Awaitable<int> Answer()
        {
            await Awaitable.NextFrameAsync();
            return 42;
        }

        static async Awaitable Throws()
        {
            await Awaitable.NextFrameAsync();
            throw new InvalidOperationException("boom");
        }

        static void NextFrame()
        {
            Time.s_FrameCount++;
            AwaitableScheduler.RunUpdate();
        }

        [Fact]
        public void AsyncMethodsResumeOnTheFollowingFrames()
        {
            Log.Clear();
            var task = TwoFrames();
            Assert.Equal(new[] { "start" }, Log);
            // The frame that started the wait does not complete it.
            AwaitableScheduler.RunUpdate();
            Assert.Single(Log);
            NextFrame();
            Assert.Equal("frame 1", Log[^1]);
            Assert.False(task.IsCompleted);
            NextFrame();
            Assert.Equal("frame 2", Log[^1]);
            Assert.True(task.IsCompleted);
        }

        [Fact]
        public void ResultsAndExceptionsReachTheAwaiter()
        {
            var answer = Answer();
            NextFrame();
            Assert.Equal(42, answer.GetAwaiter().GetResult());

            var failing = Throws();
            NextFrame();
            Assert.Throws<InvalidOperationException>(() => failing.GetAwaiter().GetResult());
        }

        [Fact]
        public void CompletionSourcesAndCancellation()
        {
            var source = new AwaitableCompletionSource<string>();
            bool resumed = false;
            source.Awaitable.GetAwaiter().OnCompleted(() => resumed = true);
            Assert.True(source.TrySetResult("ok"));
            Assert.True(resumed);
            Assert.Equal("ok", source.Awaitable.GetAwaiter().GetResult());
            Assert.False(source.TrySetResult("again"));

            using var cts = new CancellationTokenSource();
            var wait = Awaitable.WaitForSecondsAsync(100f, cts.Token);
            cts.Cancel();
            Assert.True(wait.IsCompleted);
            Assert.Throws<OperationCanceledException>(() => wait.GetAwaiter().GetResult());
        }

        [Fact]
        public void AsyncOperationsCanBeAwaited()
        {
            var op = new AsyncOperation();
            var awaiter = op.GetAwaiter();
            Assert.False(awaiter.IsCompleted);
            op.Complete();
            Assert.True(awaiter.IsCompleted);
        }
    }
}
