# Compiler cache measurements, 2026-11-02

Four synthetic sessions, two existing build slots, six Cargo jobs per session.
No AI sessions were launched, resumed or changed. kache 2.0.0 was installed
locally with its matching engine binary; global Cargo/shell settings and the
existing sccache server were left alone. New builds use `tools/build.py`.

## What the history showed

The read-only audit covered 351 project threads and 19,002 shell/tool items
between 2026-11-01 00:00 UTC and 2026-11-02 16:46:56 UTC. Claude contributed
10,791 items and Codex 8,211. Keyword classification identified 9,694
read/search calls; scripts containing build text can overlap that classification.
Tool-call durations include background launch/wait envelopes and cannot be
added to infer time on the critical path. Transcripts were not committed.

The installed launcher's actual job records give a clearer build measurement:

| Window | Builds | Sum of admission waits | Sum of run times |
| --- | ---: | ---: | ---: |
| Since Nov 1 00:00 | 312 | 19,615.0 s | 17,899.0 s |
| Since Nov 1 12:00 | 163 | 3,023.7 s | 8,316.3 s |
| Nov 2 before this task | 50 | 0.0 s | 2,366.0 s |

These are overlapping per-job totals, not elapsed project time. Earlier
serialization had already been addressed. Raising concurrency again would not
fix today's measured build cost, so the installed limits were retained.
The remaining opportunity was reuse between private worktrees/targets rather
than sharing Cargo intermediates. Existing docs record an unsafe shared-build-dir
pilot; this change never copies targets or shares `OUT_DIR`.

## Synthetic results

Rust 1.99.0 `b940084d7`, GCC 15.2, cc-rs 1.9.0, sccache 0.19.0, kache 2.0.0,
Linux x86_64, SSD-backed XFS. The fixture compiler outside the repository was
also checked and is the same Rust 1.99.0. No dependency downloads were timed.
Each session compiles a Rust library with 300 generated functions, an executable,
a build script and three C++ units with 32 template instantiations each.
All cases use fresh sources, targets and intermediates; only cache contents
persist. Times include build-pool admission and completing the whole batch.

| Batch, seconds | No cache | Existing sccache style | kache |
| --- | ---: | ---: | ---: |
| Empty cache, first batch | 5.940 | 5.168 | 4.939 |
| Warm repeat 1 | 5.994 | 3.765 | 2.174 |
| Warm repeat 2, reversed order | 5.522 | 4.003 | 2.709 |
| Warm repeat 3 | 5.807 | 4.453 | 2.737 |
| **Warm median** | **5.807** | **4.003** | **2.709** |
| Rust edit | 5.902 | 5.324 | 3.380 |
| Header edit, original mtime | 5.562 | 4.977 | 4.844 |
| Four different header values | 5.575 | 6.671 | 7.235 |
| Behavior-changing compiler flags | 6.029 | 5.719 | 5.382 |
| Expected compilation errors | 5.092 | 2.783 | 2.922 |

Warm median improved **32.3% versus sccache**, or 1.48 times the throughput;
versus no cache it improved 53.3%, or 2.14 times the throughput.
Cold/edited/error batches each have one observation and are not statistically
conclusive. Four divergent, uncached header edits were **8.5% slower than
sccache** and 29.8% slower than no cache. This is a reuse optimization, not an
improvement for every workload. No full-engine speedup is claimed.

The baseline uses the installed sccache binary in a private server/store.
It is not an exhaustive tuning comparison: the daemon latches its first
session's `SCCACHE_BASEDIRS`, whereas kache uses each invocation's base directory.
In warm repeat 1 sccache recorded 12 additional C++ hits and no additional Rust
hits. kache recorded **32 hits and 4 misses per warm batch**, with 24,345,044
bytes restored through CoW clones and zero copied bytes. The remaining four
misses include the per-checkout build script embedding `CARGO_MANIFEST_DIR`.
No artifact was obtained by copying a donor target or Cargo freshness state.

All **108 build jobs** behaved as expected: **96 successful binaries** returned
the expected values and **12 negative controls** emitted the expected compiler
error. Controls cover Rust edits, header edits with preserved timestamps,
different simultaneous session values, Rust/C++ flag changes, current embedded
manifest paths, private dep-info/OUT_DIRs and required script side effects outside
`OUT_DIR`. Seven build-environment tests and four existing benchmark process-control
tests also pass, including concurrent ownership claims and cross-checkout rejection.

## Repository smoke check

The actual `unbound-compat` crate was tested twice using the new launcher and
two fresh private targets. The first run took **8.730 s**, the second **0.916 s**.
All **7 tests passed in both runs**. This checks the launcher against the real
workspace; it is not a full-engine benchmark or a before/after sccache comparison.

## Artifacts and reproduction

Full commands, per-job logs, CPU/wall times, admission records, output SHA-256s,
cache statistics and measured source snapshots are outside Git under:

```text
~/.cache/unbound/validation/runs/build-cache-20261102/
  session-audit.json
  job-timing-audit.json
  measured-parallel/results.json
  measured-parallel/metadata.json
  measured-parallel/summary.json
  measured-build-wrapper.py
  measured-benchmark.py
  actual-crate.json
  actual-a.log
  actual-b.log
```

Preliminary interrupted runs in that directory are not used for these results.
The measured benchmark source SHA-256 is
`60afe1de8659cd6eca17057875a338fb5d2986b63252a49c6fa47b6b71f712b8`.
The measured wrapper snapshot hash is recorded in `metadata.json`; subsequent
changes only added the CLI target ownership guard. Its environment selection is
unchanged. Run artifacts are not required for reproduction; see
[README.md](README.md#reproducing-compiler-cache-measurements).

The machine's new `~/.cache/unbound/builds` is a link to the SSD path
`/mnt/data/unbound-cache-20261031/builds`. Existing targets were not moved.
Full engine builds, managed compilation, Unity Editors, GPU load and model
latency were not benchmarked. Compiler-cache hits do not replace matching-runtime
validation or the complete artifact manifest used for parity acceptance.
