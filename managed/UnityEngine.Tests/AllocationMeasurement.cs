using System;
using System.Threading;

namespace UnityEngine.Tests
{
    internal static class AllocationMeasurement
    {
        /// <summary>Empties this thread's allocation context with a collection of its own. While
        /// other threads allocate and collect, the runtime's counter for this thread was seen to
        /// rise by up to 8 KiB (about one allocation quantum) without an allocation in the test
        /// host; with the context emptied first it does not rise until the code allocates, so the
        /// window counts only what the code allocates
        /// (<see cref="AnimationAllocationTests.TheEmptiedAllocationContextCountsNoCollection"/>).</summary>
        internal static void EmptyAllocationContext() => GC.Collect(0, GCCollectionMode.Forced, blocking: true);

        internal sealed class CollectionNoise : IDisposable
        {
            bool m_Stop;
            readonly Thread m_Thread;

            internal CollectionNoise()
            {
                m_Thread = new Thread(Run);
                m_Thread.Start();
            }

            void Run()
            {
                var random = new System.Random(1);
                object kept = null;
                for (int i = 0; !Volatile.Read(ref m_Stop); i++)
                {
                    kept = new byte[random.Next(16, 200_000)];
                    if (i % 20 == 0) GC.Collect(2, GCCollectionMode.Forced, blocking: i % 40 != 0);
                }
                GC.KeepAlive(kept);
            }

            public void Dispose()
            {
                Volatile.Write(ref m_Stop, true);
                m_Thread.Join();
            }
        }
    }
}
