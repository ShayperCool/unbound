# Native TrailRenderer automatic destruction, Unity 6000.0.71f1

The installed Linux Editor queues automatic destruction from the trail's render
culling callback, after removing expired points. It uses the native delayed-call
queue. Its request is eligible at fixed/dynamic delayed-callback updates, and is
ineligible at the end-of-frame destruction update. This establishes a request
rule consistent with both rendered controlled trails and the old line canonical.
It does not establish runtime acceptance of an Unbound implementation.

This is bounded offline research from base
`870e4dfb5a5be567e96116dd6fbff14c2104c1fb`. No production source, shared runtime,
probe, canonical, comparator or tolerance was changed. No Editor, engine, build,
debugger attachment, network research or subagent was started.

## Binary binding and reproduction

The real files inspected read-only were:

- `/home/shaypercool/Unity/Hub/Editor/6000.0.71f1/Editor/Unity`, SHA-256
  `262ee370258db6a9610701e7a82d576a52d3a05c0713485230ea45734c53abf3`.
- Its adjacent `Unity_s.debug`, SHA-256
  `65071886ec5fe7d1836e53cad6ff16864ce5d19d51b6a4b4f4137d188e37fa30`.

Both ELF build IDs are `9a4f6559dbeb9b8aa47d7c970b8858ddda4f717c`.
The executable's `.gnu_debuglink` CRC is `eec2296b`, equal to the CRC computed
from that debug file. These checks reproduced the existing particle-interval
static manifest once. The two rendered capture receipts name this same installed
executable path. This checks the present bytes against that prior static proof;
it does not hash a preserved capture-time executable copy.

Addresses below are ELF virtual addresses before the runtime load slide. Obtain
names and sizes with `nm -n -S -C Unity_s.debug`. Read instructions from the actual
executable, for example:

```sh
objdump -d -Mintel --start-address=0x41ed130 --stop-address=0x41ed299 Unity
objdump -d -Mintel --start-address=0x41eb590 --stop-address=0x41ebf24 Unity
objdump -d -Mintel --start-address=0x1dea770 --stop-address=0x1dea7a4 Unity
objdump -d -Mintel --start-address=0x1de5e60 --stop-address=0x1de6152 Unity
objdump -d -Mintel --start-address=0x1de64f0 --stop-address=0x1de6941 Unity
```

Wrap CPU inspection payloads once with the installed `with-heavy-job --kind tool`.
The cache directory contains the exact commands, original instruction output,
symbol-annotated copies, targeted call/reference searches, constants, relocated
vtable binding, input hashes and binary manifest:

`/mnt/data/unbound-cache-20261031/unbound-validation/runs/trail-native-request-phase-20261101-t3-a4644cb4/`

Symbol annotation uses the matching debug symbol address ranges. Some names
printed directly by objdump against the stripped executable are unrelated nearest
dynamic symbols; the original bytes and addresses remain in `*.raw.asm`.
The supplied particle-trail `staticproof/` directory was unavailable. The supplied
particle-interval `HANDOFF.txt` and its existing `static-proof/manifest.json`
provided the working executable/debug binding instead.

## Request chain, from the actual instructions

`TrailRenderer::InitializeClass`, `0x41e9b50`, registers the bounds dispatch job at
`0x41e9c02`, renderer type 5. At `0x41e9c17` it loads
`TrailRenderer::RendererCullingOutputReady`, `0x41eb590`, and registers it at
`0x41e9c22` in `GlobalCallbacks + 0x2edd0`.
`DispatchGeometryJobs`, `0x1d09a70`, indexes callbacks at
`0x2b158 + rendererType * 0xc18`. For type 5 this is exactly `0x2edd0`; its
`0x1d09b5f` indirect call invokes the registered ordinary callback.

Two direct `RemoveOldPoints` callers were found in the executable:

| Caller | Instructions | Autodestruction argument |
| --- | --- | --- |
| `CalculateWorldMatrixAndBoundsJob`, `0x41ea470` | `0x41eaadd xor esi,esi`; `0x41eaadf call 0x41ed130` | 0 |
| `RendererCullingOutputReady`, `0x41eb590` | `0x41ebb66 mov esi,1`; `0x41ebb6b call 0x41ed130` | 1 |

The culling caller converts the same rational clock fields used by the public
`Time.time` getter to float before the call. `Time_Get_Custom_PropTime`,
`0x18be1d0`, reads fields `0x120/0x128/0x12c`, also read at
`0x41ebb15..0x41ebb5a`. The point count check is **after** removal at
`0x41ebb70..0x41ebb7a`; an empty renderer still reaches the request decision.

`TrailRenderer::RemoveOldPoints`, `0x41ed130`, removes old ring-buffer entries.
When entries remain it sets byte `this + 0x215` at `0x41ed248`. Calling this the
"previously nonempty" latch is an instruction-derived interpretation. When empty,
the request branch requires all of:

1. `AllowAutodestruct == 1`, `0x41ed25b..0x41ed25e`.
2. `autodestruct != 0`, byte `this + 0x25c`, `0x41ed260..0x41ed267`.
3. The nonempty latch is set, `0x41ed269..0x41ed270`.
4. `IsWorldPlaying()` returns true, `0x41ed272..0x41ed279`.

The public native property getter/setter independently bind `autodestruct` to
`this + 0x25c`, at `0x1893dcc` and `0x1893e13`.
The permitted request path is:

```text
0x41ed27b  mov rdi,[rbx+0x60]       ; owning GameObject
0x41ed27f  movss xmm0,[0x6cb120]    ; bytes 00 00 c8 c2, float -100.0
0x41ed28d  jmp DestroyObjectDelayed ; 0x1dea770
```

`DestroyObjectDelayed` captures the object's instance ID, loads
`DelayedDestroyCallback` at `0x1dea780`, supplies repeat interval zero and mode
`0x43` at `0x1dea794`, then calls `CallDelayed` at `0x1dea79d`.
`CallDelayed`, `0x1de5e60`, inserts an allocated callback node into its red-black
tree at `0x1de6139`. It does not invoke destruction during insertion.

This chain establishes a render-culling request when the expiry branch is taken.
It requires no later automatic decision to explain the following-frame callbacks.
The expiry render's internal program counter was not recorded in the public
probe, so correlating this branch with that render is static inference grounded
in the exact installed binary and the observed point-count transition.

## Queue eligibility and phase

For mode `0x43`, `CallDelayed` reads the rational clock at offsets
`0x50/0x58/0x5c`, then the clock at `0xb8/0xc0/0xc4`, selects their minimum at
`0x1de5f39`, and adds the supplied float delay after conversion to double at
`0x1de6028`. The first clock is independently bound to public `fixedTime` by
`Time_Get_Custom_PropFixedTime`, `0x18be430`. The second clock's field identity is
recorded without assigning it an unverified private C++ member name.
The literal -100 comes from Native instructions, not a fitted scheduling quantum.

The queue stores mode at node `+0x54` and insertion-update generation at `+0x58`,
`0x1de611d..0x1de6128`. `DelayedCallManager::Update`, `0x1de64f0`:

- increments the manager generation at `0x1de657a`;
- checks due time at `0x1de65f0..0x1de65fb`;
- requires `node.mode & updateMask != 0` at `0x1de6617..0x1de661e`;
- skips nodes inserted during this same update at `0x1de6620..0x1de662a`;
- removes a nonrepeating callback before invoking it at `0x1de65d3`.

The virtual call binding is verified, including PIE relocation.
`ProduceHelper<DelayedCallManager, false>::Produce` stores vptr `0x587dd60` at
`0x1de6b06..0x1de6b0d`. `.rela.dyn` binds slot `0x587dec8` to `0x1de64f0`.
Thus calls through vptr offset `0x168` below reach this exact update function.
Unrelocated vtable bytes in the executable are zero; raw file bytes alone do not
resolve these virtual calls.

| Native callback | Call site | Mask | Matches automatic mode `0x43` |
| --- | --- | --- | --- |
| `EarlyUpdate.ScriptRunDelayedStartupFrame` | `0x20dc168..0x20dc16d` | `0x04` | No |
| `FixedUpdate.ScriptRunDelayedFixedFrameRate` | `0x20dc2d1..0x20dc2d6` | `0x01` | Yes |
| `Update.ScriptRunDelayedDynamicFrameRate` | `0x20dc438..0x20dc43d` | `0x02` | Yes |
| `PostLateUpdate.ScriptRunDelayedDynamicFrameRate` | `0x20dc4e8..0x20dc4ed` | `0x02` | Yes |
| `PlayerSendFrameComplete` | `0x20da443..0x20da448` | `0x20` | No |

The ordering basis is also Native code. `InitPlayerLoopCallbacks` stores the
post-late dynamic callback at table `+0x6b0`, `0x20da8c3..0x20da8ca`, and
UpdateAllRenderers at `+0x708`, `0x20da7c7..0x20da7d7`.
`UpdateDefaultPlayerLoop`, `0x2097856..0x20978b1`, copies post-late callbacks
starting at `+0x6a0` in increasing eight-byte table order into 0x60-byte loop
records. The dynamic queue update therefore precedes UpdateAllRenderers.
Its fixed-group construction uses `+0x558` upward at `0x20975f5..0x2097658`,
before the Update-group records constructed at `0x20976f2..0x20977a7`.
`ExecutePlayerLoop` advances records and invokes their native callback pointers
at `0x209820a..0x209822f`. This is not an inference from managed type order.

An ordinary late render request therefore survives the remaining EOF observations.
The next fixed delayed-callback update can consume it before the next Update.
Without a fixed step, a subsequent matching dynamic update can consume it instead.
An unconditional "destroy before next Update" rule would exceed this evidence.
The controlled public capture does not label the consuming internal queue node.

`DelayedDestroyCallback`, `0x1dea760`, calls `DestroyObjectHighLevel(object,false)`
at `0x1dea763`. That function checks `CanDestroyObject` and calls
`DestroyObjectHighLevel_Internal` at `0x2079409`. This is the destruction action
of an existing request, not a new trail expiry decision.

## Existing public evidence and retained blockers

Both complete 3,557-row graphical captures were read and match the canonical git
object at `acad505f310db27e771cfd8f8c63e6e7652688ff` byte for byte, SHA-256
`399c02c3c2a5e8e5153fd42c4e10227f7e5f0b0623a764c2b25fc921395b818b`.
The short trail changes from count 2 to 0 inside Camera.Render at step 5/frame 6,
time `0.11999996`, remains alive through final EOF, and receives callbacks at
frame 7/time `0.139999956` before Update step 6. The long trail repeats this at
step 8/frame 9/time `0.179999933`, then frame 10/time `0.199999928` before step 9.
The stale callback phase string does not identify an internal node.

The old line canonical still has count 2 at f11 mid, count 0 and empty mesh at
f11 end, then false alive at f12 mid/end. The Native queue masks explain that
interval as well. The 859-row camera-free pair remains rejected. It was read
solely to preserve its identity and rejection, and was not used as an oracle.

The inherited complete union is unchanged: 1,854 keys, 1,276 pass, 575 fail,
2 missing, 1 extra. Of 578 failures, 576 are retained and two introduced keys
remain blocking, `trail.f11.stop.autodestruct.end.alive` and `.mesh`.
All 17 baseline-only keys, 15 frameCount residuals, four image failures and all
raw values/tolerances remain in the linked original full report. No offline
classification in this research resolves a runtime value.

## Smallest candidate for Root to adjudicate

The exact source target is `managed/UnityEngine/Graphics/LineRenderers.cs` on
delivered head `acad505f310db27e771cfd8f8c63e6e7652688ff`, specifically
`TrailRenderer.Age` at line 890, `UpdateTrail` at 897, `CollectDraws` at 1076 and
the private `TrailSystem` at 1132. No shared-core edit is proposed or made.

1. Keep movement/bounds ageing unable to request destruction. Replace the
   stationary unconditional ageing/request in `UpdateTrail` with ageing and the
   automatic decision during the trail's actual render collection, before its
   empty-strip return. API BakeMesh must not become an automatic request site.
   Update the nonempty latch when ageing sees points, as Native does.
2. Replace that automatic `Destroy(m_GameObject)` action with insertion into a
   trail-owned target queue. Do not mark the target globally pending or disable
   it while inserting. This is the request, so draining must not re-decide expiry
   or require the TrailRenderer to remain enabled.
3. Register that private queue's drain at the three matching fixed/dynamic nodes
   above, using existing PlayerLoop registration. A drain processes a snapshot
   once and calls the existing immediate destruction action for surviving
   targets. New requests produced while draining stay queued for a later
   matching update. No frame number, frame offset or guessed positive delay is
   needed. Keep all shared destruction and EOF callbacks unchanged.

This is a source-backed candidate description, not a tested patch. The actual
render-collection hook and relevant release-profile policy need Root's source
review alongside the separately owned geometry changes. Existing `Register`
appends after the coroutine action at a shared node; exact ordering against other
due callbacks needs review before a general parity claim. No shared registration
change is authorized by this report. In particular, changing
only `Destroy` to a queue while leaving unconditional pre-render ageing would
still contradict the controlled before.Camera.Render count. Moving the decision
before Age would also miss the Update where Native is already dead.

Root's next action is to adjudicate this isolated trail request/queue hunk, then
include it in the already planned combined implementation/build and matching
controls. No third Native pair or global destruction reorder is justified here.
