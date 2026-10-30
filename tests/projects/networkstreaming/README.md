# Progressive web request downloads

Unity 6000.0.71f1 Linux Editor oracle. A loopback server binds an available port on
127.0.0.1. It sends 32768 bytes, then waits for the probe to inspect partial state
and explicitly release the remaining 8192 bytes. No assertion depends on TCP chunk
boundaries, callback counts or frame counts. Every byte is checked for script
handlers. Records include callback thread/order, content length, progress, completion
events, rejection, abort, timeout, truncated transfers, HTTP errors and disposal.
File cases cover creation/truncation, append, empty responses, partial visibility,
removeFileOnAbort for abort/disposal/transport failure, and invalid paths. A standard
DownloadHandlerBuffer checks the shared request path.

Use a fresh output directory for each run. The runner stops its server even when
the test fails and serializes heavy work with the shared parity lock.

```bash
python3 tests/projects/networkstreaming/run-probe.py /tmp/network-editor -- \
  env DISPLAY=:0 XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)" \
  /home/shaypercool/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode \
  -projectPath "$PWD/tests/projects/networkstreaming" -executeMethod ProbeRunner.Record \
  -logFile /tmp/network-editor/editor.log
python3 tests/projects/networkstreaming/run-probe.py /tmp/network-engine -- \
  env DISPLAY=:0 XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)" \
  UNBOUND_MANAGED_DIR=/home/shaypercool/.cache/compat/delegate-network-managed \
  /mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/release/unbound run \
  -p tests/projects/networkstreaming --frames 200000 --size 320x180 --no-watch
python3 tests/projects/networkstreaming/compare.py \
  tests/projects/networkstreaming/Reference/unity-6000.0.71f1.jsonl \
  /tmp/network-engine/records.jsonl
```

Unity observations: callbacks run on the main thread, length precedes data when a
Content-Length exists, and unknown lengths have no length callback. CompleteContent
runs only on successful transfers, including HTTP error bodies, before the completed
event. Script isDone remains false even after successful CompleteContent. Active
script progress uses GetProgress, default zero. Returning false produces
ConnectionError / "Unable to write data". Explicitly disposing the handler suppresses
later data callbacks and makes the next delivery fail. Script error is an empty
string, and disposed handler state getters throw NullReferenceException.

File constructors immediately open/truncate or preserve append content. Partial
bytes are visible before completion. removeFileOnAbort deletes the whole file,
including an existing append prefix, on abort, disposal or a truncated response.
Failed transfers leave handler isDone false and skip CompleteContent.
