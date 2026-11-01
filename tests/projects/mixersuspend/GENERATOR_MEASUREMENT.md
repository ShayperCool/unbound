# Clipless generator control

Use `native.py --generators` and `engine.py --generators` with an explicit release,
rate and fresh work directory. Compare with `compare.py --profile generators`.
The control renders 88 complete 1024-frame stereo DSP blocks in each of two cases.
It keeps every device and master-sink sample, including the quiet interval.

In `attached`, an enabled clipless AudioSource has an OnAudioFilterRead generator
from the beginning. Play is called after attaching the callback and mixer route.
In `late`, the generator is attached before block 80, after
more than one second of quiet rendering. Both generators emit silence until block
82, then emit 0.125 on both channels. The mixer has suspension enabled and a dry
path through a five-second Echo. There is one audio update before each render.

The master sink is filled with -3 before every registration. Retained sentinels
distinguish a suspended graph from a graph that writes silence. Successful capture
requires all four complete files, invoked callbacks, audible final blocks and the
terminal `complete` field. Publish one
canonical profile only after independent Native captures agree exactly. This
control measures generator activity and wake behavior; it does not certify reset,
remote output or other callback configurations.
