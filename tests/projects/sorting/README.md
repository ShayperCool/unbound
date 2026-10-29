# Sorting probe

How Unity orders a world-space canvas against renderers in the same sorting layer and order: one
cell per scenario, a red canvas image over a green renderer, at different depths, in creation
orders, with nested canvases, several batches, culled images, and meshes whose vertices sit away
from their pivot. Each cell's winner is the colour on top.

Record Unity's reference (the Editor renders the camera into a texture and reads it back):

    SORTING_PROBE_OUT=/tmp/unity.json DISPLAY=:0 ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode \
        -projectPath tests/projects/sorting -executeMethod ProbeRunner.Record -logFile /tmp/unity.log

Run Unbound (the winners are read from a screenshot of the window) and compare:

    SORTING_PROBE_OUT=/tmp/unbound.json SORTING_PROBE_SCREENSHOT=1 unbound run -p tests/projects/sorting \
        --size 960x540 --no-watch --screenshot /tmp/unbound.png --frames 10
    python3 tests/projects/sorting/compare.py tests/projects/sorting/Reference/unity-6000.0.71f1.json \
        /tmp/unbound.json /tmp/unbound.png

What Unity 6 does (the reference): a canvas sorts as one unit at the centre of the bounds of what it
draws (culled graphics left out), not at its own transform; a mesh renderer at its bounds centre; at
equal distance the canvas draws on top.

A disabled nested canvas hides everything under it, enabled canvases further down included.
