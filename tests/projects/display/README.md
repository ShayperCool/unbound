# Display probe

What the Editor's Game view (Display 1) shows of cameras and overlay canvases that target other
displays (`Assets/Scripts/DisplayProbe.cs`): a red camera on Display 1, a green one on Display 2
over the right half (a higher depth), a blue one on Display 1 over the top-left quarter, a yellow
overlay canvas on Display 1 and a magenta one on Display 2. A few frames in, the Game view is
captured (`game.png`) and whether each camera rendered (OnPreRender) is written to `values.jsonl`.

The reference needs a windowed Editor (ScreenCapture reads the Game view), on an X server of its own
with the GPU through VirtualGL: `ProbeRunner` opens the scene at a 320x180 Game view and enters Play
mode when `DISPLAY_PROBE_RUN=1`.

    Xvfb :78 -screen 0 1920x1080x24 -nolisten tcp &
    DISPLAY=:78 DISPLAY_PROBE_RUN=1 DISPLAY_PROBE_OUT=$PWD/tests/projects/display/Reference \
        vglrun -d egl ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -projectPath tests/projects/display -logFile /tmp/display.log

Unbound (its window captured at frame 4, before the probe quits):

    DISPLAY_PROBE_OUT=/tmp/display unbound run -p tests/projects/display --no-watch --size 320x180 --snapshot 4:/tmp/display.png
    python3 tests/projects/display/compare.py tests/projects/display/Reference /tmp/display /tmp/display.png

What the Editor does (6000.0.71f1, Linux): a screen camera targeting another display does not
render at all (no OnPreRender); an overlay canvas targeting another display is not drawn;
`Camera.allCamerasCount` still counts every enabled camera; `Display.displays` has one display.
Unbound matches exactly.
