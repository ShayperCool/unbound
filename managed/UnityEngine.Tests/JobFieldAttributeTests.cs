using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Attributes on a job's plain struct fields apply to the pointers and containers of
    /// the struct they hold, a deferred array without safety checks still takes its list's buffer,
    /// and a job writing through a list's array view keeps its view when scheduling bumps the
    /// list's secondary version (the GPU Resident Drawer's jobs rely on all three in the Unity
    /// 6000.0.71f1 Editor, tests/projects/residentdrawer).</summary>
    public unsafe class JobFieldAttributeTests
    {
        struct Counter
        {
            public int* Value;
        }

        struct CountJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public Counter counter;

            public void Execute() => (*counter.Value)++;
        }

        struct UnmarkedCountJob : IJob
        {
            public Counter counter;

            public void Execute() => (*counter.Value)++;
        }

        [Fact]
        public void PointersInsideAMarkedStructFieldAreAllowed()
        {
            int value = 0;
            var counter = new Counter { Value = &value };
            new CountJob { counter = counter }.Schedule().Complete();
            Assert.Equal(1, value);
            var error = Assert.Throws<InvalidOperationException>(() => new UnmarkedCountJob { counter = counter }.Schedule());
            Assert.Contains("UnmarkedCountJob.counter.Value uses unsafe Pointers", error.Message);
        }

        struct Holder
        {
            public NativeArray<int> data;
        }

        struct ScatterJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public Holder holder;

            public void Execute(int index) => holder.data[holder.data.Length - 1 - index] = index;
        }

        struct UnmarkedScatterJob : IJobParallelFor
        {
            public Holder holder;

            public void Execute(int index) => holder.data[holder.data.Length - 1 - index] = index;
        }

        [Fact]
        public void AParallelForRestrictionOffOnAStructFieldCoversItsContainers()
        {
            using var data = new NativeArray<int>(8, Allocator.TempJob);
            new ScatterJob { holder = new Holder { data = data } }.Schedule(8, 2).Complete();
            Assert.Equal(new[] { 7, 6, 5, 4, 3, 2, 1, 0 }, data.ToArray());
            // Without it each batch writes its own range only: the writes elsewhere fail.
            using var restricted = new NativeArray<int>(8, Allocator.TempJob);
            new UnmarkedScatterJob { holder = new Holder { data = restricted } }.Schedule(8, 2).Complete();
            Assert.NotEqual(new[] { 7, 6, 5, 4, 3, 2, 1, 0 }, restricted.ToArray());
        }

        /// <summary>A list's header as Unity.Collections lays it out: its elements, then its length.</summary>
        struct ListHeader
        {
            public int* Ptr;
            public int Length;
            public int Capacity;
        }

        struct GrowJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public ListHeader* list;

            public void Execute() => list->Length = 3;
        }

        struct FillJob : IJob
        {
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> items;

            public void Execute()
            {
                for (int i = 0; i < items.Length; i++) items[i] = 10 + i;
            }
        }

        [Fact]
        public void ADeferredArrayWithoutSafetyChecksTakesItsListAtTheStart()
        {
            var elements = stackalloc int[4];
            var list = new ListHeader { Ptr = elements, Length = 0, Capacity = 4 };
            // A deferred view: the list's address with the low bit set, no length yet.
            var deferred = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>((byte*)&list + 1, 0, Allocator.Invalid);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref deferred, AtomicSafetyHandle.Create());
            var grown = new GrowJob { list = &list }.Schedule();
            new FillJob { items = deferred }.Schedule(grown).Complete();
            Assert.Equal(new[] { 10, 11, 12, 0 }, new[] { elements[0], elements[1], elements[2], elements[3] });
            AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(deferred));
        }

        struct WriteViewJob : IJob
        {
            public NativeArray<int> view;

            public void Execute() => view[0] = 42;
        }

        [Fact]
        public void AJobsOwnArrayViewSurvivesTheSecondaryBumpOfScheduling()
        {
            using var storage = new NativeArray<int>(1, Allocator.TempJob);
            // A list's array view (NativeList.AsArray): the secondary version, writable, bumped
            // when a job that writes the list is scheduled.
            var safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(safety, true);
            var viewSafety = safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref viewSafety);
            AtomicSafetyHandle.SetAllowSecondaryVersionWriting(viewSafety, true);
            var view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(storage.GetUnsafePtr(), 1, Allocator.None);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, viewSafety);
            new WriteViewJob { view = view }.Schedule().Complete();
            Assert.Equal(42, storage[0]);
            AtomicSafetyHandle.Release(safety);
        }
    }
}
