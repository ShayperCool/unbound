# ImmediateModeElement on a runtime panel

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode on a runtime panel (a UIDocument made
in Play mode drawing into an 800x600 render texture, cleared to black each frame, constant pixel
size). The gate compares every value exactly.

`Assets/Scripts/ImmediateProbe.cs` adds ImmediateModeElements whose ImmediateRepaint draws a GL quad
(Hidden/Internal-Colored, no blending, no depth test) in the element's local coordinates: one larger
than its box with no clipping ancestor; one in an overflow-hidden parent, larger than the parent; one
scaled 2x3 about its top left; one before a later sibling with a background and one after an earlier
one, overlapping them; one that draws with `GL.LoadOrtho`, followed by an element with a background;
culled and unculled ones outside the target and a culled one partly inside; a hidden one; and one
that throws when asked, between two others. Values: the calls per frame, GL's modelview, the active
render texture and current camera during the first call of a frame, the target's pixels at chosen
points (read the next frame), the logs, and the same after an exception, with `cullingEnabled`
swapped, and at panel scale 2.

- ImmediateRepaint runs once per frame the panel renders. GL's modelview is the element's world
  transform (its local space in points, including its scale), also at panel scale 2, where the quads
  land at twice the pixels: GL's projection maps points to the target. The active render texture is
  the panel's target and there is no current camera.
- Drawing is in the element's place among the panel's draws: over earlier siblings' backgrounds and
  under later ones'. A quad spills outside its element's box, but not outside an overflow-hidden
  ancestor's padding box. `GL.LoadOrtho` covers the whole target (its (1, 1) corner at the top right
  of the read-back image), and elements drawn after it are placed as usual.
- With `cullingEnabled`, an element whose bounds miss the target is not called; one partly inside is.
  Without it, elements outside are called. Hidden elements are not called.
- An exception in ImmediateRepaint: the later elements' callbacks are skipped for that frame and the
  exception is logged once with the thrown exception's type and message; the next frame calls all
  of them again.

Other releases and panels drawn by cameras are unmeasured.
