using System;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    /// <summary>
    /// LockBufferForWrite: the release's checks and messages, and an array over the locked range
    /// of the buffer's CPU copy (<see cref="GpuBufferStorage.Mirror"/>), written to the GPU whole
    /// when the lock ends: writes past countWritten are kept and elements the array does not write
    /// keep their contents. Matches the Unity 6000.0.71f1 Editor on OpenGL Core,
    /// tests/projects/srpmembers lock.*.
    /// </summary>
    public sealed partial class GraphicsBuffer
    {
        /// <summary>The safety handle of the array the open lock handed out.</summary>
        AtomicSafetyHandle m_Safety;
        bool m_Locked;
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

        /// <summary>Opens a lock on a byte range; a lock still open ends first, its array released.</summary>
        unsafe void* BeginBufferWrite(long offset, long size)
        {
            EndBufferWrite();
            byte* mirror = m_Storage.EnsureMirror();
            m_LockedOffset = offset;
            m_LockedSize = size;
            m_Locked = true;
            m_Storage.Locked = true;
            return mirror + offset;
        }

        unsafe void EndBufferWrite()
        {
            if (!m_Locked) return;
            if (m_LockedSize > 0) m_Storage.Write(m_LockedOffset, new ReadOnlySpan<byte>(m_Storage.Mirror + m_LockedOffset, (int)m_LockedSize));
            DropLock();
        }

        /// <summary>Ends the lock without writing; an array still handed out is released, so it
        /// throws rather than write into memory the buffer may free.</summary>
        void DropLock()
        {
            if (!m_Locked) return;
            ReleaseLockSafety(m_Safety);
            m_Safety = default;
            m_Locked = false;
            m_Storage.Locked = false;
        }

        static void ReleaseLockSafety(AtomicSafetyHandle safety)
        {
            if (safety.versionNode != IntPtr.Zero && Safety.IsAlive(safety)) AtomicSafetyHandle.Release(safety);
        }
    }
}
