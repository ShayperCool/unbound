# RuntimePanelUtils conversions

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (a 640x480 screen) on a screen panel:
a UIDocument made in Play mode without a target texture. The gate compares every value exactly
(three decimals).

`Assets/Scripts/PanelUtilsProbe.cs` converts with `RuntimePanelUtils.ScreenToPanel` screen points
on, at the corner of and off the screen; with `CameraTransformWorldToPanel` world points in front of
and behind a perspective camera at (0, 1, -10); with `CameraTransformWorldToPanelRect` world
rectangles, also for the camera turned (10, 30, 0) and orthographic (size 5). Each under constant
pixel size at scales 1 and 2 and under scale with screen size (1200x800, match 0.5); and a null panel.

- ScreenToPanel takes screen positions from the top left and divides them by the panel's scale; off
  the screen they are converted alike.
- CameraTransformWorldToPanel converts the camera's screen point, made top-left on the screen's
  height; a point behind the camera converts its (mirrored) screen point too.
- CameraTransformWorldToPanelRect is the rectangle from the position's panel point to the panel point
  of the position plus the size (y negated) transformed by the camera's world-to-camera matrix.
- A null panel throws NullReferenceException.

Other releases, displays other than the first and target-texture panels are unmeasured.
