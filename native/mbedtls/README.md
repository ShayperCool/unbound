# Mbed TLS 3.6.7 (fetched, pinned)

[Mbed TLS](https://github.com/Mbed-TLS/mbedtls) 3.6.7 (`mbedtls-3.6.7.tar.bz2`, sha256
`a7e8bcbec0e6f761b4af24f25677626b35f762f68eef79c08677a363212d11f6`, the release's
`mbedtls-3.6.7-sha256sum.txt`), Apache-2.0 licensed (its `LICENSE`). `source.py` fetches the
release once into Unbound's dependency cache (`native/pinned_source.py`), keeping `library/`,
`include/` and `LICENSE`, unmodified; `UNBOUND_MBEDTLS_ARCHIVE` names a local copy of the archive
instead. `crates/unbound-host/build.rs` compiles it with its default configuration into the native
library beside the managed engine.

Why: the Unity TLS binding (`Unity.TLS.LowLevel.Binding`, which Unity Transport's TLS and DTLS
layers call) is a layer over Mbed TLS 3: its handshake states are Mbed TLS 3's (TLS 1.2 states,
ending at 27) and its transport callbacks return Mbed TLS's codes. `native/unbound_tls.c`
implements the binding's functions over it (tests/projects/unitytls).

To update: change the version and hash in `source.py` and here; the unitytls probe and the
managed `UnityTlsBindingTests` check the binding's answers still match.
