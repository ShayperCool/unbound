using System;

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
        internal static void EmptyAllocationContext() => GC.Collect();
    }
}
