# Style-property animation interface

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 189 rows (managed style state of a runtime panel
driven by an explicit millisecond clock, as in `uitransitionkinds`; independent of the OS and the
graphics device). The gate compares every row exactly:

```sh
python3 tests/projects/styleanimations/run.py check
```

The probe reaches the element's internal `IStylePropertyAnimations` through reflection. Rows record
the read value, the running and completed counts, `HasRunningAnimation`, the set of properties
`GetAllAnimations` lists (the interface leaves its order unspecified), the usage hints and the
transition events since the previous row.

- `float`, `length`, `color`, `int`, `enum`, `intOnEnum`, `scale`, `rotate`, `translate`, `origin`,
  `bgPosition`, `bgRepeat`, `bgSize`, `background`: each typed start sampled at 0, 250, 500 and
  1000 ms. A start shows nothing before the next animation update (`float.update`: UpdateAnimation
  shows the start value at once); the enum start is discrete, the integer start rounds (on an enum
  property too); no start adds a usage hint. After the end the animation counts as completed, a start to
  its end value is refused and one to another end replaces it.
- `refuse.*`: equal values, a zero duration, a negative delay as long as the duration, NaN and a
  font size changing unit are refused. `delayOnly`, `negativeDelay`: a zero duration with a positive
  delay ends without starting; a negative delay starts part way.
- `running.*`: on a running animation the same end keeps it, its current value or a zero duration
  cancels it, its own start value reverses it (shortened) and another end retargets it from the
  current value.
- `cancel.*`: CancelAnimation puts the end value in place and its Cancel comes with the next
  update; CancelAllAnimations sends Cancel at once; both forget completed animations.
- `uss.*`: USS transitions seen through the interface (counts, cancelling one, a completed transition
  removed by a zero duration).
- `restyle.*`: a style pass keeps an animation its transition list does not name; without
  transitions the style's own value shows until the next update (`.styled` is read right after the
  style pass, before layout), with transitions the animated value stays, and changing the list cancels it.
- `counts.*`: the counts are plain settable values; HasRunningAnimation, GetAllAnimations and
  CancelAllAnimations look further only while one is positive.
- `detached.*`: outside a panel Start and UpdateAnimation throw NullReferenceException; the rest do nothing.

Text-shadow, font and font-definition starts are not read (the resolved style has no text shadow)
and are unmeasured. Other releases are unmeasured.
