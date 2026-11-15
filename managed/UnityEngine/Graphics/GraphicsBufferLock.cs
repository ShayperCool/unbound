using System;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    /// <summary>
    /// LockBufferForWrite: the release's checks and messages, an array over the locked range's
    /// current contents, written back whole when the lock ends. Matches the Unity 6000.0.71f1
    /// Editor on OpenGL Core, tests/projects/srpmembers lock.*.
    /// </summary>
    public sealed partial class GraphicsBuffer
    {
        /// <summary>The safety handle of the array the open lock handed out.</summary>
        AtomicSafetyHandle m_Safety;
        /// <summary>The open lock's memory (writes past countWritten are kept: lock.writePartial).</summary>
        unsafe byte* m_Locked;
        long m_LockedOffset, m_LockedSize;

        public unsafe NativeArray<T> LockBufferForWrite<T>(int bufferStartIndex, int count) where T : struct
        {
            if (!IsValid()) throw new InvalidOperationException("LockBufferForWrite requires a valid GraphicsBuffer");
            if ((usageFlags & UsageFlags.LockBufferForWrite) == 0)
                throw new InvalidOperationException("GraphicsBuffer must be created with usage mode UsageFlage.LockBufferForWrite to use LockBufferForWrite");
            int size = UnsafeUtility.SizeOf<T>();
            if (bufferStartIndex < 0 || count < 0 || (bufferStartIndex + count) * size > this.count * stride)
                throw MonoExceptions.OutOfRange($"Bad indices/count arguments (bufferStartIndex:{bufferStartIndex} count:{count} elementSize:{size}, this.count:{this.count}, this.stride{stride})");
            void* pointer = BeginBufferWrite((long)bufferStartIndex * size, (long)count * size);
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(pointer, count, Allocator.Invalid);
            m_Safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetAllowSecondaryVersionWriting(m_Safety, allowWriting: true);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_Safety);
            return array;
        }

        public void UnlockBufferAfterWrite<T>(int countWritten) where T : struct
        {
            try
            {
                AtomicSafetyHandle.CheckExistsAndThrow(in m_Safety);
                AtomicSafetyHandle.Release(m_Safety);
            }
            catch (Exception inner)
            {
                throw new InvalidOperationException("GraphicsBuffer.UnlockBufferAfterWrite was called without matching GraphicsBuffer.LockBufferForWrite", inner);
            }
            // The array is released either way; a bad count leaves the lock open
            // (lock.negativeCountWritten, lock.afterNegativeCountWritten).
            if (countWritten < 0) throw MonoExceptions.OutOfRange($"Bad indices/count arguments (countWritten:{countWritten})");
            EndBufferWrite();
        }

        /// <summary>Opens a lock on a byte range; a lock still open ends first.</summary>
        unsafe void* BeginBufferWrite(long offset, long size)
        {
            EndBufferWrite();
            m_Locked = (byte*)NativeMemory.Alloc((nuint)Math.Max(size, 1));
            var locked = new Span<byte>(m_Locked, (int)size);
            var current = size > 0 ? Native.ReadBuffer(m_Storage.Handle, offset, size) : null;
            if (current != null) current.AsSpan(0, Math.Min(current.Length, locked.Length)).CopyTo(locked);
            else locked.Clear();
            m_LockedOffset = offset;
            m_LockedSize = size;
            m_Storage.Locked = true;
            return m_Locked;
        }

        unsafe void EndBufferWrite()
        {
            if (m_Locked == null) return;
            if (m_LockedSize > 0) m_Storage.Write(m_LockedOffset, new ReadOnlySpan<byte>(m_Locked, (int)m_LockedSize));
            DropLock();
        }

        unsafe void DropLock()
        {
            if (m_Locked == null) return;
            NativeMemory.Free(m_Locked);
            m_Locked = null;
            m_Storage.Locked = false;
        }
    }
}
