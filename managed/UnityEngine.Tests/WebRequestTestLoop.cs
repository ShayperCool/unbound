using System;
using System.Reflection;
using System.Threading;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    // Requests deliver through the player loop. Disabling its main-thread identity lets
    // worker continuations race test assertions and does not model a running game.
    internal sealed class WebRequestTestLoop : IDisposable
    {
        static readonly FieldInfo MainThread = typeof(PlayerLoopDriver).GetField("s_MainThreadId", BindingFlags.Static | BindingFlags.NonPublic);
        readonly object previousThread = MainThread.GetValue(null);

        internal WebRequestTestLoop() => MainThread.SetValue(null, Environment.CurrentManagedThreadId);

        internal static void Until(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!done() && DateTime.UtcNow < deadline)
            {
                MainThreadContext.Instance.RunPending();
                Thread.Yield();
            }
            Assert.True(done(), "the request did not complete");
        }

        public void Dispose()
        {
            try { MainThreadContext.Instance.RunPending(); }
            finally { MainThread.SetValue(null, previousThread); }
        }
    }
}
