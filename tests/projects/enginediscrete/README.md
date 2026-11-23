# Discrete curves of engine components' properties

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (the values do not depend on the OS
or the graphics device). The gate compares every value exactly.

`Assets/Scripts/EngineDiscreteProbe.cs` records:

- `discrete.<Component>`: the bindings `AnimationUtility.GetAnimatableBindings` marks discrete
  for a SpriteRenderer, Light, Camera, MeshRenderer and AudioSource (object references only:
  materials and the sprite), and `binding.<case>` whether a case's property is offered at all and
  how (a SpriteRenderer's `m_SortingOrder` as a float binding; Light `m_Type` and
  `m_Shadows.m_Type`, Camera `m_ClearFlags`, MeshRenderer `m_CastShadows`, AudioSource
  `m_Priority` not at all);
- `play.<case>.<form>`: the property's value before and after a playable graph evaluates a clip
  made with `AnimationUtility.SetEditorCurve` at 0, 0.5 and 0.5 more seconds, its keys (1, then 2
  at one second) on a discrete binding written as the integer's bits (`bits`) or as numbers
  (`numbers`), or on a float binding (`float`, 1 then 2.6).

A discrete curve of an engine component's property animates nothing, whatever its keys; a float
curve on `m_SortingOrder` sets the rounded value; float curves on the properties the Editor does not
offer change nothing.

Unmeasured, and left binding as before: the legacy Animation component and
`AnimationClip.SampleAnimation` (which treat discrete curves otherwise), the Animator's own curves,
and a player (bundle) clip's discrete curves (its bindings' `isIntCurve` flag is not read).

Known residuals (`residuals.json`): Unbound animates any serialized field of an engine component,
also those the Editor does not offer, and its `GetAnimatableBindings` lists no object-reference
bindings and not `m_SortingOrder`.

The scene is the uitkpanelutils probe's, the settings the modelroot probe's. Other releases are
unmeasured.

## Usage

```bash
python3 tests/projects/enginediscrete/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/enginediscrete/run.py publish run-a run-b
python3 tests/projects/enginediscrete/run.py gate
```

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical macOS batch captures.
