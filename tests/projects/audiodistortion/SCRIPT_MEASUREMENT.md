# Scripted spatial filter controls

Unity 6000.0.71f1, Linux Editor, stereo, 1024-frame DSP buffers at 48000 and
44100 Hz. The `spatial-script` profile records 43 complete 32-buffer device and
master streams per rate. Two independent Editor captures agree exactly, including
5,636,184 records per rate. Existing static and spatial references remain unchanged.

The affine script halves its input and adds different left/right constants. This
distinguishes Inspector placements even for memoryless Distortion. The controls
cross mono/stereo clips, Distortion, Chorus, both component orders, script before
and after components, partial spatial blend, source volume and bypass. A 2D bypass
control and clipless generator controls distinguish routing from spatialization.
Generator controls include downstream Distortion, Chorus and their order, source
volume, partial/full spatial blend, and a changing signal through live bypass.
Generated channels retain their positions; scalar source/distance gain precedes
components. A bypassed generator keeps its callback output and skips downstream
components.

| Unity 6000.0 contract | Divergent Editor control |
| --- | --- |
| [Use audio filters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEffect.html) applies enabled components in Inspector order. | Noncommuting affine scripts and Distortion/Chorus pairs reverse order; existing `source_disabled` and listener-disabled controls retain enabled/disabled coverage. |
| [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) occupies the script's Inspector position and can generate audio without a clip. | Script before/after each component pair changes PCM. Clipless generators run with bypass on and off; the clip-backed bypass control separately retains the dry voice. |
| [AudioSource.volume](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-volume.html) sets source volume; [spatialBlend](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-spatialBlend.html) blends 2D/3D influence. | Quiet generators set the initial volume from 1 to 0.25 while their callbacks produce nonzero audio. Full/partial spatial controls retain the same generated channel positions and change distance gain before noncommuting components. The exact scalar placement is measured behavior. |
| [bypassEffects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassEffects.html) bypasses source effects. | Static clipped-source bypass keeps the voice dry and feeds silence to the separate script return. Its affine offset remains audible after its components. This routing detail and the clipless-generator result are measured behavior, not additional documented guarantees. |

The gate requires all 38 supported controls with unchanged float tolerances and
the existing one-step device PCM allowance. It also writes a full 43-control strict
comparison. Five live reconnection controls retain their complete mismatching PCM.
Four scripted clip controls require explicit `NotSupportedException` diagnostics
for both attempted bypass changes, leaving the current route intact; Stop permits
reconfiguration. Changing bypass while a clipless generator plays remains supported.

Follow-up: live bypass reconnection of clipped source chains. The Editor changes
callback input ownership, processing activity and retained effect history according
to chain placement and prior connections. The additional initial-bypass, script-last,
component-only and Stop/Play controls show that a single history clear or pause rule
is insufficient. This requires a shared connection lifecycle model. Scripted 3D
Distortion/Chorus reconnection fails explicitly; component-only reconnection retains
its previous behavior and full diagnostic comparison. No values, tolerances or
ordering have been changed to hide those differences.
