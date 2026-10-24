using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Unity.Baselib.LowLevel.Binding;
using Trace = Unbound.Runtime.Trace;

namespace Unbound.Runtime.Baselib
{
    /// <summary>
    /// Baselib's sockets over .NET sockets (non-blocking, as Baselib's are), network addresses in
    /// Baselib's layout, and the registered network (RIO) API emulated with request and completion
    /// queues over a UDP socket, the way Baselib emulates it where the OS has no RIO.
    /// </summary>
    internal static unsafe class BaselibNet
    {
        // ------------------------------------------------------------------ addresses

        internal static void Encode(Baselib_NetworkAddress* dst, Baselib_NetworkAddress_Family family, string ip, ushort port, Baselib_ErrorState* error)
        {
            if (dst == null || ip == null || !TryParse(ip, family, out var address))
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidArgument);
                return;
            }
            Write(dst, new IPEndPoint(address, port));
        }

        /// <summary>Strict numeric parsing, as inet_pton does (no host names, no shorthand IPv4).</summary>
        static bool TryParse(string text, Baselib_NetworkAddress_Family family, out IPAddress address)
        {
            address = null;
            if (family == Baselib_NetworkAddress_Family.IPv4)
            {
                var parts = text.Split('.');
                if (parts.Length != 4) return false;
                var bytes = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    if (parts[i].Length == 0 || parts[i].Length > 3) return false;
                    foreach (char c in parts[i])
                        if (c < '0' || c > '9') return false;
                    int value = int.Parse(parts[i]);
                    if (value > 255) return false;
                    bytes[i] = (byte)value;
                }
                address = new IPAddress(bytes);
                return true;
            }
            if (family == Baselib_NetworkAddress_Family.IPv6)
                return text.Contains(':') && IPAddress.TryParse(text, out address) && address.AddressFamily == AddressFamily.InterNetworkV6;
            return false;
        }

        internal static void Write(Baselib_NetworkAddress* dst, IPEndPoint endpoint)
        {
            *dst = default;
            var bytes = endpoint.Address.GetAddressBytes();
            var data = (byte*)dst;
            for (int i = 0; i < bytes.Length && i < 16; i++) data[i] = bytes[i];
            dst->port0 = (byte)(endpoint.Port >> 8);
            dst->port1 = (byte)endpoint.Port;
            dst->family = (byte)(endpoint.AddressFamily == AddressFamily.InterNetwork ? Baselib_NetworkAddress_Family.IPv4 : Baselib_NetworkAddress_Family.IPv6);
            if (endpoint.AddressFamily == AddressFamily.InterNetworkV6) dst->ipv6_scope_id = (uint)endpoint.Address.ScopeId;
        }

        internal static IPEndPoint Read(Baselib_NetworkAddress* src)
        {
            if (src == null) return null;
            int port = (src->port0 << 8) | src->port1;
            var data = (byte*)src;
            switch ((Baselib_NetworkAddress_Family)src->family)
            {
                case Baselib_NetworkAddress_Family.IPv4:
                    return new IPEndPoint(new IPAddress(new ReadOnlySpan<byte>(data, 4)), port);
                case Baselib_NetworkAddress_Family.IPv6:
                    return new IPEndPoint(new IPAddress(new ReadOnlySpan<byte>(data, 16), src->ipv6_scope_id), port);
                default:
                    return null;
            }
        }

        internal static void Decode(Baselib_NetworkAddress* src, Baselib_NetworkAddress_Family* family, byte* buffer, uint bufferLength, ushort* port, Baselib_ErrorState* error)
        {
            var endpoint = Read(src);
            if (endpoint == null)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidArgument);
                return;
            }
            if (family != null) *family = (Baselib_NetworkAddress_Family)src->family;
            if (port != null) *port = (ushort)endpoint.Port;
            if (buffer != null)
            {
                var text = Encoding.ASCII.GetBytes(endpoint.Address.ToString());
                if (text.Length + 1 > bufferLength)
                {
                    BaselibErrors.Set(error, Baselib_ErrorCode.InvalidBufferSize);
                    return;
                }
                text.CopyTo(new Span<byte>(buffer, text.Length));
                buffer[text.Length] = 0;
            }
        }

        static AddressFamily FamilyOf(Baselib_NetworkAddress_Family family) =>
            family == Baselib_NetworkAddress_Family.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

        // ------------------------------------------------------------------ hostname lookups

        sealed class Lookup
        {
            public Task<IPAddress[]> Task;
            public CancellationTokenSource Cancel = new CancellationTokenSource();
        }

        internal static Baselib_NetworkAddress_HostnameLookupHandle* HostnameLookup(string host, Baselib_NetworkAddress* dst, Baselib_ErrorState* error)
        {
            if (host == null)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidArgument);
                return null;
            }
            var lookup = new Lookup();
            lookup.Task = Dns.GetHostAddressesAsync(host, lookup.Cancel.Token);
            return (Baselib_NetworkAddress_HostnameLookupHandle*)BaselibHandles.New(lookup);
        }

        /// <summary>Whether the lookup finished; then <paramref name="dst"/> has its first address
        /// (the port is kept) and the handle is freed.</summary>
        internal static bool HostnameLookupCheckStatus(Baselib_NetworkAddress_HostnameLookupHandle* task, Baselib_NetworkAddress* dst, Baselib_ErrorState* error)
        {
            var lookup = BaselibHandles.Get<Lookup>((IntPtr)task);
            if (lookup == null)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidArgument);
                return true;
            }
            if (!lookup.Task.IsCompleted) return false;
            if (lookup.Task.IsCompletedSuccessfully && lookup.Task.Result.Length > 0)
            {
                ushort port = (ushort)((dst->port0 << 8) | dst->port1);
                Write(dst, new IPEndPoint(lookup.Task.Result[0], port));
            }
            else
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.NoSupportedAddressFound);
            }
            BaselibHandles.Free((IntPtr)task);
            return true;
        }

        internal static void HostnameLookupCancel(Baselib_NetworkAddress_HostnameLookupHandle* task)
        {
            var lookup = BaselibHandles.Get<Lookup>((IntPtr)task);
            if (lookup == null) return;
            lookup.Cancel.Cancel();
            BaselibHandles.Free((IntPtr)task);
        }

        // ------------------------------------------------------------------ sockets

        sealed class SocketState
        {
            public Socket Socket;
            public bool Connecting;
        }

        static SocketState StateOf(Baselib_Socket_Handle handle, Baselib_ErrorState* error)
        {
            var state = BaselibHandles.Get<SocketState>(handle.handle);
            if (state == null) BaselibErrors.Set(error, Baselib_ErrorCode.InvalidArgument);
            return state;
        }

        static Baselib_Socket_Handle Wrap(Socket socket) => new Baselib_Socket_Handle { handle = BaselibHandles.New(new SocketState { Socket = socket }) };

        internal static Baselib_Socket_Handle CreateSocket(Baselib_NetworkAddress_Family family, Baselib_Socket_Protocol protocol, Baselib_ErrorState* error)
        {
            try
            {
                var socket = protocol == Baselib_Socket_Protocol.TCP
                    ? new Socket(FamilyOf(family), SocketType.Stream, ProtocolType.Tcp)
                    : new Socket(FamilyOf(family), SocketType.Dgram, ProtocolType.Udp);
                socket.Blocking = false;
                if (protocol == Baselib_Socket_Protocol.TCP) socket.NoDelay = true;
                return Wrap(socket);
            }
            catch (SocketException e)
            {
                BaselibErrors.Set(error, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
                return Baselib_Socket_Handle_Invalid;
            }
        }

        internal static void Bind(Baselib_Socket_Handle handle, Baselib_NetworkAddress* address, Baselib_NetworkAddress_AddressReuse reuse, Baselib_ErrorState* error)
        {
            var state = StateOf(handle, error);
            if (state == null) return;
            try
            {
                if (reuse == Baselib_NetworkAddress_AddressReuse.Allow)
                    state.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                state.Socket.Bind(Read(address));
            }
            catch (SocketException e)
            {
                BaselibErrors.Set(error, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
            }
        }

        internal static void Connect(Baselib_Socket_Handle handle, Baselib_NetworkAddress* address, Baselib_NetworkAddress_AddressReuse reuse, Baselib_ErrorState* error)
        {
            var state = StateOf(handle, error);
            if (state == null) return;
            try
            {
                if (reuse == Baselib_NetworkAddress_AddressReuse.Allow)
                    state.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                state.Socket.Connect(Read(address));
                Trace.Baselib($"connect {Read(address)}: connected at once");
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress or SocketError.AlreadyInProgress)
            {
                // A non-blocking connect completes later: Poll reports Connected.
                state.Connecting = true;
                Trace.Baselib($"connect {Read(address)}: in progress");
            }
            catch (SocketException e)
            {
                BaselibErrors.Set(error, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
            }
        }

        internal static void Poll(Baselib_Socket_PollFd* sockets, uint count, uint timeoutInMilliseconds, Baselib_ErrorState* error)
        {
            var deadline = Environment.TickCount64 + timeoutInMilliseconds;
            while (true)
            {
                bool any = false;
                for (uint i = 0; i < count; i++)
                {
                    var fd = &sockets[i];
                    fd->resultEvents = 0;
                    var state = BaselibHandles.Get<SocketState>(fd->handle.handle);
                    if (state == null) continue;
                    try
                    {
                        if ((fd->requestedEvents & Baselib_Socket_PollEvents.Readable) != 0 && state.Socket.Poll(0, SelectMode.SelectRead))
                            fd->resultEvents |= Baselib_Socket_PollEvents.Readable;
                        bool writable = state.Socket.Poll(0, SelectMode.SelectWrite);
                        if ((fd->requestedEvents & Baselib_Socket_PollEvents.Writable) != 0 && writable)
                            fd->resultEvents |= Baselib_Socket_PollEvents.Writable;
                        if (state.Connecting && state.Socket.Poll(0, SelectMode.SelectError))
                        {
                            // The connect failed (refused, unreachable).
                            state.Connecting = false;
                            if (fd->errorState != null) BaselibErrors.Set(fd->errorState, Baselib_ErrorCode.Disconnected);
                        }
                        else if ((fd->requestedEvents & Baselib_Socket_PollEvents.Connected) != 0 && writable &&
                                 (state.Socket.Connected || state.Connecting && (int)state.Socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)! == 0))
                        {
                            // Connected, whether the connect completed at once or later (a completed
                            // non-blocking connect is writable without error; Socket.Connected only
                            // changes with the next operation).
                            state.Connecting = false;
                            fd->resultEvents |= Baselib_Socket_PollEvents.Connected;
                        }
                    }
                    catch (SocketException e)
                    {
                        if (fd->errorState != null) BaselibErrors.Set(fd->errorState, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
                    }
                    if (Trace.BaselibOn && (fd->requestedEvents & Baselib_Socket_PollEvents.Connected) != 0)
                        Trace.Baselib($"poll {state.Socket.LocalEndPoint}: requested {fd->requestedEvents} got {fd->resultEvents} connecting {state.Connecting} connected {state.Socket.Connected}");
                    any |= fd->resultEvents != 0;
                }
                if (any || timeoutInMilliseconds == 0 || Environment.TickCount64 >= deadline) return;
                Thread.Sleep(1);
            }
        }

        internal static void GetAddress(Baselib_Socket_Handle handle, Baselib_NetworkAddress* address, Baselib_ErrorState* error)
        {
            var state = StateOf(handle, error);
            if (state == null) return;
            if (state.Socket.LocalEndPoint is IPEndPoint local) Write(address, local);
            else BaselibErrors.Set(error, Baselib_ErrorCode.InvalidState);
        }

        internal static void Listen(Baselib_Socket_Handle handle, Baselib_ErrorState* error)
        {
            var state = StateOf(handle, error);
            if (state == null) return;
            try
            {
                state.Socket.Listen();
                Trace.Baselib($"listen {state.Socket.LocalEndPoint}");
            }
            catch (SocketException e)
            {
                BaselibErrors.Set(error, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
            }
        }

        internal static Baselib_Socket_Handle Accept(Baselib_Socket_Handle handle, Baselib_ErrorState* error)
        {
            var state = StateOf(handle, error);
            if (state == null) return Baselib_Socket_Handle_Invalid;
            try
            {
                var accepted = state.Socket.Accept();
                Trace.Baselib($"accepted {accepted.RemoteEndPoint} on {state.Socket.LocalEndPoint}");
                accepted.Blocking = false;
                accepted.NoDelay = true;
                return Wrap(accepted);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
            {
                return Baselib_Socket_Handle_Invalid;
            }
            catch (SocketException e)
            {
                BaselibErrors.Set(error, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
                return Baselib_Socket_Handle_Invalid;
            }
        }

        internal static uint TcpSend(Baselib_Socket_Handle handle, IntPtr data, uint length, Baselib_ErrorState* error)
        {
            var state = StateOf(handle, error);
            if (state == null) return 0;
            int sent = state.Socket.Send(new ReadOnlySpan<byte>((void*)data, (int)length), SocketFlags.None, out var code);
            if (code == SocketError.WouldBlock) return 0;
            if (code != SocketError.Success)
            {
                BaselibErrors.Set(error, BaselibErrors.FromSocket(code), (long)code);
                return 0;
            }
            return (uint)sent;
        }

        /// <summary>Bytes received, 0 when none are waiting; a closed connection is Disconnected.</summary>
        internal static uint TcpRecv(Baselib_Socket_Handle handle, IntPtr data, uint length, Baselib_ErrorState* error)
        {
            var state = StateOf(handle, error);
            if (state == null) return 0;
            int received = state.Socket.Receive(new Span<byte>((void*)data, (int)length), SocketFlags.None, out var code);
            if (code == SocketError.WouldBlock) return 0;
            if (code != SocketError.Success)
            {
                BaselibErrors.Set(error, BaselibErrors.FromSocket(code), (long)code);
                return 0;
            }
            if (received == 0 && length > 0) BaselibErrors.Set(error, Baselib_ErrorCode.Disconnected);
            return (uint)received;
        }

        internal static uint UdpSend(Baselib_Socket_Handle handle, Baselib_Socket_Message* messages, uint count, Baselib_ErrorState* error)
        {
            var state = StateOf(handle, error);
            if (state == null) return 0;
            uint sent = 0;
            for (; sent < count; sent++)
            {
                var message = messages[sent];
                try
                {
                    state.Socket.SendTo(new ReadOnlySpan<byte>((void*)message.data, (int)message.dataLen), SocketFlags.None, Read(message.address));
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
                {
                    break;
                }
                catch (SocketException e)
                {
                    BaselibErrors.Set(error, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
                    break;
                }
            }
            return sent;
        }

        internal static uint UdpRecv(Baselib_Socket_Handle handle, Baselib_Socket_Message* messages, uint count, Baselib_ErrorState* error)
        {
            var state = StateOf(handle, error);
            if (state == null) return 0;
            uint received = 0;
            for (; received < count; received++)
            {
                if (state.Socket.Available == 0) break;
                EndPoint from = state.Socket.AddressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                try
                {
                    var span = new Span<byte>((void*)messages[received].data, (int)messages[received].dataLen);
                    int n = state.Socket.ReceiveFrom(span, SocketFlags.None, ref from);
                    messages[received].dataLen = (uint)n;
                    if (messages[received].address != null) Write(messages[received].address, (IPEndPoint)from);
                }
                catch (SocketException e) when (e.SocketErrorCode is SocketError.WouldBlock or SocketError.ConnectionReset)
                {
                    break;
                }
                catch (SocketException e)
                {
                    BaselibErrors.Set(error, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
                    break;
                }
            }
            return received;
        }

        internal static void Close(Baselib_Socket_Handle handle)
        {
            var state = BaselibHandles.Get<SocketState>(handle.handle);
            if (state == null) return;
            state.Socket.Dispose();
            BaselibHandles.Free(handle.handle);
        }

        // ------------------------------------------------------------------ registered network (emulated)

        static long s_NextBufferId;

        internal static Baselib_RegisteredNetwork_Buffer RegisterBuffer(Baselib_Memory_PageAllocation allocation, Baselib_ErrorState* error)
        {
            if (allocation.ptr == IntPtr.Zero)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidArgument);
                return default;
            }
            return new Baselib_RegisteredNetwork_Buffer { id = (IntPtr)Interlocked.Increment(ref s_NextBufferId), allocation = allocation };
        }

        internal static Baselib_RegisteredNetwork_Endpoint CreateEndpoint(Baselib_NetworkAddress* address, Baselib_RegisteredNetwork_BufferSlice slice, Baselib_ErrorState* error)
        {
            if (slice.data == IntPtr.Zero || slice.size < Baselib_RegisteredNetwork_Endpoint_MaxSize || address == null)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidBufferSize);
                return default;
            }
            if ((void*)slice.data != address) *(Baselib_NetworkAddress*)slice.data = *address;
            return new Baselib_RegisteredNetwork_Endpoint { slice = slice };
        }

        internal static void EndpointAddress(Baselib_RegisteredNetwork_Endpoint endpoint, Baselib_NetworkAddress* dst, Baselib_ErrorState* error)
        {
            if (endpoint.slice.data == IntPtr.Zero || endpoint.slice.size < sizeof(Baselib_NetworkAddress))
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidArgument);
                return;
            }
            *dst = *(Baselib_NetworkAddress*)endpoint.slice.data;
        }

        /// <summary>A UDP socket with RIO-style request and completion queues.</summary>
        internal sealed class RegisteredUdp
        {
            public Socket Socket;
            public uint SendCapacity, RecvCapacity;
            readonly Queue<Baselib_RegisteredNetwork_Request> m_PendingRecv = new Queue<Baselib_RegisteredNetwork_Request>();
            readonly Queue<Baselib_RegisteredNetwork_Request> m_PendingSend = new Queue<Baselib_RegisteredNetwork_Request>();
            readonly Queue<Baselib_RegisteredNetwork_CompletionResult> m_CompletedRecv = new Queue<Baselib_RegisteredNetwork_CompletionResult>();
            readonly Queue<Baselib_RegisteredNetwork_CompletionResult> m_CompletedSend = new Queue<Baselib_RegisteredNetwork_CompletionResult>();

            public uint ScheduleRecv(Baselib_RegisteredNetwork_Request* requests, uint count)
            {
                lock (this)
                {
                    uint accepted = 0;
                    while (accepted < count && m_PendingRecv.Count + m_CompletedRecv.Count < RecvCapacity) m_PendingRecv.Enqueue(requests[accepted++]);
                    return accepted;
                }
            }

            public uint ScheduleSend(Baselib_RegisteredNetwork_Request* requests, uint count)
            {
                lock (this)
                {
                    uint accepted = 0;
                    while (accepted < count && m_PendingSend.Count + m_CompletedSend.Count < SendCapacity) m_PendingSend.Enqueue(requests[accepted++]);
                    return accepted;
                }
            }

            /// <summary>Fills the scheduled receives with the datagrams waiting; Pending when datagrams
            /// remain but no receive is scheduled for them.</summary>
            public Baselib_RegisteredNetwork_ProcessStatus ProcessRecv(Baselib_ErrorState* error)
            {
                lock (this)
                {
                    while (m_PendingRecv.Count > 0)
                    {
                        if (Socket.Available == 0) return Baselib_RegisteredNetwork_ProcessStatus.Done;
                        var request = m_PendingRecv.Peek();
                        EndPoint from = Socket.AddressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                        int received;
                        try
                        {
                            received = Socket.ReceiveFrom(new Span<byte>((void*)request.payload.data, (int)request.payload.size), SocketFlags.None, ref from);
                        }
                        catch (SocketException e) when (e.SocketErrorCode is SocketError.WouldBlock)
                        {
                            return Baselib_RegisteredNetwork_ProcessStatus.Done;
                        }
                        catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
                        {
                            // An ICMP port unreachable or an oversized datagram: that receive fails.
                            m_PendingRecv.Dequeue();
                            m_CompletedRecv.Enqueue(new Baselib_RegisteredNetwork_CompletionResult { status = Baselib_RegisteredNetwork_CompletionStatus.Failed, requestUserdata = request.requestUserdata });
                            continue;
                        }
                        catch (SocketException e)
                        {
                            BaselibErrors.Set(error, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
                            return Baselib_RegisteredNetwork_ProcessStatus.Done;
                        }
                        m_PendingRecv.Dequeue();
                        if (request.remoteEndpoint.slice.data != IntPtr.Zero)
                            Write((Baselib_NetworkAddress*)request.remoteEndpoint.slice.data, (IPEndPoint)from);
                        m_CompletedRecv.Enqueue(new Baselib_RegisteredNetwork_CompletionResult
                        {
                            status = Baselib_RegisteredNetwork_CompletionStatus.Success,
                            bytesTransferred = (uint)received,
                            requestUserdata = request.requestUserdata,
                        });
                    }
                    return Socket.Available > 0 ? Baselib_RegisteredNetwork_ProcessStatus.Pending : Baselib_RegisteredNetwork_ProcessStatus.Done;
                }
            }

            public Baselib_RegisteredNetwork_ProcessStatus ProcessSend(Baselib_ErrorState* error)
            {
                lock (this)
                {
                    while (m_PendingSend.Count > 0)
                    {
                        var request = m_PendingSend.Peek();
                        var to = Read((Baselib_NetworkAddress*)request.remoteEndpoint.slice.data);
                        var status = Baselib_RegisteredNetwork_CompletionStatus.Success;
                        uint sent = 0;
                        try
                        {
                            sent = (uint)Socket.SendTo(new ReadOnlySpan<byte>((void*)request.payload.data, (int)request.payload.size), SocketFlags.None, to);
                        }
                        catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
                        {
                            return Baselib_RegisteredNetwork_ProcessStatus.Pending;
                        }
                        catch (Exception)
                        {
                            status = Baselib_RegisteredNetwork_CompletionStatus.Failed;
                        }
                        m_PendingSend.Dequeue();
                        m_CompletedSend.Enqueue(new Baselib_RegisteredNetwork_CompletionResult { status = status, bytesTransferred = sent, requestUserdata = request.requestUserdata });
                    }
                    return Baselib_RegisteredNetwork_ProcessStatus.Done;
                }
            }

            public Baselib_RegisteredNetwork_CompletionQueueStatus WaitForCompletedRecv(uint timeoutInMilliseconds, Baselib_ErrorState* error)
            {
                var deadline = Environment.TickCount64 + timeoutInMilliseconds;
                while (true)
                {
                    ProcessRecv(error);
                    lock (this)
                        if (m_CompletedRecv.Count > 0) return Baselib_RegisteredNetwork_CompletionQueueStatus.ResultsAvailable;
                    if (Environment.TickCount64 >= deadline) return Baselib_RegisteredNetwork_CompletionQueueStatus.NoResultsAvailable;
                    Socket.Poll(1000, SelectMode.SelectRead);
                }
            }

            public Baselib_RegisteredNetwork_CompletionQueueStatus WaitForCompletedSend(uint timeoutInMilliseconds, Baselib_ErrorState* error)
            {
                ProcessSend(error);
                lock (this)
                    return m_CompletedSend.Count > 0 ? Baselib_RegisteredNetwork_CompletionQueueStatus.ResultsAvailable : Baselib_RegisteredNetwork_CompletionQueueStatus.NoResultsAvailable;
            }

            public uint DequeueRecv(Baselib_RegisteredNetwork_CompletionResult* results, uint count)
            {
                lock (this)
                {
                    uint n = 0;
                    while (n < count && m_CompletedRecv.Count > 0) results[n++] = m_CompletedRecv.Dequeue();
                    return n;
                }
            }

            public uint DequeueSend(Baselib_RegisteredNetwork_CompletionResult* results, uint count)
            {
                lock (this)
                {
                    uint n = 0;
                    while (n < count && m_CompletedSend.Count > 0) results[n++] = m_CompletedSend.Dequeue();
                    return n;
                }
            }

            public void GetAddress(Baselib_NetworkAddress* dst, Baselib_ErrorState* error)
            {
                if (Socket.LocalEndPoint is IPEndPoint local) Write(dst, local);
                else BaselibErrors.Set(error, Baselib_ErrorCode.InvalidState);
            }
        }

        internal static Baselib_RegisteredNetwork_Socket_UDP CreateRegisteredUdp(Baselib_NetworkAddress* bindAddress, Baselib_NetworkAddress_AddressReuse reuse, uint sendQueueSize, uint recvQueueSize, Baselib_ErrorState* error)
        {
            var endpoint = Read(bindAddress);
            if (endpoint == null)
            {
                BaselibErrors.Set(error, Baselib_ErrorCode.InvalidArgument);
                return default;
            }
            var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            try
            {
                if (reuse == Baselib_NetworkAddress_AddressReuse.Allow)
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Bind(endpoint);
            }
            catch (SocketException e)
            {
                socket.Dispose();
                BaselibErrors.Set(error, BaselibErrors.FromSocket(e.SocketErrorCode), e.ErrorCode);
                return default;
            }
            var udp = new RegisteredUdp { Socket = socket, SendCapacity = sendQueueSize, RecvCapacity = recvQueueSize };
            return new Baselib_RegisteredNetwork_Socket_UDP { handle = BaselibHandles.New(udp) };
        }

        internal static RegisteredUdp Registered(Baselib_RegisteredNetwork_Socket_UDP socket) =>
            BaselibHandles.Get<RegisteredUdp>(socket.handle) ?? throw new ArgumentException("Invalid Baselib registered UDP socket");

        internal static void CloseRegisteredUdp(Baselib_RegisteredNetwork_Socket_UDP socket)
        {
            var udp = BaselibHandles.Get<RegisteredUdp>(socket.handle);
            if (udp == null) return;
            udp.Socket.Dispose();
            BaselibHandles.Free(socket.handle);
        }
    }
}
