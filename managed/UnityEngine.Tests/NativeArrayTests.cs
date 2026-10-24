using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Xunit;

namespace UnityEngine.Tests
{
    public unsafe class NativeArrayTests
    {
        [Fact]
        public void ElementsLiveInNativeMemorySharedWithItsPointer()
        {
            var array = new NativeArray<int>(4, Allocator.Persistent);
            Assert.Equal(0, array[3]); // cleared
            array[1] = 7;
            var p = (int*)array.GetUnsafePtr();
            Assert.Equal(7, p[1]);
            p[2] = 9;
            Assert.Equal(9, array[2]);
            Assert.Equal(new[] { 0, 7, 9, 0 }, array.ToArray());
            Assert.Throws<IndexOutOfRangeException>(() => array[4]);
            array.Dispose();
            Assert.False(array.IsCreated);
        }

        [Fact]
        public void SubArraysReinterpretAndSlicesAliasTheSameMemory()
        {
            var array = new NativeArray<int>(new[] { 1, 2, 3, 4 }, Allocator.Temp);
            var sub = array.GetSubArray(1, 2);
            sub[0] = 20;
            Assert.Equal(20, array[1]);
            // Reinterpret<U>() needs equal sizes; with the source size given, the length scales.
            Assert.Throws<InvalidOperationException>(() => array.Reinterpret<byte>());
            Assert.Throws<InvalidOperationException>(() => array.Reinterpret<byte>(2));
            Assert.Equal(4, array.Reinterpret<float>(4).Length);
            var asBytes = array.Reinterpret<byte>(4);
            Assert.Equal(16, asBytes.Length);
            Assert.Equal(20, asBytes[4]);
            NativeSlice<int> slice = array;
            Assert.Equal(4, slice.Length);
            Assert.Equal(3, new NativeSlice<int>(array, 2)[0]);
            array.Dispose();
        }

        [Fact]
        public void ExistingMemoryAndTheUnsafeHelpers()
        {
            long* memory = stackalloc long[2];
            var view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<long>(memory, 2, Allocator.None);
            // Without a safety handle, access fails as in the Editor.
            Assert.Throws<NullReferenceException>(() => view[0]);
            var safety = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, safety);
            view[0] = 5;
            Assert.Equal(5, memory[0]);
            void* block = UnsafeUtility.Malloc(32, 16, Allocator.Persistent);
            UnsafeUtility.MemSet(block, 0xAB, 32);
            Assert.Equal(0xAB, ((byte*)block)[31]);
            UnsafeUtility.MemClear(block, 32);
            Assert.Equal(0, UnsafeUtility.MemCmp(block, (byte*)block + 16, 16));
            UnsafeUtility.Free(block, Allocator.Persistent);
            AtomicSafetyHandle.Release(safety);
            Assert.Throws<ObjectDisposedException>(() => view[0]);
            Assert.Throws<InvalidOperationException>(() => new NativeArray<Wrapper>(1, Allocator.Temp));
        }

        struct Wrapper { public string s; }
    }
}
