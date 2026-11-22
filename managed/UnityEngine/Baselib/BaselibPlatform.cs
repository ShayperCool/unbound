using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using static Unity.Baselib.LowLevel.Binding;

namespace Unbound.Runtime.Baselib
{
    /// <summary>Objects Baselib hands out as opaque handles (GC handles of managed objects).</summary>
    internal static class BaselibHandles
    {
        internal static IntPtr New(object target) => GCHandle.ToIntPtr(GCHandle.Alloc(target));

        internal static T Get<T>(IntPtr handle) where T : class =>
            handle == IntPtr.Zero || handle == (IntPtr)(-1) ? null : GCHandle.FromIntPtr(handle).Target as T;

        internal static void Free(IntPtr handle)
        {
            if (handle != IntPtr.Zero && handle != (IntPtr)(-1)) GCHandle.FromIntPtr(handle).Free();
        }
    }

    internal static unsafe class BaselibErrors
    {
        /// <summary>Raises an error as Baselib does (Baselib_ErrorState_RaiseError in the release's
        /// Baselib_ErrorState.h): a state that already holds an error keeps it, for every caller
        /// (measured for SetPageState, tests/projects/corecaps <c>baselib.err.set.sticky</c>).</summary>
        internal static void Set(Baselib_ErrorState* state, Baselib_ErrorCode code, long nativeError = 0)
        {
            if (state == null || state->code != Baselib_ErrorCode.Success) return;
            state->code = code;
            state->nativeErrorCode = (ulong)nativeError;
            state->nativeErrorCodeType = nativeError != 0 ? Baselib_ErrorState_NativeErrorCodeType.PlatformDefined : Baselib_ErrorState_NativeErrorCodeType.None;
        }

        internal static string Explain(Baselib_ErrorState* state, Baselib_ErrorState_ExplainVerbosity verbosity)
        {
            if (state == null) return "";
            var text = $"{state->code} (0x{(uint)state->code:x8})";
            if (verbosity == Baselib_ErrorState_ExplainVerbosity.ErrorType_SourceLocation_Explanation && state->nativeErrorCodeType != Baselib_ErrorState_NativeErrorCodeType.None)
                text += $" - native error code {state->nativeErrorCode}";
            return text;
        }

        internal static Baselib_ErrorCode FromSocket(System.Net.Sockets.SocketError error) => error switch
        {
            System.Net.Sockets.SocketError.AddressAlreadyInUse => Baselib_ErrorCode.AddressInUse,
            System.Net.Sockets.SocketError.AddressNotAvailable => Baselib_ErrorCode.AddressUnreachable,
            System.Net.Sockets.SocketError.HostUnreachable => Baselib_ErrorCode.AddressUnreachable,
            System.Net.Sockets.SocketError.NetworkUnreachable => Baselib_ErrorCode.AddressUnreachable,
            System.Net.Sockets.SocketError.AddressFamilyNotSupported => Baselib_ErrorCode.AddressFamilyNotSupported,
            System.Net.Sockets.SocketError.ConnectionRefused => Baselib_ErrorCode.Disconnected,
            System.Net.Sockets.SocketError.ConnectionReset => Baselib_ErrorCode.Disconnected,
            System.Net.Sockets.SocketError.ConnectionAborted => Baselib_ErrorCode.Disconnected,
            System.Net.Sockets.SocketError.NotConnected => Baselib_ErrorCode.Disconnected,
            System.Net.Sockets.SocketError.Shutdown => Baselib_ErrorCode.Disconnected,
            System.Net.Sockets.SocketError.TimedOut => Baselib_ErrorCode.Timeout,
            System.Net.Sockets.SocketError.InvalidArgument => Baselib_ErrorCode.InvalidArgument,
            System.Net.Sockets.SocketError.MessageSize => Baselib_ErrorCode.InvalidBufferSize,
            System.Net.Sockets.SocketError.NoBufferSpaceAvailable => Baselib_ErrorCode.OutOfSystemResources,
            System.Net.Sockets.SocketError.TooManyOpenSockets => Baselib_ErrorCode.OutOfSystemResources,
            _ => Baselib_ErrorCode.UnexpectedError,
        };
    }

    internal static unsafe class BaselibLibraries
    {
        internal static Baselib_DynamicLibrary_Handle Open(string path, Baselib_ErrorState* error)
        {
            if (path != null && NativeLibrary.TryLoad(path, out var handle)) return new Baselib_DynamicLibrary_Handle { handle = handle };
            BaselibErrors.Set(error, Baselib_ErrorCode.FailedToOpenDynamicLibrary);
            return Baselib_DynamicLibrary_Handle_Invalid;
        }

        internal static IntPtr GetFunction(Baselib_DynamicLibrary_Handle library, string name, Baselib_ErrorState* error)
        {
            if (name != null && NativeLibrary.TryGetExport(library.handle, name, out var function)) return function;
            BaselibErrors.Set(error, Baselib_ErrorCode.FunctionNotFound);
            return IntPtr.Zero;
        }

        internal static void Close(Baselib_DynamicLibrary_Handle library)
        {
            if (library.handle != IntPtr.Zero && library.handle != (IntPtr)(-1) && library.handle != NativeLibrary.GetMainProgramHandle())
                NativeLibrary.Free(library.handle);
        }
    }

    /// <summary>Virtual memory page allocation and protection backed by POSIX mmap/munmap/mprotect/madvise,
    /// matching the Unity 6000.0.71f1 Linux Editor (tests/projects/corecaps <c>baselib.*</c>).</summary>
    internal static unsafe partial class BaselibMemory
    {
        const int PROT_NONE = 0, PROT_READ = 1, PROT_WRITE = 2, PROT_EXEC = 4;
        const int MAP_PRIVATE = 0x02;
        const int MAP_ANONYMOUS_LINUX = 0x20;
        const int MAP_ANON_MACOS = 0x1000;
        const int MADV_DONTNEED = 4;
        const int ENOMEM = 12, EACCES = 13, EINVAL = 22;
        static readonly IntPtr MAP_FAILED = (IntPtr)(-1);

        [LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
        private static partial IntPtr Mmap(IntPtr addr, nuint length, int prot, int flags, int fd, nint offset);

        [LibraryImport("libc", EntryPoint = "munmap", SetLastError = true)]
        private static partial int Munmap(IntPtr addr, nuint length);

        [LibraryImport("libc", EntryPoint = "mprotect", SetLastError = true)]
        private static partial int Mprotect(IntPtr addr, nuint length, int prot);

        [LibraryImport("libc", EntryPoint = "madvise", SetLastError = true)]
        private static partial int Madvise(IntPtr addr, nuint length, int advice);

        static int MapAnonymousFlag => OperatingSystem.IsMacOS() ? MAP_ANON_MACOS : MAP_ANONYMOUS_LINUX;

        static bool TryMapProtection(Baselib_Memory_PageState state, out int prot)
        {
            switch (state)
            {
                case Baselib_Memory_PageState.Reserved:
                case Baselib_Memory_PageState.NoAccess:
                    prot = PROT_NONE;
                    return true;
                case Baselib_Memory_PageState.ReadOnly:
                    prot = PROT_READ;
                    return true;
                case Baselib_Memory_PageState.ReadWrite:
                    prot = PROT_READ | PROT_WRITE;
                    return true;
                case Baselib_Memory_PageState.ReadOnly_Executable:
                    prot = PROT_READ | PROT_EXEC;
                    return true;
                case Baselib_Memory_PageState.ReadWrite_Executable:
                    prot = PROT_READ | PROT_WRITE | PROT_EXEC;
                    return true;
                default:
                    prot = PROT_NONE;
                    return false;
            }
        }

        static bool TryByteSize(ulong pageSize, ulong pageCount, out ulong bytes)
        {
            if (pageSize != 0 && pageCount <= ulong.MaxValue / pageSize)
            {
                ulong total = pageSize * pageCount;
                if (total <= (ulong)long.MaxValue)
                {
                    bytes = total;
                    return true;
                }
            }
            bytes = 0;
            return false;
        }

        static Baselib_ErrorCode MapProtectionError(int errno) => errno switch
        {
            ENOMEM => Baselib_ErrorCode.InvalidAddressRange,
            EACCES => Baselib_ErrorCode.UnsupportedPageState,
            EINVAL => Baselib_ErrorCode.InvalidAddressRange,
            _ => Baselib_ErrorCode.UnexpectedError,
        };

        internal static Baselib_Memory_PageAllocation AllocatePages(ulong pageSize, ulong pageCount, ulong alignmentInPages,
            Baselib_Memory_PageState pageState, uint extPageState, Baselib_ErrorState* error)
        {
            if (error != null && error->code != Baselib_ErrorCode.Success) return default;
            if (pageCount == 0)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidPageCount);
                return default;
            }
            ulong sysPage = (ulong)Environment.SystemPageSize;
            if (pageSize != sysPage)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidPageSize);
                return default;
            }
            if (alignmentInPages == 0 || (alignmentInPages & (alignmentInPages - 1)) != 0)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.UnsupportedAlignment);
                return default;
            }
            if (!TryByteSize(pageSize, pageCount, out ulong size))
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.OutOfMemory);
                return default;
            }
            if (!TryByteSize(pageSize, alignmentInPages, out ulong alignment))
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.UnsupportedAlignment);
                return default;
            }
            if (!TryMapProtection(pageState, out int prot))
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.UnsupportedPageState);
                return default;
            }
            ulong extra = alignment > pageSize ? alignment - pageSize : 0UL;
            if (size > (ulong)long.MaxValue - extra)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.OutOfMemory);
                return default;
            }
            ulong mapSize = size + extra;
            IntPtr mapped = Mmap(IntPtr.Zero, (nuint)mapSize, prot | (int)extPageState, MAP_PRIVATE | MapAnonymousFlag, -1, 0);
            if (mapped == MAP_FAILED)
            {
                int errno = Marshal.GetLastPInvokeError();
                var code = errno switch
                {
                    ENOMEM => Baselib_ErrorCode.OutOfMemory,
                    EACCES => Baselib_ErrorCode.UnsupportedPageState,
                    _ => Baselib_ErrorCode.UnexpectedError,
                };
                BaselibErrors.Set(error, code, errno);
                return default;
            }
            ulong raw = (ulong)mapped;
            ulong aligned = (raw + alignment - 1UL) & ~(alignment - 1UL);
            ulong lead = aligned - raw;
            ulong trail = mapSize - size - lead;
            if (lead > 0) Munmap((IntPtr)raw, (nuint)lead);
            if (trail > 0) Munmap((IntPtr)(aligned + size), (nuint)trail);
            return new Baselib_Memory_PageAllocation { ptr = (IntPtr)aligned, pageSize = pageSize, pageCount = pageCount };
        }

        internal static void SetPageState(IntPtr address, ulong pageSize, ulong pageCount,
            Baselib_Memory_PageState pageState, uint extPageState, Baselib_ErrorState* error)
        {
            if (address == IntPtr.Zero || pageCount == 0) return;
            if (error != null && error->code != Baselib_ErrorCode.Success) return;
            if (pageSize != (ulong)Environment.SystemPageSize)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidPageSize);
                return;
            }
            if (!TryByteSize(pageSize, pageCount, out ulong size))
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.OutOfMemory);
                return;
            }
            if (!TryMapProtection(pageState, out int prot))
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.UnsupportedPageState);
                return;
            }
            if (pageState == Baselib_Memory_PageState.Reserved)
            {
                if (Madvise(address, (nuint)size, MADV_DONTNEED) != 0)
                {
                    int errno = Marshal.GetLastPInvokeError();
                    BaselibErrors.Set(error, MapProtectionError(errno), errno);
                    return;
                }
            }
            if (Mprotect(address, (nuint)size, prot | (int)extPageState) != 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                BaselibErrors.Set(error, MapProtectionError(errno), errno);
            }
        }

        internal static void ReleasePages(Baselib_Memory_PageAllocation allocation, Baselib_ErrorState* error)
        {
            if (allocation.ptr == IntPtr.Zero || allocation.pageCount == 0) return;
            if (error != null && error->code != Baselib_ErrorCode.Success) return;
            if (allocation.pageSize != (ulong)Environment.SystemPageSize)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidPageSize);
                return;
            }
            if (!TryByteSize(allocation.pageSize, allocation.pageCount, out ulong size))
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.OutOfMemory);
                return;
            }
            if (Munmap(allocation.ptr, (nuint)size) != 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                BaselibErrors.Set(error, errno == EINVAL ? Baselib_ErrorCode.InvalidAddressRange : Baselib_ErrorCode.UnexpectedError, errno);
            }
        }
    }

    internal static unsafe class BaselibTimer
    {
        /// <summary>The process start on the high precision clock: Baselib counts time since startup
        /// (callers such as com.unity.transport assume it is never near zero).</summary>
        static readonly long s_Start = Stopwatch.GetTimestamp() -
            (long)((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds * Stopwatch.Frequency);

        internal static double SinceStartup => (Stopwatch.GetTimestamp() - s_Start) / (double)Stopwatch.Frequency;

        internal static Baselib_Timer_TickToNanosecondConversionRatio Ratio
        {
            get
            {
                ulong numerator = 1_000_000_000UL, denominator = (ulong)Stopwatch.Frequency;
                ulong a = numerator, b = denominator;
                while (b != 0) (a, b) = (b, a % b);
                return new Baselib_Timer_TickToNanosecondConversionRatio { ticksToNanosecondsNumerator = numerator / a, ticksToNanosecondsDenominator = denominator / a };
            }
        }
    }

    internal static class BaselibThreads
    {
        // Thread-local storage: slot indices, values per thread.
        static readonly object s_TlsLock = new object();
        static readonly Stack<int> s_FreeSlots = new Stack<int>();
        static int s_NextSlot = 1;
        [ThreadStatic] static UIntPtr[] t_Slots;

        internal static UIntPtr TlsAlloc()
        {
            lock (s_TlsLock) return (UIntPtr)(s_FreeSlots.Count > 0 ? s_FreeSlots.Pop() : s_NextSlot++);
        }

        internal static void TlsFree(UIntPtr handle)
        {
            lock (s_TlsLock) s_FreeSlots.Push((int)handle);
        }

        internal static void TlsSet(UIntPtr handle, UIntPtr value)
        {
            int slot = (int)handle;
            var slots = t_Slots;
            if (slots == null || slots.Length <= slot) Array.Resize(ref t_Slots, Math.Max(slot + 1, 128));
            t_Slots[slot] = value;
        }

        internal static UIntPtr TlsGet(UIntPtr handle)
        {
            int slot = (int)handle;
            var slots = t_Slots;
            return slots != null && slot < slots.Length ? slots[slot] : UIntPtr.Zero;
        }

        // Futexes: waiters per address.
        static readonly ConcurrentDictionary<IntPtr, object> s_Futexes = new ConcurrentDictionary<IntPtr, object>();

        internal static unsafe void FutexWait(IntPtr address, int expected, uint timeoutInMilliseconds)
        {
            var monitor = s_Futexes.GetOrAdd(address, _ => new object());
            lock (monitor)
            {
                if (Volatile.Read(ref *(int*)address) != expected) return;
                Monitor.Wait(monitor, (int)Math.Min(timeoutInMilliseconds, int.MaxValue));
            }
        }

        internal static void FutexNotify(IntPtr address, uint count, bool all)
        {
            if (!s_Futexes.TryGetValue(address, out var monitor)) return;
            lock (monitor)
            {
                if (all || count == uint.MaxValue) Monitor.PulseAll(monitor);
                else
                    for (uint i = 0; i < count; i++) Monitor.Pulse(monitor);
            }
        }

        internal static Baselib_SystemSemaphore_Handle CreateSemaphore() =>
            new Baselib_SystemSemaphore_Handle { handle = BaselibHandles.New(new SemaphoreSlim(0, Baselib_SystemSemaphore_MaxCount)) };

        internal static SemaphoreSlim Semaphore(Baselib_SystemSemaphore_Handle handle) =>
            BaselibHandles.Get<SemaphoreSlim>(handle.handle) ?? throw new ArgumentException("Invalid Baselib semaphore");

        internal static void FreeSemaphore(Baselib_SystemSemaphore_Handle handle)
        {
            BaselibHandles.Get<SemaphoreSlim>(handle.handle)?.Dispose();
            BaselibHandles.Free(handle.handle);
        }
    }

    /// <summary>Baselib's file IO: synchronous files over positioned reads and writes, and the
    /// asynchronous event queue completing requests in order.</summary>
    internal static unsafe class BaselibFiles
    {
        static Baselib_ErrorCode Code(Exception e) => e switch
        {
            FileNotFoundException => Baselib_ErrorCode.InvalidPathname,
            DirectoryNotFoundException => Baselib_ErrorCode.InvalidPathname,
            UnauthorizedAccessException => Baselib_ErrorCode.RequestedAccessIsNotAllowed,
            ArgumentException => Baselib_ErrorCode.InvalidPathname,
            _ => Baselib_ErrorCode.IOError,
        };

        internal static Baselib_FileIO_SyncFile SyncOpen(string path, Baselib_FileIO_OpenFlags flags, Baselib_ErrorState* error)
        {
            try
            {
                var access = (flags & (Baselib_FileIO_OpenFlags.Read | Baselib_FileIO_OpenFlags.Write)) switch
                {
                    Baselib_FileIO_OpenFlags.Write => FileAccess.Write,
                    Baselib_FileIO_OpenFlags.Read | Baselib_FileIO_OpenFlags.Write => FileAccess.ReadWrite,
                    _ => FileAccess.Read,
                };
                var mode = (flags & Baselib_FileIO_OpenFlags.CreateAlways) != 0 ? FileMode.Create
                    : (flags & Baselib_FileIO_OpenFlags.OpenAlways) != 0 ? FileMode.OpenOrCreate
                    : FileMode.Open;
                var handle = System.IO.File.OpenHandle(path, mode, access, FileShare.ReadWrite | FileShare.Delete);
                return new Baselib_FileIO_SyncFile { handle = BaselibHandles.New(handle) };
            }
            catch (Exception e)
            {
                BaselibErrors.Set(error, Code(e));
                return Baselib_FileIO_SyncFile_Invalid;
            }
        }

        internal static Baselib_FileIO_SyncFile FromNativeHandle(ulong handle) =>
            new Baselib_FileIO_SyncFile { handle = BaselibHandles.New(new SafeFileHandle((IntPtr)(long)handle, ownsHandle: false)) };

        static SafeFileHandle FileOf(Baselib_FileIO_SyncFile file, Baselib_ErrorState* error)
        {
            var handle = BaselibHandles.Get<SafeFileHandle>(file.handle);
            if (handle == null) BaselibErrors.Set(error, Baselib_ErrorCode.InvalidArgument);
            return handle;
        }

        internal static ulong SyncRead(Baselib_FileIO_SyncFile file, ulong offset, IntPtr buffer, ulong size, Baselib_ErrorState* error)
        {
            var handle = FileOf(file, error);
            if (handle == null) return 0;
            try
            {
                ulong total = 0;
                while (total < size)
                {
                    int chunk = (int)Math.Min(size - total, int.MaxValue);
                    int read = RandomAccess.Read(handle, new Span<byte>((byte*)buffer + total, chunk), (long)(offset + total));
                    if (read == 0) break;
                    total += (ulong)read;
                }
                return total;
            }
            catch (Exception e)
            {
                BaselibErrors.Set(error, Code(e));
                return 0;
            }
        }

        internal static ulong SyncWrite(Baselib_FileIO_SyncFile file, ulong offset, IntPtr buffer, ulong size, Baselib_ErrorState* error)
        {
            var handle = FileOf(file, error);
            if (handle == null) return 0;
            try
            {
                ulong total = 0;
                while (total < size)
                {
                    int chunk = (int)Math.Min(size - total, int.MaxValue);
                    RandomAccess.Write(handle, new ReadOnlySpan<byte>((byte*)buffer + total, chunk), (long)(offset + total));
                    total += (ulong)chunk;
                }
                return total;
            }
            catch (Exception e)
            {
                BaselibErrors.Set(error, Code(e));
                return 0;
            }
        }

        /// <summary>Positioned writes go straight to the OS: nothing is buffered to flush.</summary>
        internal static void SyncFlush(Baselib_FileIO_SyncFile file, Baselib_ErrorState* error) => FileOf(file, error);

        internal static void SyncSetFileSize(Baselib_FileIO_SyncFile file, ulong size, Baselib_ErrorState* error)
        {
            var handle = FileOf(file, error);
            if (handle == null) return;
            try
            {
                RandomAccess.SetLength(handle, (long)size);
            }
            catch (Exception e)
            {
                BaselibErrors.Set(error, Code(e));
            }
        }

        internal static ulong SyncGetFileSize(Baselib_FileIO_SyncFile file, Baselib_ErrorState* error)
        {
            var handle = FileOf(file, error);
            if (handle == null) return 0;
            try
            {
                return (ulong)RandomAccess.GetLength(handle);
            }
            catch (Exception e)
            {
                BaselibErrors.Set(error, Code(e));
                return 0;
            }
        }

        internal static void SyncClose(Baselib_FileIO_SyncFile file, Baselib_ErrorState* error)
        {
            var handle = FileOf(file, error);
            if (handle == null) return;
            handle.Dispose();
            BaselibHandles.Free(file.handle);
        }

        // ------------------------------------------------------------------ asynchronous

        sealed class EventQueue
        {
            public readonly Queue<Baselib_FileIO_EventQueue_Result> Results = new Queue<Baselib_FileIO_EventQueue_Result>();
            public bool ShutDown;
        }

        sealed class AsyncFile
        {
            public EventQueue Queue;
            public SafeFileHandle Handle;
        }

        internal static Baselib_FileIO_EventQueue CreateEventQueue() => new Baselib_FileIO_EventQueue { handle = BaselibHandles.New(new EventQueue()) };

        internal static void FreeEventQueue(Baselib_FileIO_EventQueue eq) => BaselibHandles.Free(eq.handle);

        internal static void Shutdown(Baselib_FileIO_EventQueue eq, uint threadCount)
        {
            var queue = BaselibHandles.Get<EventQueue>(eq.handle);
            if (queue == null) return;
            lock (queue)
            {
                queue.ShutDown = true;
                Monitor.PulseAll(queue);
            }
        }

        static void Post(EventQueue queue, Baselib_FileIO_EventQueue_Result result)
        {
            lock (queue)
            {
                queue.Results.Enqueue(result);
                Monitor.PulseAll(queue);
            }
        }

        internal static ulong Dequeue(Baselib_FileIO_EventQueue eq, Baselib_FileIO_EventQueue_Result* results, ulong count, uint timeoutInMilliseconds)
        {
            var queue = BaselibHandles.Get<EventQueue>(eq.handle);
            if (queue == null || count == 0) return 0;
            lock (queue)
            {
                if (queue.Results.Count == 0 && timeoutInMilliseconds > 0 && !queue.ShutDown)
                    Monitor.Wait(queue, (int)Math.Min(timeoutInMilliseconds, int.MaxValue));
                ulong n = 0;
                while (n < count && queue.Results.Count > 0) results[n++] = queue.Results.Dequeue();
                return n;
            }
        }

        internal static Baselib_FileIO_AsyncFile AsyncOpen(Baselib_FileIO_EventQueue eq, string path, ulong userdata)
        {
            var queue = BaselibHandles.Get<EventQueue>(eq.handle);
            var result = new Baselib_FileIO_EventQueue_Result { type = Baselib_FileIO_EventQueue_ResultType.Baselib_FileIO_EventQueue_OpenFile, userdata = userdata };
            var file = new AsyncFile { Queue = queue };
            try
            {
                file.Handle = System.IO.File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                result.openFile.fileSize = (ulong)RandomAccess.GetLength(file.Handle);
            }
            catch (Exception e)
            {
                BaselibErrors.Set(&result.errorState, Code(e));
            }
            Post(queue, result);
            return new Baselib_FileIO_AsyncFile { handle = BaselibHandles.New(file) };
        }

        internal static void AsyncRead(Baselib_FileIO_AsyncFile file, Baselib_FileIO_ReadRequest* requests, ulong count, ulong userdata)
        {
            var async = BaselibHandles.Get<AsyncFile>(file.handle);
            if (async == null) return;
            for (ulong i = 0; i < count; i++)
            {
                var result = new Baselib_FileIO_EventQueue_Result { type = Baselib_FileIO_EventQueue_ResultType.Baselib_FileIO_EventQueue_ReadFile, userdata = userdata };
                if (async.Handle == null)
                {
                    BaselibErrors.Set(&result.errorState, Baselib_ErrorCode.InvalidArgument);
                }
                else
                {
                    try
                    {
                        result.readFile.bytesTransferred = (ulong)RandomAccess.Read(async.Handle,
                            new Span<byte>((void*)requests[i].buffer, (int)Math.Min(requests[i].size, int.MaxValue)), (long)requests[i].offset);
                    }
                    catch (Exception e)
                    {
                        BaselibErrors.Set(&result.errorState, Code(e));
                    }
                }
                Post(async.Queue, result);
            }
        }

        internal static void AsyncClose(Baselib_FileIO_AsyncFile file)
        {
            var async = BaselibHandles.Get<AsyncFile>(file.handle);
            if (async == null) return;
            async.Handle?.Dispose();
            BaselibHandles.Free(file.handle);
        }
    }
}
