# Unity TLS binding

What the Unity TLS binding (`Unity.TLS.LowLevel.Binding` in UnityEngine.TLSModule, internal, the
functions Unity Transport's TLS and DTLS layers call) answers. Unity 6000.0.71f1, recorded with
the macOS Editor in batch mode (two identical captures); the values depend on neither the OS nor
the graphics device.

`Assets/Scripts/UnityTlsProbe.cs` calls the binding by reflection, through small generated
methods whose pointer parameters are `IntPtr` (Mono's reflection does not pass pointers to it),
and records in Play mode into values.jsonl in `$UNITYTLS_OUT` (tests/value_probe.py); values
compare exactly. A server and a client talk over in-memory pipes (a datagram per send for DTLS);
the certificates (an EC P-256 CA, a `localhost` server certificate it signed, another CA) are in
the script.

* `sizes`, `config.default.*`: the structures' sizes and what `unitytls_client_init_config`
  leaves in a configuration filled with 0xCD.
* `<tls|dtls>.<case>.<server|client>.*`: each side's fields before and after `init` (role,
  state, whether it has a context), the init result, role, state and added cipher suites.
* `<tls|dtls>.<case>.<client|server>.steps`: every handshake call's result, state and handshake
  state (`-` before the side's first handshake call: the binding leaves the field unset until
  then), the sides taking turns, client first; `.final` and its error state (code and detail);
  `.packets`: the sends of each side.
* `.toServer.*`, `.toClient.*`: a message sent, read, and read again.

Cases: `cert` (a trusted server), `steps` (one handshake step a call), `wrongCa`, `wrongHost`,
`noCa`, `garbledCert` (a server certificate that does not parse), `clientAuthRequired` (a client
without a certificate), and `dtls.psk` (a pre-shared key without certificates).

What it shows: the binding is a layer over Mbed TLS 3 (its handshake states, TLS 1.2's, end at
27). `init` makes the context: before it the binding's queries read nothing valid (asking the
role or state before `init` crashes the Editor, so the probe does not). A failed `init` leaves the
side failed without a context, its error the parse error (a certificate) or "invalid argument" (a
server without its certificate and key, even with a pre-shared key). Every client verifies the
server (no CA: not trusted). A failed handshake reports "internal error" with the peer
certificate's verification flags (8 not trusted, 4 name mismatch), or 0x80000000 ("not done")
when none was verified. In steps, the step that finishes the handshake returns a step, the next
call finishes it. DTLS sends one record a datagram and needs no hello verification. Not recorded:
cookies (`unitytls_client_set_cookie_info`), the log callback, timeouts and retransmission, MTU,
added cipher suites, and Unity Transport itself.

Record and gate:

    python3 tests/projects/unitytls/run.py record --label a   # twice, then
    python3 tests/projects/unitytls/run.py publish a b
    python3 tests/projects/unitytls/run.py gate
