# Platform probe

What the platform APIs answer in the Linux Editor, which has no phone behind them: AndroidJNI and
its Java wrappers (AndroidJavaObject, AndroidJavaClass, AndroidJavaProxy, AndroidJNIHelper's
signatures, argument arrays and lookups), Android permissions and the application object,
Handheld, iOS.Device, the webcam and microphone, location, compass, gyro and accelerometer, user
authorization, screen orientation and cutouts, and the touch-screen keyboard. `PlatformProbe`
records what each call returns or throws and what it logs, as JSON lines in `PLATFORM_PROBE_OUT`,
and quits after three frames.

Record Unity's reference (`ProbeRunner.Create` made the empty scene; the Editor needs a display):

    PLATFORM_PROBE_OUT=$PWD/tests/projects/platform/Reference/unity-6000.0.71f1.jsonl \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/platform \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/platform.log

Run Unbound and compare. Mono ends the NullReferenceException message without the period CoreCLR
puts there, so that one is normalised:

    PLATFORM_PROBE_OUT=/tmp/platform.jsonl unbound run -p tests/projects/platform --headless 5 --no-watch
    sed 's/instance of an object\./instance of an object/' /tmp/platform.jsonl |
        diff tests/projects/platform/Reference/unity-6000.0.71f1.jsonl -

The answers depend on the machine only where the Editor asks the hardware: the reference was
recorded on a machine without a webcam (`WebCamTexture.devices` is empty, `Play` does nothing).
Unbound captures neither the webcam nor the microphone yet; it warns when a game starts either on a
machine that has the device.
