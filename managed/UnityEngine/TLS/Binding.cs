using System;
using System.Runtime.InteropServices;

namespace Unity.TLS.LowLevel
{
    /// <summary>
    /// The Unity TLS binding (what Unity Transport's TLS and DTLS layers call): its functions are
    /// Unbound's native library's (native/unbound_tls.c, over Mbed TLS 3.6, the library the
    /// binding is a layer over), its structures the binding's own (the native code checks their
    /// sizes). Measured: tests/projects/unitytls.
    /// </summary>
    internal static unsafe partial class Binding
    {
        const string Library = "unbound_physx";

        [DllImport(Library)] public static extern uint unitytls_client_send_data(unitytls_client* clientInstance, byte* data, UIntPtr dataLen);
        [DllImport(Library)] public static extern uint unitytls_client_read_data(unitytls_client* clientInstance, byte* buffer, UIntPtr bufferLen, UIntPtr* bytesRead);
        [DllImport(Library)] public static extern void unitytls_client_add_ciphersuite(unitytls_client* clientInstance, uint suite);
        [DllImport(Library)] public static extern uint unitytls_client_get_ciphersuite(unitytls_client* clientInstance, int ndx);
        [DllImport(Library)] public static extern int unitytls_client_get_ciphersuite_cnt(unitytls_client* clientInstance);
        [DllImport(Library)] public static extern void unitytls_client_init_config(unitytls_client_config* config);
        [DllImport(Library)] public static extern unitytls_client* unitytls_client_create(uint role, unitytls_client_config* config);
        [DllImport(Library)] public static extern void unitytls_client_destroy(unitytls_client* clientInstance);
        [DllImport(Library)] public static extern int unitytls_client_init(unitytls_client* clientInstance);
        [DllImport(Library)] public static extern uint unitytls_client_handshake(unitytls_client* clientInstance);
        [DllImport(Library)] public static extern uint unitytls_client_set_cookie_info(unitytls_client* clientInstance, byte* peerIdDataPtr, int peerIdDataLen);
        [DllImport(Library)] public static extern uint unitytls_client_get_handshake_state(unitytls_client* clientInstance);
        [DllImport(Library)] public static extern uint unitytls_client_get_errorsState(unitytls_client* clientInstance, ulong* reserved);
        [DllImport(Library)] public static extern uint unitytls_client_get_state(unitytls_client* clientInstance);
        [DllImport(Library)] public static extern uint unitytls_client_get_role(unitytls_client* clientInstance);

        public partial struct unitytls_errorstate
        {
            public uint magic;
            public uint code;
            public ulong reserved;
        }

        public partial struct unitytls_dataRef
        {
            public byte* dataPtr;
            public UIntPtr dataLen;
        }

        public partial struct unitytls_client_config
        {
            public unitytls_dataRef caPEM;
            public unitytls_dataRef serverPEM;
            public unitytls_dataRef privateKeyPEM;
            public uint clientAuth;
            public uint transportProtocol;
            public unitytls_dataRef psk;
            public unitytls_dataRef pskIdentity;
            public IntPtr onDataCB;
            public IntPtr dataSendCB;
            public IntPtr dataReceiveCB;
            public IntPtr dataReceiveTimeoutCB;
            public IntPtr transportUserData;
            public IntPtr applicationUserData;
            public int handshakeReturnsOnStep;
            public int handshakeReturnsIfWouldBlock;
            public uint ssl_read_timeout_ms;
            public byte* hostname;
            public uint tracelevel;
            public IntPtr logCallback;
            public uint ssl_handshake_timeout_min;
            public uint ssl_handshake_timeout_max;
            public ushort mtu;
        }

        public partial struct unitytls_client
        {
            public uint role;
            public uint state;
            public uint handshakeState;
            public IntPtr ctx;
            public unitytls_client_config* config;
            public IntPtr internalCtx;
        }
    }
}
