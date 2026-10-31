# Combined 2D query and captured skin validation

This source-only harness combines query, lifecycle, loading and motion regression
checks. Canonical Native data belongs to the corresponding probe's versioned
Reference set; this directory keeps no duplicate oracles and no run artifacts.
The common suite map is `tests/query_references.py`.

The accepted implementation closes the 76 original broad query failures and
retains 45 motion values. Main query controls retain 3,192 point/normal values;
selection retains one, fine selection six, support geometry twenty union-key
values, and the frame/center suite 2,729. Boundary64, isolated48, box-skin644 and
initial-overlap6256 pass. All failures remain visible with the unchanged geometry
comparator. Expanded motion and rejected investigations are cached outside Git.

The last integrated validation passed 494 tests, the 5,055 scene matrix values,
loading9,312, lifecycle25,356, receiver1,152, timing18,491, order5,720, all focused
3D controls, existing physical and graphics regressions and API checking. These
counts describe the measured release and do not claim full engine parity.

```bash
python3 tests/projects/query2dskinmerge/verify.py
python3 tests/projects/query2dskinmerge/report.py --work /path/to/cache/run --label final
# Report all failures while checking whether an explicit baseline regressed:
python3 tests/projects/query2dskinmerge/report.py --work /path/to/cache/run --label final --baseline-work /path/to/cache/baseline --no-regressions
```

Default verify validates references only; engine comparisons require actual run
inputs. Full validate/check_gates commands use explicit private work and matching
build artifacts. All generated captures, reports, manifests, TRX and logs default
to `$XDG_CACHE_HOME/unbound/validation/runs/`, never this directory. Use a fresh run
label to retain failed attempts. Existing archive data may be read explicitly for
migration audits, but is never the canonical Unity oracle. Other release behavior
needs its own Editor reference and compatibility-profile handling where different.
