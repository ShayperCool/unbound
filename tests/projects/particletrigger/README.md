# Particle trigger controls

The release-selected canonical has 402 records, including the original 150 controls
also recorded by the broad particles probe. `run.py` prepares private project copies
under the cache; `verify.py --unity-version 6000.0.71f1` keeps every missing, extra and
failing component with the original tolerance.

## Measured lifetime correction, 2026-11-02

The tested source is `f3f50958`. One managed build and seven engine captures cover
trigger 402, lifetime 27, boundary 565, cross-kind 96, broad 1560 with 17 images and
emission 16573. Trigger has zero failing values, including the original 150 and the
previous row 16 lifetime mismatch. All eight regressions of the earlier zero-life
candidate are resolved. No comparator introduces a failure against either baseline.

An existing trigger-killed particle remains observable with zero life until the next
internal step; newborns killed during emission are discarded immediately. Constant-rate
newborns are aged by their share of the whole internal step, including loop crossings.
Manual simulation includes referenced sub-emitter systems; the child's paused/alive
state and clock now match the recorded controls. Other restart/reference combinations
remain unmeasured. These rules are general, without fixture names, seeds or snapping.

The later origin correction, tested at `2cc4eef0` / Core `dfb08538`, resolves all
three target child positions. Natural death and trigger-killed carry emit at the
end of the parent's whole last step. Only a copy's event position moves; the stored
particle, trails, collision deaths and inherited fields keep their previous behavior.
The three first-age lifetimes remain unchanged, differing by 0.005 seconds. Broad
child position/life errors remain functional too. Boundary 565 stays byte-identical,
including all 599 failing components and the inherited 48 public-input bit paths.
The wider-intermediate model
for those inputs is a compiler-policy hypothesis, not an adopted policy.

Broad 305 and emission 847 failing components remain explicit. Small numerical changes
without observed count/seed/order/state/IsAlive or image worsening are documented in
the particles README and COMPATIBILITY_GAPS ledger; tolerances and raw values are
unchanged. All 17 broad images are byte-identical to both baselines. Cross-kind96 has
no Native oracle and makes no parity claim.

Evidence: `validation/runs/claude-fresh-20261101b/causal-validation/RECEIPT.md` and
`validation/runs/particle-causal-root-admission-20261102/root-validation.json` under
the global Unbound cache. Full raw unions, precision keys and rejected earlier
attempts remain there. No new Native capture was used for this correction.

Origin validation used one build, one unfiltered suite (1599/1610, the same 11),
three failing red controls on the older Core, and four AFTER probes. Trigger and
boundary are byte-identical; the only non-real-time raw deltas are three lifetime
child positions and ten broad child positions, all predicted. All 17 images are
byte-identical. No emission or cross-kind recapture was performed on this Core.
Death-copy movement with forces, noise, world space or inherited properties remains
unmeasured. The negative first-age result is preserved; no quantum rule is implemented.

Evidence: `validation/runs/claude-fresh-20261101b/origin-validation/RECEIPT.md` and
`validation/runs/death-origin-root-admission-20261102/root-validation.json`.
