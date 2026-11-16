# Pointer capture events

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 11 rows (managed event state in a runtime panel,
independent of the OS and the graphics device). The gate compares every row exactly.

Two elements, A and B, record each PointerCaptureEvent and PointerCaptureOutEvent they get: its
target, the pointer id through the public `pointerId` and through the internal
IPointerCaptureEventInternal interface (reflection), and its related target.

- `interfaces*`: both capture event types implement IPointerCaptureEvent and the internal
  IPointerCaptureEventInternal.
- `script.*`, `reuse`: events a script makes with `GetPooled(target, relatedTarget, pointerId)` carry
  that pointer id (touch 4, mouse 0) through both; a reused pooled event is reset (no target, pointer
  id -1).
- `panel.*`: after `CapturePointer`/`ReleasePointer`, the panel sends the capture events when it
  next processes a pointer event (here a move of the same pointer): a capture, a capture moved from
  A to B (out on A related to B, then in on B related to A) and a release, for a touch and for the
  mouse.

Other releases are unmeasured.
