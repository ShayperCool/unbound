# Playables probe

Scenarios over the Playables API and what runs on it, run identically in the Unity Editor and in
Unbound; each writes its values (callback traces with frame data, transforms, active states, signal
and audio times) to a JSON file and `compare.py` lists the differences (numbers within a tolerance,
also inside event text).

* `core.*`: PlayableGraph and playables evaluated synchronously: callback order, time, speed,
  weights, play states, traversal modes, wrap modes, connections, outputs, notifications.
* `frames.*`, `director.frames`: graphs and a PlayableDirector the player loop updates, with the
  player loop phase of every callback (marker systems around the Director systems).
* `anim.*`: animation playables: clips, mixers, layer mixers (masks, additive), outputs sharing an
  Animator, the controller playable, animation events, animation jobs; `anim.frames` the phase
  the animation is written in.
* `audio.eval`: audio clip and mixer playables.
* `timeline.evaluate`, `timeline.play`: the scene's TimelineAsset (animation, activation, audio,
  signal and control tracks, a marker) evaluated at set times and played over frames;
  `timeline.loop` plays it looping on a runtime director, layered over an Animator's controller.
* `rig.frames`: Animation Rigging (two-bone IK and multi-aim constraints) following moving targets.
* `cine.frames`: a Cinemachine track cutting and blending between two virtual cameras.

The scene and assets are authored by the Editor (`Assets/Editor/ProbeSetup.cs`):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -nographics \
        -projectPath tests/projects/playables -executeMethod ProbeSetup.Build -quit -logFile /tmp/setup.log

Record Unity's reference (the Editor enters Play mode, writes and exits):

    PLAYABLES_PROBE_OUT=/tmp/unity.json ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -nographics \
        -projectPath tests/projects/playables -executeMethod ProbeRunner.Record -logFile /tmp/unity.log

Run Unbound and compare:

    PLAYABLES_PROBE_OUT=/tmp/unbound.json unbound run -p tests/projects/playables --headless 1200
    python3 tests/projects/playables/compare.py Reference/unity-6000.0.71f1.json /tmp/unbound.json

`PLAYABLES_PROBE_ONLY=<prefix>` runs only the scenarios whose name starts with the prefix;
`PLAYABLES_DEBUG=1` logs the director graph's trees in `timeline.evaluate`. `Reference/` keeps the
recording of the release named in its file name.

Known difference: `director.frames` destroys a director's GameObject from Update. The Editor runs
OnDisable at the end of the Update scripts and OnDestroy right after them, so the director's graph
is torn down in that frame's DirectorUpdate; Unbound destroys objects later in the frame
(PostLateUpdate), so the graph plays one more DirectorUpdate.
