using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor.MPE;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Messages between the channel service and its clients in one process, as the Unity
    /// 6000.0.71f1 Editor's (tests/projects/channelmessages).</summary>
    public sealed class ChannelMessagesTests
    {
        const string Channel = "unit-channel";

        static void Settle() => Thread.Sleep(300);

        [Fact]
        public void ClientsAndTheServiceExchangeTextAndBinaryMessagesOnTicksAndDispatches()
        {
            bool wasRunning = ChannelService.IsRunning();
            ChannelService.Start();
            var serverReceived = new List<string>();
            var clientReceived = new List<string>();
            ChannelClient client = null;
            try
            {
                Assert.True(ChannelService.GetPort() > 0);
                ChannelService.GetOrCreateChannel(Channel, (connection, data) => serverReceived.Add(connection + ":" + Encoding.UTF8.GetString(data)));
                client = ChannelClient.GetOrCreateClient(Channel);
                Assert.Same(client, ChannelClient.GetOrCreateClient(Channel));
                Assert.False(client.IsConnected());
                client.RegisterMessageHandler((byte[] data) => clientReceived.Add("binary:" + string.Join(",", data)));
                client.RegisterMessageHandler((string text) => clientReceived.Add("text:" + text));
                client.Start(false);
                Assert.True(client.IsConnected());
                Assert.False(client.isAutoTick);

                Assert.Equal(ChannelService.ChannelNameToId(Channel), client.clientId);
                Action<byte[]> again = _ => { };
                client.RegisterMessageHandler(again);
                Assert.Equal("Channel Client Handler already registered", Assert.Throws<Exception>(() => client.RegisterMessageHandler(again)).Message);
                client.UnregisterMessageHandler(again);

                var info = client.GetChannelClientInfo();
                Assert.True(info.connectionId > 0);
                Assert.Equal(client.clientId, info.clientId);
                Assert.Contains(info, ChannelService.GetChannelClientList());

                // A client's message leaves on its tick and reaches the handler on a dispatch.
                client.Send("hello");
                Settle();
                ChannelService.DispatchMessages();
                Assert.Empty(serverReceived);
                client.Tick();
                Settle();
                Assert.Empty(serverReceived);
                ChannelService.DispatchMessages();
                Assert.Equal(new[] { info.connectionId + ":hello" }, serverReceived);

                // The service's messages reach both kinds of handlers on the client's tick, text first.
                ChannelService.BroadcastBinary(ChannelService.ChannelNameToId(Channel), new byte[] { 0xc3, 0xa9 });
                ChannelService.Send(info.connectionId, "one");
                Settle();
                Assert.Empty(clientReceived);
                client.Tick();
                Assert.Equal(new[] { "text:é", "binary:195,169", "text:one", "binary:111,110,101" }, clientReceived);

                // Closed, it leaves the service's list and a new client takes its name.
                ChannelClient.Close(Channel);
                Assert.False(client.IsConnected());
                for (int i = 0; i < 100 && ChannelService.GetChannelClientList().Any(c => c.name == Channel); i++) Thread.Sleep(20);
                Assert.DoesNotContain(ChannelService.GetChannelClientList(), c => c.name == Channel);
                Assert.NotSame(client, ChannelClient.GetOrCreateClient(Channel));
            }
            finally
            {
                ChannelClient.Close(Channel);
                ChannelService.CloseChannel(Channel);
                if (!wasRunning) ChannelService.Stop();
            }
        }

        [Fact]
        public void TheEchoChannelAnswersEachMessageAsItWasSent()
        {
            bool wasRunning = ChannelService.IsRunning();
            ChannelService.Start();
            var echoed = new List<string>();
            try
            {
                var echo = ChannelClient.GetOrCreateClient("echo");
                echo.RegisterMessageHandler((string text) => echoed.Add("text:" + text));
                echo.Start(false);
                echo.Send("ping");
                echo.Tick();
                Settle();
                ChannelService.DispatchMessages();
                Settle();
                echo.Tick();
                Assert.Equal(new[] { "text:ping" }, echoed);
            }
            finally
            {
                ChannelClient.Close("echo");
                if (!wasRunning) ChannelService.Stop();
            }
        }

        // Stopping the service with a client connected ends its connections; the process goes on.
        [Fact]
        public void StoppingTheServiceWithAConnectedClientEndsTheConnection()
        {
            bool wasRunning = ChannelService.IsRunning();
            ChannelService.Start();
            try
            {
                var client = ChannelClient.GetOrCreateClient(Channel);
                client.Start(false);
                Assert.True(client.IsConnected());
                ChannelService.Stop();
                for (int i = 0; i < 100 && client.IsConnected(); i++) Thread.Sleep(20);
                Assert.False(client.IsConnected());
                Assert.Empty(ChannelService.GetChannelClientList());
                Thread.Sleep(200);
            }
            finally
            {
                ChannelClient.Close(Channel);
                if (wasRunning) ChannelService.Start();
            }
        }
    }
}
