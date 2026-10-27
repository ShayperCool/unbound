# XR probe (no device)

What the XR API answers in the Editor without an XR provider or plug-in: `XrProbe` records
XRSettings, XRDevice, XRStats, InputDevices and InputTracking, an invalid InputDevice, the XR
subsystems, every CommonUsages field's usage name, then XRSettings after
`LoadDeviceByName("MockHMD")` (the Editor's built-in mock HMD loads but never becomes active) and
after setting its scales.

Record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/xr \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/xr-create.log
    XR_PROBE_OUT=$PWD/tests/projects/xr/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/xr \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/xr.log

Run Unbound and compare:

    XR_PROBE_OUT=/tmp/xr unbound run -p tests/projects/xr --frames 300 --size 320x180 --no-watch
    python3 tests/projects/xr/compare.py tests/projects/xr/Reference /tmp/xr

Every record matches.
