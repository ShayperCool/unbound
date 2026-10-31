# Inline transition phases

These 10 cases distinguish immediate inline values from sampled animation values. They set
width and opacity separately, then retarget opacity before the first update or from a Run,
Start, Cancel or End callback, on either the same element or its sibling. The panel clock is
explicit, including repeated timestamps. Callback records keep the target, observer,
propagation phase, property, elapsed time and all presented values.

The Unity 6000.0.71f1 reference contains 110 samples. Two independent Native cache projects
produced exactly the same bytes before this one canonical file was published. Other releases
are unmeasured. Existing transition references are unchanged.

```sh
cache="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/transitioninline"
python3 tests/projects/uitransitioninline/run.py native --release 6000.0.71f1 --output "$cache/native-a"
python3 tests/projects/uitransitioninline/run.py native --release 6000.0.71f1 --output "$cache/native-b"
cmp "$cache/native-a/capture/inline.jsonl" "$cache/native-b/capture/inline.jsonl"
python3 tests/projects/uitransitioninline/run.py engine --release 6000.0.71f1 --output "$cache/engine" \
  --cli /path/to/private/unbound --managed /path/to/fresh/managed
python3 tests/projects/uitransitioninline/verify.py --release 6000.0.71f1 \
  --candidate "$cache/engine/capture/inline.jsonl" --output "$cache/report.json"
```

The driver wraps its heavy children and creates disposable cache projects. The verifier uses
the complete ordered union comparator from `uitransitioncallbacks`. Only physical value arrays
use the original `2e-5` absolute / `2e-6` relative tolerance. Event fields and elapsed times are exact.
