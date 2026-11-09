# Scripted spatial filter controls

Unity 6000.0.71f1, Linux Editor, stereo, 1024-frame DSP buffers at 48000 and
44100 Hz. The `spatial-script` profile records 55 complete 32-buffer device and
master streams per rate. Two independent Editor captures agree exactly, including
7,209,074 records per rate. Existing static and spatial references remain unchanged.

The affine script halves its input and adds different left/right constants. This
distinguishes Inspector placements even for memoryless Distortion. The controls
cross mono/stereo clips, Distortion, Chorus, both component orders, script before
and after components, partial spatial blend, source volume and bypass. A 2D bypass
control and clipless generator controls distinguish routing from spatialization.
Generator controls include downstream Distortion, Chorus and their order, source
volume, partial/full spatial blend, and a changing signal through live bypass.
Generated channels retain their positions; scalar source/distance gain precedes
components. A bypassed generator keeps its callback output and skips downstream
components. The additional full-scale quiet controls distinguish the gain floor:
changes below 0.00032 retain the preceding DSP value, while a change exactly at
0.00032 is applied. A live 0.2501-to-0.25 control records both public values and
the retained audible gain. These are measured details, not documented guarantees.

| Unity 6000.0 contract | Divergent Editor control |
| --- | --- |
| [Use audio filters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEffect.html) applies enabled components in Inspector order. | Noncommuting affine scripts and Distortion/Chorus pairs reverse order; existing `source_disabled` and listener-disabled controls retain enabled/disabled coverage. |
| [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) occupies the script's Inspector position and can generate audio without a clip. | Script before/after each component pair changes PCM. Clipless generators run with bypass on and off; the clip-backed bypass control separately retains the dry voice. |
| [AudioSource.volume](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-volume.html) sets source volume; [spatialBlend](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-spatialBlend.html) blends 2D/3D influence. | Quiet generators set the initial volume from 1 to 0.25 while their callbacks produce nonzero audio. Full/partial spatial controls retain the same generated channel positions and change distance gain before noncommuting components. The exact scalar placement is measured behavior. |
| [bypassEffects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassEffects.html) bypasses source effects. | Static clipped-source bypass keeps the voice dry and feeds silence to the separate script return. Its affine offset remains audible after its components. This routing detail and the clipless-generator result are measured behavior, not additional documented guarantees. |
| [bypassEffects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassEffects.html) changed during playback. | Live controls toggle bypass at buffers 8 and 16 (one control at 8, 12, 16 and 20) on 2D/3D clips, script before/after Distortion and Chorus, component-only Echo and Chorus/LowPass chains, a clipless source that plays only `PlayOneShot`, Stop/Play while bypassed, and clipless generators. |

Live bypass reconnection, measured rather than documented. A bypass change moves
the voice to or from its dry route and sets the built-in components inactive while
bypassed. An inactive component holds its state and silences every unit above it in
Inspector order; scripts still run on silence below the last inactive component. The
first bypass change after a clip starts playing (`Play`, `PlayOneShot`) also
reconnects the chain: each component restarts empty (Chorus keeps its sweep span,
the resonant LowPass keeps its state) and the last unit in Inspector order stays
active, processing silence while bypassed. Later changes only set activation. After
a `Stop`, `Play` restores activation from `bypassEffects`; components keep their
state through `Stop` followed by `Play`. A clipless generator's `Play` does not
reconnect. A changed list of filter components or scripts activates every component.
`bypassListenerEffects` and component enable changes during playback are unmeasured;
they leave the activation unchanged.
`Compat.AudioComponentActivationMeasured` selects this; other releases keep
components frozen on the bypass flag. A Stop/Play of a quiet generator restarts its
64-frame source gain ramp from silence.

The gate requires all 55 controls with unchanged float tolerances and the existing
one-step device PCM allowance, and writes the full strict comparison.
