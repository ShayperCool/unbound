# Audio from an AssetBundle

An `audio` bundle the Editor built for StandaloneLinux64 (`ProbeRunner.Create`, committed under
`Reference/bundles`): test tones imported with each compression the Editor offers there — Vorbis at
qualities 0.3 to 1 (mono and stereo, 22.05 to 48 kHz), ADPCM, PCM — and each load type (decompressed
on load, compressed in memory, streaming). `AudioProbe` records, for each clip of the bundle and then
for the same file as the project's asset: its properties, its load state, whether GetData reads it
(every 37th sample and the sum of squares), and whether an AudioSource plays it a frame later.

Measured (see `crates/unbound-assets/src/fsb5.rs` and `managed/UnityEngine/Audio/AudioClip.cs`): a
player clip keeps an FMOD sound bank (FSB5) in the bundle's `.resource` stream. PCM16 reads back as
stored; IMA ADPCM is FMOD's (blocks of 64 frames: a header sample, then 63 nibbles, channels
interleaved by four bytes) and its clip is as long as its last block, as an ADPCM asset's is; a
Vorbis sound keeps its audio packets and the CRC32 of its setup header, which libvorbis 1.3.7 writes
again for every rate from 32 kHz up (FMOD's own below that: the 22.05 kHz clip is reported as not
supported), and FMOD stores its samples as `x * 32767` truncated to 16 bits. GetData fails with
Unity's messages on a clip compressed in memory (but PCM) and on a streamed one, and a streamed
asset stays unloaded through LoadAudioData and Play.

Make the bundle (once; it is committed):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -nographics -projectPath tests/projects/bundleaudio \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/bundleaudio-create.log

Record Unity's reference (the Editor needs a display):

    AUDIO_PROBE_OUT=$PWD/tests/projects/bundleaudio/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/bundleaudio \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/bundleaudio.log

Run Unbound and compare (a fixed frame rate: the clips last 0.75 s, and a slow first frame would
let a clip end before the frame that checks it):

    AUDIO_PROBE_OUT=/tmp/audio unbound run -p tests/projects/bundleaudio --headless 300000 --fixed-fps 60
    python3 tests/projects/bundleaudio/compare.py tests/projects/bundleaudio/Reference /tmp/audio

Every record matches but the 22.05 kHz Vorbis clip of the bundle (expected: Unbound reports it and
plays nothing). Vorbis samples may be a unit of the last bit off (FMOD's decoder is not libvorbis);
a project asset's ADPCM and Vorbis samples are its source's, not the lossy codec's, so only its
properties and load behaviour compare.
