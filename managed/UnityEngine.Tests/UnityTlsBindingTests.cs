using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Unity.TLS.LowLevel;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Unity TLS binding over Mbed TLS (native/unbound_tls.c): the answers
    /// tests/projects/unitytls records in the Editor, over in-memory pipes.</summary>
    [Collection("Serial")]
    public sealed unsafe class UnityTlsBindingTests
    {
        const string CaPem = @"-----BEGIN CERTIFICATE-----
MIIBbTCCARKgAwIBAgIULh+DNVc/aU1z88X76aL8/Rsp3K8wCgYIKoZIzj0EAwIw
GzEZMBcGA1UEAwwQVW5ib3VuZCBQcm9iZSBDQTAgFw0yNjExMjYxNzM0MzVaGA8y
MTI2MTEwMjE3MzQzNVowGzEZMBcGA1UEAwwQVW5ib3VuZCBQcm9iZSBDQTBZMBMG
ByqGSM49AgEGCCqGSM49AwEHA0IABC9clXzGP2SFtRGQ0bC7tU2xXyMkm9ArWJ5l
affez+H4AkXph6Z4SwwO65pTNUVCkkD8fMy5IWgg8VLdNrvzWJmjMjAwMB0GA1Ud
DgQWBBSqRyHpYzfC60yKMjz08XMRBH//WzAPBgNVHRMBAf8EBTADAQH/MAoGCCqG
SM49BAMCA0kAMEYCIQD0g9zMRrkLMgOh/XnPMOZ14W/1Zea6nVicAiqVuj8HXgIh
AOsAvJhp1LnewQ3WCFyi7ImD933AwNydgB8rexl4xB9/
-----END CERTIFICATE-----";
        const string ServerPem = @"-----BEGIN CERTIFICATE-----
MIIBVjCB/KADAgECAhQJLYKAcgYxOhBiz4Tkn2HjWQchdDAKBggqhkjOPQQDAjAb
MRkwFwYDVQQDDBBVbmJvdW5kIFByb2JlIENBMCAXDTI2MTEyNjE3MzQzNVoYDzIx
MjYxMTAyMTczNDM1WjAUMRIwEAYDVQQDDAlsb2NhbGhvc3QwWTATBgcqhkjOPQIB
BggqhkjOPQMBBwNCAAT9ryvhwQLxMOi81HIbg+RLdYo/uK9+WRL6+hUTnvOA87vS
nSjQ9i77NgMHGXcrrndevfXcK2mvhYNqjN4MXe6WoyMwITAUBgNVHREEDTALggls
b2NhbGhvc3QwCQYDVR0TBAIwADAKBggqhkjOPQQDAgNJADBGAiEAx2FU2VuROU1V
nxsRcvOs3uJN+7bN9eN9kkc2oDz7slkCIQDHVyd5CPCCk2wAjPRDzKIG4TdqngUK
MZr2pXR7BFM9Cw==
-----END CERTIFICATE-----";
        const string ServerKey = @"-----BEGIN EC PRIVATE KEY-----
MHcCAQEEIPCWk9/l867Z5HhMfd68/qQ/OeZQALJnCZqXa1VSOSxJoAoGCCqGSM49
AwEHoUQDQgAE/a8r4cEC8TDovNRyG4PkS3WKP7ivflkS+voVE57zgPO70p0o0PYu
+zYDBxl3K653Xr313Ctpr4WDaozeDF3ulg==
-----END EC PRIVATE KEY-----";
        const string OtherCaPem = @"-----BEGIN CERTIFICATE-----
MIIBXDCCAQKgAwIBAgIUZSbCoMSuFHS9I3mRn6KdB2gi1Y0wCgYIKoZIzj0EAwIw
EzERMA8GA1UEAwwIT3RoZXIgQ0EwIBcNMjYxMTI2MTczNDM1WhgPMjEyNjExMDIx
NzM0MzVaMBMxETAPBgNVBAMMCE90aGVyIENBMFkwEwYHKoZIzj0CAQYIKoZIzj0D
AQcDQgAEdAeCLTkTyPf0CmNy6zlK0jCZRFemiyptZM9VdsxNFgtN14btMyt5DIy6
sIlBe4Jllu94KvxQfxcrM+jIYF7w5aMyMDAwHQYDVR0OBBYEFH+l20CfpIU96WNO
rgMiFhQPNKDNMA8GA1UdEwEB/wQFMAMBAf8wCgYIKoZIzj0EAwIDSAAwRQIhAJKj
Yw9/lfUAAH9G6yFFq+MFu8nHlfYVqEI0zigLYuz7AiBnnIeyviQS8FEZzYeAi6gB
5MDaH1S8yAr9uKkiD5uxtw==
-----END CERTIFICATE-----";

        static readonly Dictionary<long, Queue<byte[]>> s_Inbound = new();

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static int Send(IntPtr user, byte* data, UIntPtr length, uint status)
        {
            var bytes = new byte[(int)length];
            Marshal.Copy((IntPtr)data, bytes, 0, bytes.Length);
            s_Inbound[(long)user ^ 1].Enqueue(bytes);
            return bytes.Length;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static int Receive(IntPtr user, byte* data, UIntPtr length, uint status)
        {
            var queue = s_Inbound[(long)user];
            if (queue.Count == 0) return -0x6900;
            var packet = queue.Dequeue();
            int take = Math.Min(packet.Length, (int)length);
            Marshal.Copy(packet, 0, (IntPtr)data, take);
            if (take < packet.Length)
            {
                var rest = new byte[packet.Length - take];
                Array.Copy(packet, take, rest, 0, rest.Length);
                var items = queue.ToArray();
                queue.Clear();
                queue.Enqueue(rest);
                foreach (var item in items) queue.Enqueue(item);
            }
            return take;
        }

        readonly List<GCHandle> pins = new();

        Binding.unitytls_dataRef Ref(string text)
        {
            var handle = GCHandle.Alloc(Encoding.ASCII.GetBytes(text), GCHandleType.Pinned);
            pins.Add(handle);
            return new Binding.unitytls_dataRef { dataPtr = (byte*)handle.AddrOfPinnedObject(), dataLen = (UIntPtr)((byte[])handle.Target).Length };
        }

        byte* Text(string text) => Ref(text + "\0").dataPtr;

        Binding.unitytls_client_config* Config(uint protocol, long id)
        {
            var config = (Binding.unitytls_client_config*)Marshal.AllocHGlobal(sizeof(Binding.unitytls_client_config));
            Binding.unitytls_client_init_config(config);
            config->transportProtocol = protocol;
            config->dataSendCB = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte*, UIntPtr, uint, int>)&Send;
            config->dataReceiveCB = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte*, UIntPtr, uint, int>)&Receive;
            config->transportUserData = (IntPtr)id;
            config->handshakeReturnsOnStep = 0;
            s_Inbound[id] = new Queue<byte[]>();
            return config;
        }

        static void Handshake(Binding.unitytls_client* client, Binding.unitytls_client* server)
        {
            for (int i = 0; i < 100; i++)
            {
                foreach (var side in new[] { (IntPtr)client, (IntPtr)server })
                {
                    uint state = Binding.unitytls_client_get_state((Binding.unitytls_client*)side);
                    if (state == Binding.UnityTLSClientState_Init || state == Binding.UnityTLSClientState_Handshake)
                        Binding.unitytls_client_handshake((Binding.unitytls_client*)side);
                }
            }
        }

        [Fact]
        public void TheStructuresAreTheBindingsSizes()
        {
            Assert.Equal(192, sizeof(Binding.unitytls_client_config));
            Assert.Equal(40, sizeof(Binding.unitytls_client));
        }

        [Fact]
        public void AConfigurationStartsWithTheBindingsDefaults()
        {
            var config = (Binding.unitytls_client_config*)Marshal.AllocHGlobal(sizeof(Binding.unitytls_client_config));
            try
            {
                new Span<byte>(config, sizeof(Binding.unitytls_client_config)).Fill(0xCD);
                Binding.unitytls_client_init_config(config);
                Assert.Equal(1u, config->clientAuth);
                Assert.Equal((uint)Binding.UnityTLSTransportProtocol_Datagram, config->transportProtocol);
                Assert.Equal(1, config->handshakeReturnsOnStep);
                Assert.Equal(1, config->handshakeReturnsIfWouldBlock);
                Assert.Equal(2u, config->tracelevel);
                Assert.Equal(1000u, config->ssl_handshake_timeout_min);
                Assert.Equal(60000u, config->ssl_handshake_timeout_max);
                Assert.Equal(0, config->mtu);
                Assert.True(config->caPEM.dataPtr == null && config->hostname == null && config->dataSendCB == IntPtr.Zero);
            }
            finally { Marshal.FreeHGlobal((IntPtr)config); }
        }

        void Scenario(uint protocol, string ca, string host, Action<IntPtr, IntPtr> check, bool serverCertificate = true)
        {
            var serverConfig = Config(protocol, 2);
            var clientConfig = Config(protocol, 3);
            if (serverCertificate)
            {
                serverConfig->serverPEM = Ref(ServerPem);
                serverConfig->privateKeyPEM = Ref(ServerKey);
            }
            if (ca != null) clientConfig->caPEM = Ref(ca);
            clientConfig->hostname = Text(host);
            var server = Binding.unitytls_client_create(Binding.UnityTLSRole_Server, serverConfig);
            var client = Binding.unitytls_client_create(Binding.UnityTLSRole_Client, clientConfig);
            try
            {
                Binding.unitytls_client_init(server);
                Binding.unitytls_client_init(client);
                check((IntPtr)client, (IntPtr)server);
            }
            finally
            {
                Binding.unitytls_client_destroy(client);
                Binding.unitytls_client_destroy(server);
                Marshal.FreeHGlobal((IntPtr)serverConfig);
                Marshal.FreeHGlobal((IntPtr)clientConfig);
                foreach (var pin in pins) pin.Free();
                pins.Clear();
            }
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        public void ATrustedServerCompletesTheHandshakeAndEchoes(uint protocol)
        {
            Scenario(protocol, CaPem, "localhost", (c, s) =>
            {
                var client = (Binding.unitytls_client*)c;
                var server = (Binding.unitytls_client*)s;
                Handshake(client, server);
                Assert.Equal((uint)Binding.UnityTLSClientState_Messaging, Binding.unitytls_client_get_state(client));
                Assert.Equal((uint)Binding.UnityTLSClientState_Messaging, Binding.unitytls_client_get_state(server));
                Assert.Equal((uint)Binding.UNITYTLS_SSL_HANDSHAKE_OVER, Binding.unitytls_client_get_handshake_state(client));
                var message = Encoding.ASCII.GetBytes("hello server");
                fixed (byte* p = message)
                    Assert.Equal((uint)Binding.UNITYTLS_SUCCESS, Binding.unitytls_client_send_data(client, p, (UIntPtr)message.Length));
                var buffer = new byte[64];
                UIntPtr read;
                fixed (byte* p = buffer)
                {
                    Assert.Equal((uint)Binding.UNITYTLS_SUCCESS, Binding.unitytls_client_read_data(server, p, (UIntPtr)buffer.Length, &read));
                    Assert.Equal("hello server", Encoding.ASCII.GetString(buffer, 0, (int)read));
                    Assert.Equal((uint)Binding.UNITYTLS_USER_WOULD_BLOCK, Binding.unitytls_client_read_data(server, p, (UIntPtr)buffer.Length, &read));
                }
            });
        }

        [Fact]
        public void AnUntrustedOrMisnamedServerFailsWithTheVerificationFlags()
        {
            foreach (var (ca, host, flags) in new[] { (OtherCaPem, "localhost", 8ul), (CaPem, "example.com", 4ul), (null, "localhost", 8ul) })
                Scenario(1, ca, host, (c, s) =>
                {
                    var client = (Binding.unitytls_client*)c;
                    var server = (Binding.unitytls_client*)s;
                    Handshake(client, server);
                    ulong detail;
                    Assert.Equal((uint)Binding.UnityTLSClientState_Fail, Binding.unitytls_client_get_state(client));
                    Assert.Equal((uint)Binding.UNITYTLS_INTERNAL_ERROR, Binding.unitytls_client_get_errorsState(client, &detail));
                    Assert.Equal(flags, detail);
                    // The server saw no certificate verified: "not done".
                    Assert.Equal((uint)Binding.UNITYTLS_INTERNAL_ERROR, Binding.unitytls_client_get_errorsState(server, &detail));
                    Assert.Equal(0x80000000ul, detail);
                });
        }

        [Fact]
        public void AServerWithoutItsCertificateDoesNotStart()
        {
            Scenario(1, CaPem, "localhost", (c, s) =>
            {
                var server = (Binding.unitytls_client*)s;
                ulong detail;
                Assert.Equal((uint)Binding.UnityTLSClientState_Fail, Binding.unitytls_client_get_state(server));
                Assert.Equal((uint)Binding.UNITYTLS_INVALID_ARGUMENT, Binding.unitytls_client_get_errorsState(server, &detail));
                Assert.Equal((uint)Binding.UNITYTLS_USER_WRITE_FAILED, Binding.unitytls_client_send_data(server, null, UIntPtr.Zero));
            }, serverCertificate: false);
        }
    }
}
