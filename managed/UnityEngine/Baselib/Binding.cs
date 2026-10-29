using System;
using System.Runtime.InteropServices;
using Unbound.Runtime.Baselib;

namespace Unity.Baselib.LowLevel
{
    /// <summary>
    /// Unity's Baselib, the platform layer com.unity.transport, com.unity.logging and
    /// com.unity.collections call: implemented over .NET (sockets, files, timers, threads). The
    /// registered network (RIO) layer is emulated over ordinary sockets, as Baselib does on Linux.
    /// Layouts and error codes are Baselib's: callers read the structs' fields directly.
    /// </summary>
    internal static unsafe partial class Binding
    {
        public partial struct Baselib_DynamicLibrary_Handle
        {
            public IntPtr handle;
        }

        public enum Baselib_ErrorCode
        {
            Success = 0,
            OutOfMemory = 16777216,
            OutOfSystemResources = 16777217,
            InvalidAddressRange = 16777218,
            InvalidArgument = 16777219,
            InvalidBufferSize = 16777220,
            InvalidState = 16777221,
            NotSupported = 16777222,
            Timeout = 16777223,
            UnsupportedAlignment = 33554432,
            InvalidPageSize = 33554433,
            InvalidPageCount = 33554434,
            UnsupportedPageState = 33554435,
            ThreadCannotJoinSelf = 50331648,
            NetworkInitializationError = 67108864,
            AddressInUse = 67108865,
            AddressUnreachable = 67108866,
            AddressFamilyNotSupported = 67108867,
            Disconnected = 67108868,
            InvalidPathname = 83886080,
            RequestedAccessIsNotAllowed = 83886081,
            IOError = 83886082,
            FailedToOpenDynamicLibrary = 100663296,
            FunctionNotFound = 100663297,
            NoSupportedAddressFound = 117440512,
            TryAgain = 117440513,
            UnexpectedError = -1,
        }

        public enum Baselib_ErrorState_NativeErrorCodeType : byte
        {
            None = 0,
            PlatformDefined = 1,
        }

        public enum Baselib_ErrorState_ExtraInformationType : byte
        {
            None = 0,
            StaticString = 1,
            GenerationCounter = 2,
        }

        public partial struct Baselib_ErrorState
        {
            public Baselib_SourceLocation sourceLocation;
            public ulong nativeErrorCode;
            public ulong extraInformation;
            public Baselib_ErrorCode code;
            public Baselib_ErrorState_NativeErrorCodeType nativeErrorCodeType;
            public Baselib_ErrorState_ExtraInformationType extraInformationType;
        }

        public enum Baselib_ErrorState_ExplainVerbosity
        {
            ErrorType = 0,
            ErrorType_SourceLocation_Explanation = 1,
        }

        public partial struct Baselib_FileIO_EventQueue
        {
            public IntPtr handle;
        }

        public partial struct Baselib_FileIO_AsyncFile
        {
            public IntPtr handle;
        }

        public partial struct Baselib_FileIO_SyncFile
        {
            public IntPtr handle;
        }

        [Flags]
        public enum Baselib_FileIO_OpenFlags : uint
        {
            Read = 1u,
            Write = 2u,
            OpenAlways = 4u,
            CreateAlways = 8u,
        }

        public partial struct Baselib_FileIO_ReadRequest
        {
            public ulong offset;
            public IntPtr buffer;
            public ulong size;
        }

        public enum Baselib_FileIO_Priority
        {
            Normal = 0,
            High = 1,
        }

        public enum Baselib_FileIO_EventQueue_ResultType
        {
            Baselib_FileIO_EventQueue_Callback = 1,
            Baselib_FileIO_EventQueue_OpenFile = 2,
            Baselib_FileIO_EventQueue_ReadFile = 3,
            Baselib_FileIO_EventQueue_CloseFile = 4,
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void EventQueueCallback(ulong arg0);

        public partial struct Baselib_FileIO_EventQueue_Result_Callback
        {
            public IntPtr callback;
        }

        public partial struct Baselib_FileIO_EventQueue_Result_OpenFile
        {
            public ulong fileSize;
        }

        public partial struct Baselib_FileIO_EventQueue_Result_ReadFile
        {
            public ulong bytesTransferred;
        }

        [StructLayout(LayoutKind.Explicit)]
        public partial struct Baselib_FileIO_EventQueue_Result
        {
            [FieldOffset(0)]
            public Baselib_FileIO_EventQueue_ResultType type;
            [FieldOffset(8)]
            public ulong userdata;
            [FieldOffset(16)]
            public Baselib_ErrorState errorState;
            [FieldOffset(64)]
            public Baselib_FileIO_EventQueue_Result_Callback callback;
            [FieldOffset(64)]
            public Baselib_FileIO_EventQueue_Result_OpenFile openFile;
            [FieldOffset(64)]
            public Baselib_FileIO_EventQueue_Result_ReadFile readFile;
        }

        public partial struct Baselib_NetworkAddress_HostnameLookupHandle
        {
            public byte _placeholder;
        }

        public partial struct Baselib_Memory_PageSizeInfo
        {
            public ulong defaultPageSize;
            public ulong pageSizes0;
            public ulong pageSizes1;
            public ulong pageSizes2;
            public ulong pageSizes3;
            public ulong pageSizes4;
            public ulong pageSizes5;
            public ulong pageSizesLen;
        }

        public partial struct Baselib_Memory_PageAllocation
        {
            public IntPtr ptr;
            public ulong pageSize;
            public ulong pageCount;
        }

        public enum Baselib_Memory_PageState
        {
            Reserved = 0,
            NoAccess = 1,
            ReadOnly = 2,
            ReadWrite = 4,
            ReadOnly_Executable = 18,
            ReadWrite_Executable = 20,
        }

        public enum Baselib_NetworkAddress_Family
        {
            Invalid = 0,
            IPv4 = 1,
            IPv6 = 2,
        }

        [StructLayout(LayoutKind.Explicit)]
        public partial struct Baselib_NetworkAddress
        {
            [FieldOffset(0)] public byte data0;
            [FieldOffset(1)] public byte data1;
            [FieldOffset(2)] public byte data2;
            [FieldOffset(3)] public byte data3;
            [FieldOffset(4)] public byte data4;
            [FieldOffset(5)] public byte data5;
            [FieldOffset(6)] public byte data6;
            [FieldOffset(7)] public byte data7;
            [FieldOffset(8)] public byte data8;
            [FieldOffset(9)] public byte data9;
            [FieldOffset(10)] public byte data10;
            [FieldOffset(11)] public byte data11;
            [FieldOffset(12)] public byte data12;
            [FieldOffset(13)] public byte data13;
            [FieldOffset(14)] public byte data14;
            [FieldOffset(15)] public byte data15;
            [FieldOffset(0)] public byte ipv6_0;
            [FieldOffset(1)] public byte ipv6_1;
            [FieldOffset(2)] public byte ipv6_2;
            [FieldOffset(3)] public byte ipv6_3;
            [FieldOffset(4)] public byte ipv6_4;
            [FieldOffset(5)] public byte ipv6_5;
            [FieldOffset(6)] public byte ipv6_6;
            [FieldOffset(7)] public byte ipv6_7;
            [FieldOffset(8)] public byte ipv6_8;
            [FieldOffset(9)] public byte ipv6_9;
            [FieldOffset(10)] public byte ipv6_10;
            [FieldOffset(11)] public byte ipv6_11;
            [FieldOffset(12)] public byte ipv6_12;
            [FieldOffset(13)] public byte ipv6_13;
            [FieldOffset(14)] public byte ipv6_14;
            [FieldOffset(15)] public byte ipv6_15;
            [FieldOffset(0)] public byte ipv4_0;
            [FieldOffset(1)] public byte ipv4_1;
            [FieldOffset(2)] public byte ipv4_2;
            [FieldOffset(3)] public byte ipv4_3;
            [FieldOffset(16)] public byte port0;
            [FieldOffset(17)] public byte port1;
            [FieldOffset(18)] public byte family;
            [FieldOffset(19)] public byte _padding;
            [FieldOffset(20)] public uint ipv6_scope_id;
        }

        public enum Baselib_NetworkAddress_AddressReuse
        {
            DoNotAllow = 0,
            Allow = 1,
        }

        public partial struct Baselib_RegisteredNetwork_Buffer
        {
            public IntPtr id;
            public Baselib_Memory_PageAllocation allocation;
        }

        public partial struct Baselib_RegisteredNetwork_BufferSlice
        {
            public IntPtr id;
            public IntPtr data;
            public uint size;
            public uint offset;
        }

        public partial struct Baselib_RegisteredNetwork_Endpoint
        {
            public Baselib_RegisteredNetwork_BufferSlice slice;
        }

        public partial struct Baselib_RegisteredNetwork_Request
        {
            public Baselib_RegisteredNetwork_BufferSlice payload;
            public Baselib_RegisteredNetwork_Endpoint remoteEndpoint;
            public IntPtr requestUserdata;
        }

        public enum Baselib_RegisteredNetwork_CompletionStatus
        {
            Failed = 0,
            Success = 1,
        }

        public partial struct Baselib_RegisteredNetwork_CompletionResult
        {
            public Baselib_RegisteredNetwork_CompletionStatus status;
            public uint bytesTransferred;
            public IntPtr requestUserdata;
        }

        public partial struct Baselib_RegisteredNetwork_Socket_UDP
        {
            public IntPtr handle;
        }

        public enum Baselib_RegisteredNetwork_ProcessStatus
        {
            NonePendingImmediately = 0,
            Done = 0,
            Pending = 1,
        }

        public enum Baselib_RegisteredNetwork_CompletionQueueStatus
        {
            NoResultsAvailable = 0,
            ResultsAvailable = 1,
        }

        public partial struct Baselib_Socket_Handle
        {
            public IntPtr handle;
        }

        public enum Baselib_Socket_Protocol
        {
            UDP = 1,
            TCP = 2,
        }

        public partial struct Baselib_Socket_Message
        {
            public Baselib_NetworkAddress* address;
            public IntPtr data;
            public uint dataLen;
        }

        [Flags]
        public enum Baselib_Socket_PollEvents
        {
            Readable = 1,
            Writable = 2,
            Connected = 4,
        }

        public partial struct Baselib_Socket_PollFd
        {
            public Baselib_Socket_Handle handle;
            public Baselib_Socket_PollEvents requestedEvents;
            public Baselib_Socket_PollEvents resultEvents;
            public Baselib_ErrorState* errorState;
        }

        public partial struct Baselib_SourceLocation
        {
            public byte* file;
            public byte* function;
            public uint lineNumber;
        }

        public partial struct Baselib_SystemSemaphore_Handle
        {
            public IntPtr handle;
        }

        public partial struct Baselib_Timer_TickToNanosecondConversionRatio
        {
            public ulong ticksToNanosecondsNumerator;
            public ulong ticksToNanosecondsDenominator;
        }

        public enum Baselib_WakeupFallbackStrategy
        {
            OneByOne = 0,
            All = 1,
        }

        public static readonly UIntPtr Baselib_Memory_MaxAlignment = new UIntPtr(65536u);
        public static readonly UIntPtr Baselib_Memory_MinGuaranteedAlignment = new UIntPtr(8u);
        public const uint Baselib_NetworkAddress_IpMaxStringLength = 46u;
        public static readonly IntPtr Baselib_RegisteredNetwork_Buffer_Id_Invalid = IntPtr.Zero;
        public const uint Baselib_RegisteredNetwork_Endpoint_MaxSize = 28u;
        public const int Baselib_SystemSemaphore_MaxCount = int.MaxValue;
        public static readonly IntPtr Baselib_Thread_InvalidId = IntPtr.Zero;
        public static readonly UIntPtr Baselib_Thread_MaxThreadNameLength = new UIntPtr(64u);
        public const uint Baselib_TLS_MinimumGuaranteedSlots = 100u;
        public const ulong Baselib_SecondsPerMinute = 60uL;
        public const ulong Baselib_MillisecondsPerSecond = 1000uL;
        public const ulong Baselib_MillisecondsPerMinute = 60000uL;
        public const ulong Baselib_MicrosecondsPerMillisecond = 1000uL;
        public const ulong Baselib_MicrosecondsPerSecond = 1000000uL;
        public const ulong Baselib_MicrosecondsPerMinute = 60000000uL;
        public const ulong Baselib_NanosecondsPerMicrosecond = 1000uL;
        public const ulong Baselib_NanosecondsPerMillisecond = 1000000uL;
        public const ulong Baselib_NanosecondsPerSecond = 1000000000uL;
        public const ulong Baselib_NanosecondsPerMinute = 60000000000uL;
        public const ulong Baselib_Timer_MaxNumberOfNanosecondsPerTick = 1000uL;
        public const double Baselib_Timer_MinNumberOfNanosecondsPerTick = 0.01;
        public const double Baselib_Timer_HighPrecisionTimerCrossThreadMontotonyTolerance_InNanoseconds = 100.0;
        public static readonly Baselib_Memory_PageAllocation Baselib_Memory_PageAllocation_Invalid = default;
        public static readonly Baselib_RegisteredNetwork_Socket_UDP Baselib_RegisteredNetwork_Socket_UDP_Invalid = default;
        public static readonly Baselib_Socket_Handle Baselib_Socket_Handle_Invalid = new Baselib_Socket_Handle { handle = (IntPtr)(-1) };
        public static readonly Baselib_DynamicLibrary_Handle Baselib_DynamicLibrary_Handle_Invalid = new Baselib_DynamicLibrary_Handle { handle = (IntPtr)(-1) };
        public static readonly Baselib_FileIO_EventQueue Baselib_FileIO_EventQueue_Invalid = new Baselib_FileIO_EventQueue { handle = (IntPtr)0 };
        public static readonly Baselib_FileIO_AsyncFile Baselib_FileIO_AsyncFile_Invalid = new Baselib_FileIO_AsyncFile { handle = (IntPtr)0 };
        public static readonly Baselib_FileIO_SyncFile Baselib_FileIO_SyncFile_Invalid = new Baselib_FileIO_SyncFile { handle = (IntPtr)(-1) };

        // ------------------------------------------------------------------ dynamic libraries

        public static Baselib_DynamicLibrary_Handle Baselib_DynamicLibrary_OpenUtf8(byte* pathnameUtf8, Baselib_ErrorState* errorState) =>
            BaselibLibraries.Open(Marshal.PtrToStringUTF8((IntPtr)pathnameUtf8), errorState);

        public static Baselib_DynamicLibrary_Handle Baselib_DynamicLibrary_OpenUtf16(char* pathnameUtf16, Baselib_ErrorState* errorState) =>
            BaselibLibraries.Open(new string(pathnameUtf16), errorState);

        public static Baselib_DynamicLibrary_Handle Baselib_DynamicLibrary_OpenProgramHandle(Baselib_ErrorState* errorState) =>
            new Baselib_DynamicLibrary_Handle { handle = NativeLibrary.GetMainProgramHandle() };

        public static Baselib_DynamicLibrary_Handle Baselib_DynamicLibrary_FromNativeHandle(ulong handle, uint type, Baselib_ErrorState* errorState) =>
            new Baselib_DynamicLibrary_Handle { handle = (IntPtr)(long)handle };

        public static IntPtr Baselib_DynamicLibrary_GetFunction(Baselib_DynamicLibrary_Handle handle, byte* functionName, Baselib_ErrorState* errorState) =>
            BaselibLibraries.GetFunction(handle, Marshal.PtrToStringUTF8((IntPtr)functionName), errorState);

        public static void Baselib_DynamicLibrary_Close(Baselib_DynamicLibrary_Handle handle) => BaselibLibraries.Close(handle);

        // ------------------------------------------------------------------ errors

        /// <summary>Writes a description of the error (null-terminated) and returns its length.</summary>
        public static uint Baselib_ErrorState_Explain(Baselib_ErrorState* errorState, byte* buffer, uint bufferLen, Baselib_ErrorState_ExplainVerbosity verbosity)
        {
            var text = BaselibErrors.Explain(errorState, verbosity);
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            if (buffer != null && bufferLen > 0)
            {
                int n = (int)Math.Min((uint)bytes.Length, bufferLen - 1);
                bytes.AsSpan(0, n).CopyTo(new Span<byte>(buffer, n));
                buffer[n] = 0;
            }
            return (uint)bytes.Length;
        }

        // ------------------------------------------------------------------ files

        public static Baselib_FileIO_EventQueue Baselib_FileIO_EventQueue_Create() => BaselibFiles.CreateEventQueue();

        public static void Baselib_FileIO_EventQueue_Free(Baselib_FileIO_EventQueue eq) => BaselibFiles.FreeEventQueue(eq);

        public static ulong Baselib_FileIO_EventQueue_Dequeue(Baselib_FileIO_EventQueue eq, Baselib_FileIO_EventQueue_Result* results, ulong count, uint timeoutInMilliseconds) =>
            BaselibFiles.Dequeue(eq, results, count, timeoutInMilliseconds);

        public static void Baselib_FileIO_EventQueue_Shutdown(Baselib_FileIO_EventQueue eq, uint threadCount) => BaselibFiles.Shutdown(eq, threadCount);

        public static Baselib_FileIO_AsyncFile Baselib_FileIO_AsyncOpen(Baselib_FileIO_EventQueue eq, byte* pathname, ulong userdata, Baselib_FileIO_Priority priority) =>
            BaselibFiles.AsyncOpen(eq, Marshal.PtrToStringUTF8((IntPtr)pathname), userdata);

        public static void Baselib_FileIO_AsyncRead(Baselib_FileIO_AsyncFile file, Baselib_FileIO_ReadRequest* requests, ulong count, ulong userdata, Baselib_FileIO_Priority priority) =>
            BaselibFiles.AsyncRead(file, requests, count, userdata);

        public static void Baselib_FileIO_AsyncClose(Baselib_FileIO_AsyncFile file) => BaselibFiles.AsyncClose(file);

        public static Baselib_FileIO_SyncFile Baselib_FileIO_SyncOpen(byte* pathname, Baselib_FileIO_OpenFlags openFlags, Baselib_ErrorState* errorState) =>
            BaselibFiles.SyncOpen(Marshal.PtrToStringUTF8((IntPtr)pathname), openFlags, errorState);

        public static Baselib_FileIO_SyncFile Baselib_FileIO_SyncFileFromNativeHandle(ulong handle, uint type) => BaselibFiles.FromNativeHandle(handle);

        public static ulong Baselib_FileIO_SyncRead(Baselib_FileIO_SyncFile file, ulong offset, IntPtr buffer, ulong size, Baselib_ErrorState* errorState) =>
            BaselibFiles.SyncRead(file, offset, buffer, size, errorState);

        public static ulong Baselib_FileIO_SyncWrite(Baselib_FileIO_SyncFile file, ulong offset, IntPtr buffer, ulong size, Baselib_ErrorState* errorState) =>
            BaselibFiles.SyncWrite(file, offset, buffer, size, errorState);

        public static void Baselib_FileIO_SyncFlush(Baselib_FileIO_SyncFile file, Baselib_ErrorState* errorState) => BaselibFiles.SyncFlush(file, errorState);

        public static void Baselib_FileIO_SyncSetFileSize(Baselib_FileIO_SyncFile file, ulong size, Baselib_ErrorState* errorState) =>
            BaselibFiles.SyncSetFileSize(file, size, errorState);

        public static ulong Baselib_FileIO_SyncGetFileSize(Baselib_FileIO_SyncFile file, Baselib_ErrorState* errorState) => BaselibFiles.SyncGetFileSize(file, errorState);

        public static void Baselib_FileIO_SyncClose(Baselib_FileIO_SyncFile file, Baselib_ErrorState* errorState) => BaselibFiles.SyncClose(file, errorState);

        // ------------------------------------------------------------------ hostname lookup

        public static Baselib_NetworkAddress_HostnameLookupHandle* Baselib_NetworkAddress_HostnameLookup(byte* hostName, Baselib_NetworkAddress* dstAddress, Baselib_ErrorState* errorState) =>
            BaselibNet.HostnameLookup(Marshal.PtrToStringUTF8((IntPtr)hostName), dstAddress, errorState);

        public static bool Baselib_NetworkAddress_HostnameLookupCheckStatus(Baselib_NetworkAddress_HostnameLookupHandle* task, Baselib_NetworkAddress* dstAddress, Baselib_ErrorState* errorState) =>
            BaselibNet.HostnameLookupCheckStatus(task, dstAddress, errorState);

        public static void Baselib_NetworkAddress_HostnameLookupCancel(Baselib_NetworkAddress_HostnameLookupHandle* task) => BaselibNet.HostnameLookupCancel(task);

        // ------------------------------------------------------------------ memory

        public static void Baselib_Memory_GetPageSizeInfo(Baselib_Memory_PageSizeInfo* outPagesSizeInfo)
        {
            ulong page = (ulong)Environment.SystemPageSize;
            *outPagesSizeInfo = new Baselib_Memory_PageSizeInfo { defaultPageSize = page, pageSizes0 = page, pageSizesLen = 1 };
        }

        public static IntPtr Baselib_Memory_Allocate(UIntPtr size) => (IntPtr)NativeMemory.Alloc(Math.Max((nuint)size, 1));

        public static IntPtr Baselib_Memory_Reallocate(IntPtr ptr, UIntPtr newSize) => (IntPtr)NativeMemory.Realloc((void*)ptr, Math.Max((nuint)newSize, 1));

        public static void Baselib_Memory_Free(IntPtr ptr) => NativeMemory.Free((void*)ptr);

        public static IntPtr Baselib_Memory_AlignedAllocate(UIntPtr size, UIntPtr alignment) =>
            (IntPtr)NativeMemory.AlignedAlloc(Math.Max((nuint)size, 1), Math.Max((nuint)alignment, (nuint)sizeof(void*)));

        public static IntPtr Baselib_Memory_AlignedReallocate(IntPtr ptr, UIntPtr newSize, UIntPtr alignment) =>
            (IntPtr)NativeMemory.AlignedRealloc((void*)ptr, Math.Max((nuint)newSize, 1), Math.Max((nuint)alignment, (nuint)sizeof(void*)));

        public static void Baselib_Memory_AlignedFree(IntPtr ptr) => NativeMemory.AlignedFree((void*)ptr);

        public static Baselib_Memory_PageAllocation Baselib_Memory_AllocatePages(ulong pageSize, ulong pageCount, ulong alignmentInMultipleOfPageSize, Baselib_Memory_PageState pageState, Baselib_ErrorState* errorState) =>
            BaselibMemory.AllocatePages(pageSize, pageCount, alignmentInMultipleOfPageSize, errorState);

        public static Baselib_Memory_PageAllocation Baselib_Memory_AllocatePagesEx(ulong pageSize, ulong pageCount, ulong alignmentInMultipleOfPageSize, Baselib_Memory_PageState pageState, uint extPageState, Baselib_ErrorState* errorState) =>
            BaselibMemory.AllocatePages(pageSize, pageCount, alignmentInMultipleOfPageSize, errorState);

        public static void Baselib_Memory_ReleasePages(Baselib_Memory_PageAllocation pageAllocation, Baselib_ErrorState* errorState) => BaselibMemory.ReleasePages(pageAllocation, errorState);

        /// <summary>Page protection is not emulated: every page stays readable and writable.</summary>
        public static void Baselib_Memory_SetPageState(IntPtr addressOfFirstPage, ulong pageSize, ulong pageCount, Baselib_Memory_PageState pageState, Baselib_ErrorState* errorState) =>
            Unbound.Runtime.Stubs.Ignored("Baselib_Memory_SetPageState", "does not protect pages yet: they stay readable and writable");

        public static void Baselib_Memory_SetPageStateEx(IntPtr addressOfFirstPage, ulong pageSize, ulong pageCount, Baselib_Memory_PageState pageState, uint extPageState, Baselib_ErrorState* errorState) =>
            Unbound.Runtime.Stubs.Ignored("Baselib_Memory_SetPageStateEx", "does not protect pages yet: they stay readable and writable");

        // ------------------------------------------------------------------ network addresses

        public static void Baselib_NetworkAddress_Encode(Baselib_NetworkAddress* dstAddress, Baselib_NetworkAddress_Family family, byte* ip, ushort port, Baselib_ErrorState* errorState) =>
            BaselibNet.Encode(dstAddress, family, ip == null ? null : Marshal.PtrToStringUTF8((IntPtr)ip), port, errorState);

        public static void Baselib_NetworkAddress_Decode(Baselib_NetworkAddress* srcAddress, Baselib_NetworkAddress_Family* family, byte* ipAddressBuffer, uint ipAddressBufferLen, ushort* port, Baselib_ErrorState* errorState) =>
            BaselibNet.Decode(srcAddress, family, ipAddressBuffer, ipAddressBufferLen, port, errorState);

        // ------------------------------------------------------------------ registered network (emulated)

        [Unbound.EditorFaithful("registered networking is emulated on desktop, in Unity's Baselib too")]
        public static bool Baselib_RegisteredNetwork_IsEmulated() => true;

        public static Baselib_RegisteredNetwork_Buffer Baselib_RegisteredNetwork_Buffer_Register(Baselib_Memory_PageAllocation pageAllocation, Baselib_ErrorState* errorState) =>
            BaselibNet.RegisterBuffer(pageAllocation, errorState);

        [Unbound.EditorFaithful("the emulated registration holds nothing: the pages are released with ReleasePages")]
        public static void Baselib_RegisteredNetwork_Buffer_Deregister(Baselib_RegisteredNetwork_Buffer buffer) { }

        public static Baselib_RegisteredNetwork_BufferSlice Baselib_RegisteredNetwork_BufferSlice_Create(Baselib_RegisteredNetwork_Buffer buffer, uint offset, uint size) =>
            new Baselib_RegisteredNetwork_BufferSlice { id = buffer.id, data = buffer.allocation.ptr + (int)offset, size = size, offset = offset };

        [Unbound.EditorFaithful("the empty slice is all zeros by definition")]
        public static Baselib_RegisteredNetwork_BufferSlice Baselib_RegisteredNetwork_BufferSlice_Empty() => default;

        public static Baselib_RegisteredNetwork_Endpoint Baselib_RegisteredNetwork_Endpoint_Create(Baselib_NetworkAddress* srcAddress, Baselib_RegisteredNetwork_BufferSlice dstSlice, Baselib_ErrorState* errorState) =>
            BaselibNet.CreateEndpoint(srcAddress, dstSlice, errorState);

        [Unbound.EditorFaithful("the empty endpoint is all zeros by definition")]
        public static Baselib_RegisteredNetwork_Endpoint Baselib_RegisteredNetwork_Endpoint_Empty() => default;

        public static void Baselib_RegisteredNetwork_Endpoint_GetNetworkAddress(Baselib_RegisteredNetwork_Endpoint endpoint, Baselib_NetworkAddress* dstAddress, Baselib_ErrorState* errorState) =>
            BaselibNet.EndpointAddress(endpoint, dstAddress, errorState);

        public static Baselib_RegisteredNetwork_Socket_UDP Baselib_RegisteredNetwork_Socket_UDP_Create(Baselib_NetworkAddress* bindAddress, Baselib_NetworkAddress_AddressReuse endpointReuse, uint sendQueueSize, uint recvQueueSize, Baselib_ErrorState* errorState) =>
            BaselibNet.CreateRegisteredUdp(bindAddress, endpointReuse, sendQueueSize, recvQueueSize, errorState);

        public static uint Baselib_RegisteredNetwork_Socket_UDP_ScheduleRecv(Baselib_RegisteredNetwork_Socket_UDP socket, Baselib_RegisteredNetwork_Request* requests, uint requestsCount, Baselib_ErrorState* errorState) =>
            BaselibNet.Registered(socket).ScheduleRecv(requests, requestsCount);

        public static uint Baselib_RegisteredNetwork_Socket_UDP_ScheduleSend(Baselib_RegisteredNetwork_Socket_UDP socket, Baselib_RegisteredNetwork_Request* requests, uint requestsCount, Baselib_ErrorState* errorState) =>
            BaselibNet.Registered(socket).ScheduleSend(requests, requestsCount);

        public static Baselib_RegisteredNetwork_ProcessStatus Baselib_RegisteredNetwork_Socket_UDP_ProcessRecv(Baselib_RegisteredNetwork_Socket_UDP socket, Baselib_ErrorState* errorState) =>
            BaselibNet.Registered(socket).ProcessRecv(errorState);

        public static Baselib_RegisteredNetwork_ProcessStatus Baselib_RegisteredNetwork_Socket_UDP_ProcessSend(Baselib_RegisteredNetwork_Socket_UDP socket, Baselib_ErrorState* errorState) =>
            BaselibNet.Registered(socket).ProcessSend(errorState);

        public static Baselib_RegisteredNetwork_CompletionQueueStatus Baselib_RegisteredNetwork_Socket_UDP_WaitForCompletedRecv(Baselib_RegisteredNetwork_Socket_UDP socket, uint timeoutInMilliseconds, Baselib_ErrorState* errorState) =>
            BaselibNet.Registered(socket).WaitForCompletedRecv(timeoutInMilliseconds, errorState);

        public static Baselib_RegisteredNetwork_CompletionQueueStatus Baselib_RegisteredNetwork_Socket_UDP_WaitForCompletedSend(Baselib_RegisteredNetwork_Socket_UDP socket, uint timeoutInMilliseconds, Baselib_ErrorState* errorState) =>
            BaselibNet.Registered(socket).WaitForCompletedSend(timeoutInMilliseconds, errorState);

        public static uint Baselib_RegisteredNetwork_Socket_UDP_DequeueRecv(Baselib_RegisteredNetwork_Socket_UDP socket, Baselib_RegisteredNetwork_CompletionResult* results, uint resultsCount, Baselib_ErrorState* errorState) =>
            BaselibNet.Registered(socket).DequeueRecv(results, resultsCount);

        public static uint Baselib_RegisteredNetwork_Socket_UDP_DequeueSend(Baselib_RegisteredNetwork_Socket_UDP socket, Baselib_RegisteredNetwork_CompletionResult* results, uint resultsCount, Baselib_ErrorState* errorState) =>
            BaselibNet.Registered(socket).DequeueSend(results, resultsCount);

        public static void Baselib_RegisteredNetwork_Socket_UDP_GetNetworkAddress(Baselib_RegisteredNetwork_Socket_UDP socket, Baselib_NetworkAddress* dstAddress, Baselib_ErrorState* errorState) =>
            BaselibNet.Registered(socket).GetAddress(dstAddress, errorState);

        public static void Baselib_RegisteredNetwork_Socket_UDP_Close(Baselib_RegisteredNetwork_Socket_UDP socket) => BaselibNet.CloseRegisteredUdp(socket);

        // ------------------------------------------------------------------ sockets

        public static Baselib_Socket_Handle Baselib_Socket_Create(Baselib_NetworkAddress_Family family, Baselib_Socket_Protocol protocol, Baselib_ErrorState* errorState) =>
            BaselibNet.CreateSocket(family, protocol, errorState);

        public static void Baselib_Socket_Bind(Baselib_Socket_Handle socket, Baselib_NetworkAddress* address, Baselib_NetworkAddress_AddressReuse addressReuse, Baselib_ErrorState* errorState) =>
            BaselibNet.Bind(socket, address, addressReuse, errorState);

        public static void Baselib_Socket_TCP_Connect(Baselib_Socket_Handle socket, Baselib_NetworkAddress* address, Baselib_NetworkAddress_AddressReuse addressReuse, Baselib_ErrorState* errorState) =>
            BaselibNet.Connect(socket, address, addressReuse, errorState);

        public static void Baselib_Socket_Poll(Baselib_Socket_PollFd* sockets, uint socketsCount, uint timeoutInMilliseconds, Baselib_ErrorState* errorState) =>
            BaselibNet.Poll(sockets, socketsCount, timeoutInMilliseconds, errorState);

        public static void Baselib_Socket_GetAddress(Baselib_Socket_Handle socket, Baselib_NetworkAddress* address, Baselib_ErrorState* errorState) =>
            BaselibNet.GetAddress(socket, address, errorState);

        public static void Baselib_Socket_TCP_Listen(Baselib_Socket_Handle socket, Baselib_ErrorState* errorState) => BaselibNet.Listen(socket, errorState);

        public static Baselib_Socket_Handle Baselib_Socket_TCP_Accept(Baselib_Socket_Handle socket, Baselib_ErrorState* errorState) => BaselibNet.Accept(socket, errorState);

        public static uint Baselib_Socket_UDP_Send(Baselib_Socket_Handle socket, Baselib_Socket_Message* messages, uint messagesCount, Baselib_ErrorState* errorState) =>
            BaselibNet.UdpSend(socket, messages, messagesCount, errorState);

        public static uint Baselib_Socket_TCP_Send(Baselib_Socket_Handle socket, IntPtr data, uint dataLen, Baselib_ErrorState* errorState) =>
            BaselibNet.TcpSend(socket, data, dataLen, errorState);

        public static uint Baselib_Socket_UDP_Recv(Baselib_Socket_Handle socket, Baselib_Socket_Message* messages, uint messagesCount, Baselib_ErrorState* errorState) =>
            BaselibNet.UdpRecv(socket, messages, messagesCount, errorState);

        public static uint Baselib_Socket_TCP_Recv(Baselib_Socket_Handle socket, IntPtr data, uint dataLen, Baselib_ErrorState* errorState) =>
            BaselibNet.TcpRecv(socket, data, dataLen, errorState);

        public static void Baselib_Socket_Close(Baselib_Socket_Handle socket) => BaselibNet.Close(socket);

        // ------------------------------------------------------------------ threads and synchronization

        [Unbound.EditorFaithful("futexes are emulated (Baselib_SystemFutex_Wait/Notify work): there is no native support to report")]
        public static bool Baselib_SystemFutex_NativeSupport() => false;

        public static void Baselib_SystemFutex_Wait(IntPtr address, int expected, uint timeoutInMilliseconds) => BaselibThreads.FutexWait(address, expected, timeoutInMilliseconds);

        public static void Baselib_SystemFutex_Notify(IntPtr address, uint count, Baselib_WakeupFallbackStrategy wakeupFallbackStrategy) =>
            BaselibThreads.FutexNotify(address, count, wakeupFallbackStrategy == Baselib_WakeupFallbackStrategy.All);

        public static Baselib_SystemSemaphore_Handle Baselib_SystemSemaphore_Create() => BaselibThreads.CreateSemaphore();

        public static Baselib_SystemSemaphore_Handle Baselib_SystemSemaphore_CreateInplace(IntPtr semaphoreData) => BaselibThreads.CreateSemaphore();

        public static void Baselib_SystemSemaphore_Acquire(Baselib_SystemSemaphore_Handle semaphore) => BaselibThreads.Semaphore(semaphore).Wait();

        public static bool Baselib_SystemSemaphore_TryAcquire(Baselib_SystemSemaphore_Handle semaphore) => BaselibThreads.Semaphore(semaphore).Wait(0);

        public static bool Baselib_SystemSemaphore_TryTimedAcquire(Baselib_SystemSemaphore_Handle semaphore, uint timeoutInMilliseconds) =>
            BaselibThreads.Semaphore(semaphore).Wait((int)Math.Min(timeoutInMilliseconds, int.MaxValue));

        public static void Baselib_SystemSemaphore_Release(Baselib_SystemSemaphore_Handle semaphore, uint count)
        {
            if (count > 0) BaselibThreads.Semaphore(semaphore).Release((int)Math.Min(count, int.MaxValue));
        }

        public static void Baselib_SystemSemaphore_Free(Baselib_SystemSemaphore_Handle semaphore) => BaselibThreads.FreeSemaphore(semaphore);

        public static void Baselib_SystemSemaphore_FreeInplace(Baselib_SystemSemaphore_Handle semaphore) => BaselibThreads.FreeSemaphore(semaphore);

        public static void Baselib_Thread_YieldExecution() => System.Threading.Thread.Yield();

        public static IntPtr Baselib_Thread_GetCurrentThreadId() => (IntPtr)Environment.CurrentManagedThreadId;

        public static UIntPtr Baselib_TLS_Alloc() => BaselibThreads.TlsAlloc();

        public static void Baselib_TLS_Free(UIntPtr handle) => BaselibThreads.TlsFree(handle);

        public static void Baselib_TLS_Set(UIntPtr handle, UIntPtr value) => BaselibThreads.TlsSet(handle, value);

        public static UIntPtr Baselib_TLS_Get(UIntPtr handle) => BaselibThreads.TlsGet(handle);

        // ------------------------------------------------------------------ timers

        public static Baselib_Timer_TickToNanosecondConversionRatio Baselib_Timer_GetTicksToNanosecondsConversionRatio() => BaselibTimer.Ratio;

        public static ulong Baselib_Timer_GetHighPrecisionTimerTicks() => (ulong)System.Diagnostics.Stopwatch.GetTimestamp();

        public static void Baselib_Timer_WaitForAtLeast(uint timeInMilliseconds) => System.Threading.Thread.Sleep((int)Math.Min(timeInMilliseconds, int.MaxValue));

        public static double Baselib_Timer_GetTimeSinceStartupInSeconds() => BaselibTimer.SinceStartup;
    }
}

namespace Unity.Baselib
{
    internal partial struct ErrorState
    {
        LowLevel.Binding.Baselib_ErrorState nativeErrorState;

        public LowLevel.Binding.Baselib_ErrorCode ErrorCode => nativeErrorState.code;

        public unsafe LowLevel.Binding.Baselib_ErrorState* NativeErrorStatePtr
        {
            get
            {
                fixed (LowLevel.Binding.Baselib_ErrorState* result = &nativeErrorState) return result;
            }
        }

        public void ThrowIfFailed()
        {
            if (ErrorCode != LowLevel.Binding.Baselib_ErrorCode.Success) throw new BaselibException(this);
        }

        public unsafe string Explain(LowLevel.Binding.Baselib_ErrorState_ExplainVerbosity verbosity = LowLevel.Binding.Baselib_ErrorState_ExplainVerbosity.ErrorType_SourceLocation_Explanation)
        {
            fixed (LowLevel.Binding.Baselib_ErrorState* state = &nativeErrorState)
                return BaselibErrors.Explain(state, verbosity);
        }
    }

    internal partial class BaselibException : Exception
    {
        readonly ErrorState errorState;

        public LowLevel.Binding.Baselib_ErrorCode ErrorCode => errorState.ErrorCode;

        internal BaselibException(ErrorState errorState) : base(errorState.Explain())
        {
            this.errorState = errorState;
        }
    }

    internal static partial class BaselibNativeLibrary
    {
    }
}
