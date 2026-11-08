# Stream completion diagnostic

These source snapshots retain the exploratory controls for the Unity 6000.0.71f1
Editor. The complete driver expects the normal mixersuspend fixture layout. To
repeat it, overlay these three source files at their original Controls/ and root
paths in a private fixture copy, then run reset_data.py native there. Captures
remain outside Git.

With a 36000-frame streamed Vorbis clip, forty offline 1024-frame renders did not
complete playback in the Editor. The engine's decoded stream completed in those
renders. Automatic streamed completion therefore needs separate investigation of
stream delivery and clock ownership. The published reset-data profile measures
explicit Stop, destruction and clip replacement only. No completion observation
from this diagnostic replaces an existing oracle or changes a tolerance.
