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
  -p tests/projects/networkstreaming --scene Assets/Scenes/Probe.unity --frames 200000 --size 320x180 --no-watch
python3 tests/projects/networkstreaming/compare.py \
  tests/projects/networkstreaming/Reference/unity-6000.0.71f1.jsonl \
  /tmp/network-engine/records.jsonl
```

Unity observations: callbacks run on the main thread, length precedes data when a
positive Content-Length exists. Empty and unknown lengths have no length callback. CompleteContent
runs only on successful requests before the completed event. HTTP error bodies are
delivered but skip CompleteContent, as do connection failures. Script isDone remains
false even after successful CompleteContent. Active
script progress uses GetProgress, default zero. Returning false produces
ConnectionError / "Unable to write data". Explicitly disposing the handler suppresses
later data callbacks and makes the next delivery fail. Script error is an empty
string, and disposed handler state getters throw NullReferenceException.

File constructors immediately open/truncate or preserve append content. Partial
bytes are visible before completion. removeFileOnAbort deletes the whole file,
including an existing append prefix, on abort, disposal or a truncated response.
Failed transfers leave handler isDone false and skip CompleteContent.

The extension verifies that unbuffered callbacks receive distinct, exactly sized
arrays whose contents survive later callbacks. Buffered callbacks reuse the supplied
array. Header and data callback exceptions are logged and fail with "Unable to write
data"; a CompleteContent exception is logged but preserves Success. Abort from a
header or data callback prevents later delivery. The data-callback abort fires only
when all gated first-part bytes have arrived, avoiding dependence on socket reads.
Request disposal with automatic handler disposal disabled is covered too.

Disposing the handler from inside ReceiveData terminated the Unity Editor in the
exploratory run. That undefined operation is excluded from the executable oracle;
explicit disposal between callbacks is tested. The fixture makes no claims about
AssetBundles, Caching, TLS, compression, Unity releases other than 6000.0, or exact
network read boundaries. Existing core request tests cover GET, POST/forms, HTTP and
connection errors, file URLs, WWW and escaping.

Verification on 2026-10-30: both independent completed Editor recordings agree on all
458 observations, and Unbound matches all 458. The final managed host build has zero
errors. The six existing UnityWebRequestTests pass after the change. Build output and
intermediate files remain isolated from the parent checkout and native cargo target.
No stub regeneration was needed. The parent must run integrated api-check after merge.

```bash
/home/shaypercool/.cache/compat/with-heavy-job timeout 240 \
  /home/shaypercool/.dotnet/dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o /home/shaypercool/.cache/compat/delegate-network-managed \
  -p:UnboundTarget=/home/shaypercool/.cache/compat/delegate-network-build/ \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false --nologo
/home/shaypercool/.cache/compat/with-heavy-job timeout 180 \
  /home/shaypercool/.dotnet/dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj \
  -c Release -p:UnboundTarget=/home/shaypercool/.cache/compat/delegate-network-build/ \
  --filter FullyQualifiedName~UnityWebRequestTests --disable-build-servers -m:1 \
  -p:UseSharedCompilation=false /nodeReuse:false --nologo
```

Local evidence is under `/home/shaypercool/.cache/compat/`: `network-editor-5` and
`network-editor-6` contain completed reference runs, `network-engine-3` contains the
matching runtime records, and `network-build-3.log` / `network-tests-final.log` contain
final build and core test results. The logged oracle callback exceptions are deliberate.
All probe-owned loopback servers and Editor processes have stopped.
