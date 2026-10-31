# Interrupted GPU startup

The first final2 focused Gamma camera attempt exited 101 before rendering or
writing any observations/buffers. Its complete startup log is retained here.
Vulkan device creation returned `RequestDeviceError: Device(Lost)`. The GPU check
afterwards reported RTX 4060, 1112/8188 MiB in use, 59 C and 4% utilization, with
only the existing ccc daemon listed as a compute process. No renderer changes,
driver settings or comparison tolerances were used to work around the failure.

The failed private log is `regress-final2/camera-focused-gamma.log`. A single
fresh-directory retry is `regress-final2/camera-focused-gamma-retry1`, using the
same project, CLI, private managed output, native library and command arguments.
The first final-build Gamma control is also preserved in `regress-final` and its
strict result in `Evidence/InitialFinal/final-camera-focused-gamma.txt`.
