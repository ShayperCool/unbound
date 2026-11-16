# Transition value kinds

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 207 rows (managed style state of a runtime panel
driven by an explicit millisecond clock, as in `uitransitions`; independent of the OS and the
graphics device). The gate compares every row exactly.

Each case sets a start value, a one-second transition (linear unless named), then the end value,
and records the resolved value at 0, 250, 500, 750 and 1000 ms with the element's usage hints and
the transition events since the previous row.

- `bgposX`, `bgposY`, `textOverflow`, `whiteSpace`: discrete values, taken at half the eased
  progress (background positions too, offset and keyword alike); `sliceLeft`: integer rounding.
- `rotateTurn`: degrees and turns; `translateBack`, `originBack`: depth (z) stays within its end
  values under an overshooting easing while x and y overshoot.
- `widthTo*`, `heightToPercent`, `marginTopToPercent`, `flexBasis*`, `radiusToPercent`,
  `translateToPercent`, `zeroParent*`: a length going between pixels and percent converts its
  start against the parent's width or height (margins: width), flex-basis against the parent's
  main axis, border radii against the element's own width, translate against its own size; a
  start near 0 becomes 0px; a parent of no width still converts to pixels, never to percent.
- `fontSizeToPercent`, `letterSpacingToPercent`, `bgSizeToPercent`, `bgSizeInitialToPixels`: text
  lengths and background sizes never convert, so a change of unit does not run (the initial
  background size is 100% 100%); `bgSizeSameUnit`: the same unit runs; `bgSizeCover*`: a keyword
  size (cover, contain) has 0px components and runs from them, to pixels and to another keyword
  (which it takes at the end); `bgSizeAutoComponent`: a size with an `auto` component does not run.
- `fontPercent*`: a percent font size from a USS rule is a share of the parent's font size; an
  inline one (from code or a UXML `style` attribute) keeps its number, and a child inheriting it
  takes its own share of that number.
- `hint*`: a starting transition of a colour (background, border, text, image tint) adds
  `UsageHints.DynamicColor`, of rotate, scale, translate or transform-origin `DynamicTransform`,
  inline, from USS classes and through `all`, keeping hints already set; one that does not start
  adds nothing.

Other releases are unmeasured.
