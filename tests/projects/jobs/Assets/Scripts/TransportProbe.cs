using System.Collections;
using Unity.Collections;
using Unity.Networking.Transport;

/// <summary>
/// com.unity.transport over the loopback interface (UDP, and WebSocket over TCP, through Baselib's sockets; IPC): a
/// server and a client driver in the same process connect and exchange a message.
/// </summary>
public static class TransportProbe
{
    public static IEnumerator Run()
    {
        yield return Exchange("transport.udp", NetworkDriver.Create(), NetworkDriver.Create(), 47011);
        yield return Exchange("transport.websocket", NetworkDriver.Create(new WebSocketNetworkInterface()), NetworkDriver.Create(new WebSocketNetworkInterface()), 47012);
        yield return Exchange("transport.ipc", NetworkDriver.Create(new IPCNetworkInterface()), NetworkDriver.Create(new IPCNetworkInterface()), 47013);
    }

    static IEnumerator Exchange(string scenario, NetworkDriver server, NetworkDriver client, ushort port)
    {
        ProbeLog.Begin(scenario);
        var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port);
        ProbeLog.Value("bind", server.Bind(endpoint));
        ProbeLog.Value("listen", server.Listen());
        ProbeLog.Value("listening", server.Listening);
        ProbeLog.Value("localPort", server.GetLocalEndpoint().Port);
        var connection = client.Connect(endpoint);
        ProbeLog.Value("connectionCreated", connection.IsCreated);
        NetworkConnection accepted = default;
        bool connected = false, received = false, serverGotDisconnect = false;
        string payload = "";
        for (int frame = 0; frame < 300 && !(received && serverGotDisconnect); frame++)
        {
            server.ScheduleUpdate().Complete();
            client.ScheduleUpdate().Complete();
            if (!accepted.IsCreated)
            {
                accepted = server.Accept();
                if (accepted.IsCreated) ProbeLog.Value("acceptedAtLeastFrame", frame >= 0);
            }
            NetworkEvent.Type type;
            while ((type = client.PopEventForConnection(connection, out var stream)) != NetworkEvent.Type.Empty)
            {
                if (type == NetworkEvent.Type.Connect)
                {
                    connected = true;
                    client.BeginSend(connection, out var writer);
                    writer.WriteFixedString32("hello from client");
                    writer.WriteInt(1234);
                    client.EndSend(writer);
                }
                else if (type == NetworkEvent.Type.Data)
                {
                    payload += "|client:" + stream.ReadInt();
                    client.Disconnect(connection);
                }
            }
            if (accepted.IsCreated)
            {
                while ((type = server.PopEventForConnection(accepted, out var stream)) != NetworkEvent.Type.Empty)
                {
                    if (type == NetworkEvent.Type.Data)
                    {
                        payload += stream.ReadFixedString32().ToString() + ":" + stream.ReadInt();
                        received = true;
                        server.BeginSend(accepted, out var writer);
                        writer.WriteInt(4321);
                        server.EndSend(writer);
                    }
                    else if (type == NetworkEvent.Type.Disconnect)
                    {
                        serverGotDisconnect = true;
                    }
                }
            }
            yield return null;
        }
        ProbeLog.Value("connected", connected);
        ProbeLog.Value("accepted", accepted.IsCreated);
        ProbeLog.Value("received", received);
        ProbeLog.Value("payload", payload);
        ProbeLog.Value("serverGotDisconnect", serverGotDisconnect);
        ProbeLog.Value("remotePort", accepted.IsCreated ? server.GetRemoteEndpoint(accepted).Address.Length > 0 : false);
        client.Dispose();
        server.Dispose();
    }
}
