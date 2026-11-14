# Audio scope and follow-ups

Q373, Q390, Q416 and Q449 measure Unity 6000.0.71f1 on Linux. Published PCM references have two independent GPU Editor captures with exact metadata and raw sample agreement. Unless a row says otherwise, captures use a private eight-channel PulseAudio device. Scope is limited to the listed release, rates, layouts, startup histories and graph edits.

| Profile | Measured scope | Engine result at the existing bounds |
|---|---|---|
| [Live surround](TRANSITIONS.md) | 45 controls per rate, 48000/44100 Hz, 1024 frames, Quad/5.0/5.1/7.1 and Stereo control. Parameters, enabled states and zone presets. | All 16,712,055 values per rate pass; strict device differences 494/841. Reset diagnostics remain separate. |
| [Wet and linked paths](WET.md) | 238 wet and 18 placement controls per rate in those surround layouts. Six wet kinds, Send/Receive, Normalize and reverb startup. | All 94,505,124 values per rate pass; strict device differences 224/200 in wet and 29/33 in wet-edges. |
| [Rate/buffer pairs](SETTINGS.md) | 168 controls: 32000/256 and 48000/512 across five layouts; 96000/2048 in Stereo/Quad. | All 54,134,190 values pass; strict device differences 3921. |
| [Wide settings](WIDE.md) | 2940 controls across four rates, eleven requested periods and five layouts, with two entire pairs excluded. | All 964,835,412 values pass; strict device differences 85,136. Full 3080-control diagnostics preserve excluded failures. |
| [Prologic fallback](PROLOGIC.md) | 42 paired mono/stereo-input controls per rate at 48000/44100 Hz, 1024 frames. Six-input availability is separate in [ENCODING.md](ENCODING.md). | All 5,505,368 values per rate pass; strict device differences 236/250. |
| [Live zones](ZONES.md) | 77 controls per rate, Stereo/Quad/5.0/5.1/7.1. Movement, radii, User edits, overlap and listener components. | All 24,838,909 values per rate pass; strict device differences 10,836/11,006. |
| [Mono effects](MONO.md) | 64 controls per rate. Chorus taps, reverb folding, wet boundaries and linked processing, with Stereo-output mono-clip controls. | All 4,456,925 values per rate pass; strict device differences 329/401. |
| [Listener scripts](LISTENER_SCRIPTS.md) | 66 controls per rate. Wide moving zones, static Mono zones, script gains and routed/unrouted bypass. | All 17,826,478 values per rate pass; strict device differences 2528/2902. |
| [Sample scheduling](SCHEDULE.md), Q416 item 1 | 21 controls per rate. Fractional absolute/relative starts, initial and later scheduled edits, double Play, end edits, ordinary and post-reset layouts. | All 4,063,955 values per rate are bit-identical. Whole-frame conversion and pending-start publication use Compat. |
| [Listener components/scripts](LISTENER_CHAINS.md), Q416 item 3 | 36 controls per rate, Stereo/Quad, all six filters before/after and between scripts, bypass controls. Ten separate disabled/switched/simultaneous-listener diagnostics. | All 6,816,040 values per rate pass; strict device differences 174/53. The separate static diagnostic agrees strictly on 1,966,172 values per rate. No general simultaneous-listener ordering is promised. |
| [Live Mono and topology](MONO_LIVE.md), Q416 item 4 | 74 independently repeated Echo/Chorus and live-zone controls per rate in Mono/Stereo/Quad. The complete 135-control and six-case simultaneous-listener profiles remain diagnostics. | All 10,093,276 values per rate pass; strict device differences 1557/1609. Listener additions, retained removals, script activation and owner recreation use Compat; all six built-in filters reject duplicates. Full-profile residuals remain below. |
| [Six-input voice and other devices](VOICE_INPUT.md), Q416 item 5 and Q449 | 36 non-streamed 2D Stereo/Prologic source-processing controls per rate on each private 2/4/6/8-channel PulseAudio device, plus four encoding controls per device. | All 11,665,952 source-profile values and 1,310,770 encoding values per device/rate pass strictly. Unsupported six-input contexts fail clearly on the measured release. |

Every Master float in admitted PCM profiles passes ATOL `2e-5` / RTOL `2e-4`. Device differences fit the existing one-step PCM gate. Source callback floats use the same strict float bounds. Reports preserve every discrepancy, with no new allowance, alignment correction or sample limit. New profiles preserve all earlier canonical references and authored inputs.

Q416 implementation landings run all declared audiopitch recipes and fourteen other audio probes through the normal gate, along with managed and Rust tests. Documentation-only landings retain their normal exemption and do not claim a fresh PCM replay. The original Pitch/layout/device/surround recipe and every admitted profile remain in the sheriff suite. Diagnostic profiles return their actual failures and are not PCM admissions.

The complete 135-control topology diagnostic now retains 29,638/31,048 gate failures, including the unchanged 10,604/11,040 Master values. The earlier 29,650/31,060 reports remain visible; twelve duplicate-add Error callback differences per rate resolve in Q449 without changing PCM. Mono Echo and source-composition failures remain below. The separate six-case live simultaneous-listener diagnostic retains 123,337/123,586 failures. Neither diagnostic is included in the admitted 74-control PCM profile.

## Q449 follow-up closure

The follow-up measurements use Unity 6000.0.71f1 on Linux and the NVIDIA driver.
The existing float bounds and one-step device gate remain unchanged.

| Item | Result |
|---|---|
| R1 reset timing | Fresh unchanged 62-control immediate-reset pairs compare 24,543,998 values per rate. The 48000 Hz pair agrees exactly; the 44100 Hz pair differs on 665,500 values, with 248,639 Master and 416,861 device samples. No fixed post-reset offset or new reset PCM admission follows from an unstable repeat. Earlier scheduled counterexamples remain open. |
| R2 excluded settings | A fresh whole 14-control 32000/340 Surround cell repeats exactly on 4,617,323 values. A floating-point history endpoint is consistent with the earlier isolated Flange spike, but does not reconstruct its nonzero amplitude or explain the other histories. Both whole setting pairs remain excluded. |
| R4a/b/d startup, composition and listeners | Fresh complete Native repeats preserve all 19,399,899 mono-live and 917,566 simultaneous-listener values per rate. The startup transition still needs construction and parameter publication history. Sparse source compositions retain 93/116 Master failures; the six-case live-listener diagnostic retains 123,337/123,586 gate failures. No new PCM context is admitted. |
| R4c duplicate diagnostics | Closed for the measured rejection route. All 1345 componentlogs values pass exactly. All 270 mono-live PCM files per rate remain byte-identical, while twelve duplicate-add metadata differences per rate resolve. |
| R5a additional devices | The full 36-control source profile now passes on private 2-, 4- and 6-channel devices at both rates, adding 69,995,712 strict engine values. Each new canonical preserves its actual capability getter and shares verified Native PCM with the earlier eight-channel reference. Spatial/streamed six-input voices and other input widths remain outside this qualification. |

The R5a landing runs all 21 declared audiopitch commands, including the original
18 unchanged recipes. R4c also passes all 5,451 managed tests, API checks and its
selected component-factory probes. Documentation-only follow-ups use the normal
landing exemption and do not claim another PCM replay. No new release selector,
cache format, ABI change or per-OS arithmetic instance is introduced by Q449.

The queue below retains the unresolved cases. See [RECOVERY.md](RECOVERY.md),
[WIDE.md](WIDE.md), [MONO_LIVE.md](MONO_LIVE.md),
[componentlogs](../componentlogs/README.md) and [VOICE_INPUT.md](VOICE_INPUT.md)
for controls, references and reproduction.

## Open queue work

- **Q416-R1, earlier Q373-R1/Q390-R1. Reset publication and real-device timing.** Q416's 19-control recovery pairs agree exactly on 8,160,617 values per rate; the engine gate passes with 182/237 strict device differences and no Master failures. Q449 repeats all 62 immediate controls: 24,543,998 values agree at 48000 Hz, while 665,500 differ at 44100 Hz. Earlier immediate and scheduled counterexamples remain unresolved. Q416's 21-control scheduling correction establishes whole-frame start conversion and initial/later edits, not a universal post-reset startup offset. See [RECOVERY.md](RECOVERY.md) and [SCHEDULE.md](SCHEDULE.md).
- **Q416-R2, earlier Q390-R2. Excluded settings histories.** Both whole excluded pairs, 32000/340 and 48000/256, were repeated across all 140 controls. Original histories retain four exact differences out of 46,024,812 values, including a large Flange Master spike. Per-case Reset retains 15,872 out of 46,024,813, including seven Master floats. The two pairs remain excluded from additional Normalize/static-wet admission. Q449 repeats the complete 14-control 32000/340 Surround cell exactly on 4,617,323 values; its history endpoint observation does not explain the other failures or admit either pair. No disagreeing repeat is published as a canonical correction. See [WIDE.md](WIDE.md).
- **Q416-R4a. Initial Mono Echo history.** Isolated Echo and Reverb/Echo composition retain Master failures. A single Reverb control passes; these results do not establish a general reverb-folding defect. Initialization and parameter publication need controls that distinguish graph-construction histories. No effect-name-dependent correction is admitted.
- **Q416-R4b. Source composition precision.** Sparse Stereo/Quad LowPass/Chorus and Reverb/Echo Master differences remain outside the float bounds. These remain failures rather than device-only residuals.
- **Q416-R4d. Simultaneous-listener live histories.** The separate six-case live simultaneous-listener diagnostic has 123,337/123,586 failures. Static multiple-listener controls passing does not certify arbitrary live graph histories. Unity documents one active listener; no additional global order is imposed. See [MONO_LIVE.md](MONO_LIVE.md).
- **Q416-R5a. Additional input and device contexts.** Six-input spatial sources, streamed clips and active output layouts outside Stereo/Prologic remain unsupported on the measured release. Starts fail clearly; live context changes stop only six-input voices and report once on the control thread. Other input widths, arbitrary graph histories, physical-device PCM, other backends and releases remain unmeasured. Four private PulseAudio device widths all retain the ordinary Stereo response in Prologic; those captures provide no encoded-output reference for other drivers. See [VOICE_INPUT.md](VOICE_INPUT.md) and [ENCODING.md](ENCODING.md).
- **Additional contexts.** Mixer-slot insertion, identity, retargeting, placement and bypass edits; other effect combinations and wet kinds; tail-only/same-frame zone edits; surround layouts outside the measured topology controls; other rates, periods, releases and drivers remain unmeasured. Prior paths on other releases remain explicitly unqualified. Newly admitted Normalize/static-wet settings continue to reject unmeasured contexts.

Fresh independent Q416 Native repeats, diagnostic PCM and complete discrepancy reports are retained outside Git, with older Native runs losslessly archived. Redundant completed engine gate PCM caches were removed at the user's request after retaining their complete reports and logs. Earlier Q373 counterexample captures remain available. Q390 raw captures were removed by earlier cache cleanup; historical counts remain documented and are not reconstructed from the new sessions.
