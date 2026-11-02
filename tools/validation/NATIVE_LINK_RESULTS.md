# Native link reuse, 2026-11-02

The host build script previously reused content-keyed SDK archives but compiled
both native shims and linked `libunbound_physx.so` on every invocation, including
managed-only changes. It now leaves the native library untouched when a private
content-hash receipt and the library's actual bytes match. Managed compilation
is not skipped. No native source, ABI, compatibility profile or compiler flags
were changed. Claude and other model sessions were not started.

## Real measurements

Linux x86_64, Rust 1.99.0, GCC 15.2, cc-rs 1.9.0, kache 2.0.0, SSD-backed XFS.
The real PhysX/NvCloth archives were prepared from repository sources in a private
directory. Their preparation took 72.761 s and 3.663 s respectively. With those
archives warm, the original path spent 0.628 s compiling shims and 0.372 s linking.
That diagnostic run included kache and took 1.106 s in total native work.

The final comparison used **four independent sessions, two build slots, six
compiler jobs per session**, and five warm repetitions. Each session had private
archives, intermediates, receipt and output. SDK archive preparation and compiler
cache fill happened before timing. Both modes used kache. The baseline disabled
only the new receipt using `UNBOUND_NATIVE_CACHE=0`; thus this measures additional
gain after compiler caching. The order reversed on alternate repeats.

| Four-session batch, seconds | Always compile/link | Verified reuse |
| --- | ---: | ---: |
| Repeat 1 | 1.704 | 1.343 |
| Repeat 2, reversed | 1.987 | 1.627 |
| Repeat 3 | 2.159 | 1.371 |
| Repeat 4, reversed | 1.701 | 1.297 |
| Repeat 5 | 1.660 | 1.304 |
| **Median** | **1.704** | **1.343** |

The batch completed **21.2% faster**, saving 0.361 s. Median native work per
session went from **0.697 s to 0.506 s**, a **27.4% reduction**. Verification
itself cost a median 0.366 s. Reuse skipped shim compilation and native linking
entirely. Batch time includes process/lease overhead; phase time does not.

All **48 real native runs** passed the ABI smoke check. Every baseline and cached
library had the same SHA-256, also matching the original diagnostic build:

```text
3d5e5413dda814bf56af3de8eae0dfb6432d4ddefe5a8248f72fac79895f9916
```

On every hit, both the hash and mtime stayed unchanged, and there were no
shim-compile/native-link phase records. This preserves native artifact identity
for matching-runtime validation; it does not waive any acceptance checks.

## Synthetic and Cargo controls

The synthetic run used the actual build script against small independent SDKs.
**49 build jobs** behaved as expected: 47 successful library checks and two
expected failures, one preprocessing error and one corrupted-archive link error.
Controls covered:

- different values in four simultaneous sessions;
- managed-only edits reusing native output;
- changed shim headers with their original mtime preserved;
- changed SDK source/archive bytes and changed C++ flags;
- missing/corrupted output libraries and corrupted receipts;
- compiler selection and replacement of its bytes at the same path;
- compile/link failure and recovery without a stale success receipt.

Eight additional controls invoked **Cargo itself**: initial build, unchanged
build, managed edit, flag change/reset, missing library, and initial/changed
external include. All passed. The external-header test changed only a header
outside the watched native tree, proving Cargo registered that transitive input.
The managed edit reused the library without changing its bytes or mtime.

The tiny synthetic SDKs were **12.8% slower** with the receipt: warm four-session
median 0.624 s versus 0.704 s. Their shim compilation and link together took about
0.044 s, less than input verification. They establish invalidation correctness,
not a benefit for tiny native libraries. A first receipt miss also pays for
verification before and after compilation. These costs are retained in the logs.

## Scope and reproduction

The measured benefit applies to repeated host build-script invocations with
unchanged native inputs. It is not a full engine, C# compilation, gameplay or
Unity Editor benchmark. macOS was not available for runtime measurements; MSVC
keeps the existing compile/link path. Source/header bytes used by preprocessing
and actual SDK archives are fingerprinted each time the script runs. The existing
SDK archive cache's input policy is unchanged. Cargo itself still uses mtimes
to decide whether the script needs to run.

Read-only inspection and benchmark artifacts live outside Git:

```text
~/.cache/unbound/validation/runs/native-link-cache-20261102/
  baseline-build.rs
  real-baseline-cold.log
  real-baseline-warm.log
  baseline-result.json
  baseline-libunbound_physx.so
  seed-archives.json
  real-parallel-measured/results.json
  real-parallel-measured/metadata.json
  real-parallel-measured/summary.json
  synthetic-measured/results.json
  synthetic-measured/cargo-controls.json
  synthetic-measured/metadata.json
```

Only `real-parallel-measured` and `synthetic-measured` supply the final table and
control counts; preliminary runs are not pooled with them. Each directory also
contains the compiled driver source snapshot, per-job commands/environments,
logs, lease records, output hashes and phase times. The metadata records exact
production source hashes. See [README.md](README.md#native-host-link-reuse) for
commands that work without these machine-local artifacts.
