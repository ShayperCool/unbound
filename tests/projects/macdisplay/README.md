# Display queries on macOS

The answers about the host's display are decided by the OS, so they follow the macOS Editor.
`Assets/Scripts/MacDisplayProbe.cs` records, on the fifth frame of Play mode (Game view at a
960x540 rendering resolution, the engine's window 960x540), `Screen.currentResolution`,
`Screen.resolutions`, `Screen.dpi`, `Display.main`'s sizes, `Screen.mainWindowDisplayInfo` and
`Screen.GetDisplayLayout` (`unity.*`), and the main display's facts read in the same process from
CoreGraphics (current and all display modes, physical size), CoreVideo (the display link's
nominal refresh period) and AppKit (`NSScreen` frame, visible frame, backing scale) (`os.*`).

Matches the Unity 6000.0.71f1 macOS Editor (two identical windowed captures on a MacBook's
built-in display): the current resolution is the desktop's usable area (the visible frame, the
screen without the menu bar) in pixels, at the display link's refresh period as an exact ratio
(24000000/199999); the resolutions are one per display mode's pixel size at that rate, the current
mode's given as the usable area, plus the desktop's size in points without a rate (0/1), ordered by
width then height; the density is the current mode's pixel width over the screen's width in
inches; the display info and layout carry the screen's localized name, the current resolution and
its work area (bottom-left origin); `Display.main` keeps the Game view's size.

The reference describes the recording machine's display: `check` compares the `unity.*` rows only
when this machine's `os.*` rows equal the reference's, and otherwise reports that the reference
does not apply (a display with the Dock shown, or another display, is unmeasured). On another OS
the probe does not apply.

    python3 tests/projects/macdisplay/run.py native --label L     # two macOS Editor captures
    python3 tests/projects/macdisplay/run.py publish L-1 L-2
    python3 tests/projects/macdisplay/run.py check                # the engine against the reference
