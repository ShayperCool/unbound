# channelmessages

Messages between the Editor's channel service (`UnityEditor.MPE.ChannelService`) and channel
clients (`ChannelClient`) of the same Editor, in Unity 6000.0.71f1 Play mode
(`Assets/Scripts/ChannelMessagesProbe.cs`; two byte-identical Linux batch Editor captures in
`Reference/unity-6000.0.71f1/`). Multiplayer Play Mode's main Editor and its virtual players talk
over this service.

Rows: the service started and its port; a channel opened with a handler; a client of it created,
its handlers (text, binary) registered, started without auto-tick, connected; the service's client
list and the client's info; a text and a binary message from the client (what the service's handler
got after half a second, after `DispatchMessages`, after frames; then the same after the client's
`Tick`); text and binary broadcasts and sends to the client's connection, a binary message with
bytes that are not UTF-8 and a non-ASCII text (what the client's handlers got after half a second,
after frames, after `Tick`); a client of another channel with its binary handler registered first;
an auto-ticked client of the Editor's own `echo` channel; closing the client.

What it shows: `Start` connects the client at once. A client's messages leave on its `Tick` (each
Editor update for an auto-ticked one) and reach the channel's handlers, as bytes (a text message's
UTF-8), on `DispatchMessages` or the next Editor update. The service's messages reach the client on
its `Tick`, each one both handler kinds: the text handlers first, with the bytes as UTF-8 (bytes
that are not UTF-8 become U+FFFD), then the binary handlers, whatever the order of registration and
whether it was sent as text or binary. A broadcast reaches the clients of its channel only. The
`echo` channel sends each message back as it was sent. A closed client leaves the service's list and
the next `GetOrCreateClient` of its name makes another one.

Unmeasured: clients in other processes (how a virtual player reaches the main Editor), the
`echoAll`, `event` and `status` channels, a handler that throws, a client that sends before it
connects.

```sh
python3 tests/projects/channelmessages/run.py record --label a
python3 tests/projects/channelmessages/run.py record --label b
python3 tests/projects/channelmessages/run.py publish a b
python3 tests/projects/channelmessages/run.py gate
```
