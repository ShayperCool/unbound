using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

sealed class ParticleVisibilityProbe : MonoBehaviour
{
    [Serializable]
    sealed class Row
    {
        public string k, v;
    }

    sealed class Rig
    {
        internal string Name;
        internal GameObject Root;
        internal Animator Animator;
        internal AnimationState Legacy;
        internal ParticleSystemRenderer Renderer;
        internal ParticleSystem ObservedSystem;
        internal ParticleSystemCullingMode SimulationMode;
        internal float PreviousSimulationTime;
        internal Transform Marker;
        internal bool World;
    }

    const string Variable = "PARTICLE_ANIMATION_VISIBILITY_OUT";
    const int Layer = 28;
    readonly List<Rig> rigs = new List<Rig>();
    readonly List<string> rows = new List<string>();
    Camera camera;
    int tick;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable(Variable) != null)
            new GameObject("Probe").AddComponent<ParticleVisibilityProbe>();
    }

    void Start()
    {
        rows.Add(JsonUtility.ToJson(new Row { k = "unity", v = Application.unityVersion }));
        Time.captureDeltaTime = .02f;
        camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        camera.orthographicSize = 3;
        camera.cullingMask = 1 << Layer;
    }

    Rig Create(string name, bool world, bool live, float minSize, bool trails, bool stretch, bool legacy = false, float size = 1f, float emittedSize = .5f, bool moving = false,
        ParticleSystemCullingMode cullingMode = ParticleSystemCullingMode.AlwaysSimulate, bool emitOverTime = false)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        root.layer = Layer;
        var marker = new GameObject("Marker").transform;
        marker.SetParent(root.transform, false);
        var ps = root.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.startSize = size;
        main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.cullingMode = cullingMode;
        var em = ps.emission;
        em.enabled = emitOverTime;
        if (emitOverTime)
        {
            main.startSpeed = 0f;
            em.rateOverTime = 20f;
        }
        var shape = ps.shape;
        shape.enabled = false;
        var trail = ps.trails;
        trail.enabled = trails;
        trail.minVertexDistance = .01f;
        var renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.minParticleSize = minSize;
        if (stretch)
        {
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.cameraVelocityScale = 1f;
        }
        var mat = new Material(Shader.Find("Sprites/Default"));
        renderer.sharedMaterial = mat;
        if (trails) renderer.trailMaterial = mat;
        Animator animator = null;
        Animation animation = null;
        if (legacy)
        {
            animation = root.AddComponent<Animation>();
            animation.playAutomatically = false;
            animation.cullingType = AnimationCullingType.BasedOnRenderers;
            animation.AddClip(Resources.Load<AnimationClip>("LegacyLoop"), "Loop");
        }
        else
        {
            animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Culling");
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
        }
        root.AddComponent<ParticleProbeEvents>();
        root.SetActive(true);
        if (animation != null) animation.Play("Loop");
        ps.Play(false);
        if (live)
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = world ? new Vector3(1000, 0, 0) : Vector3.zero,
                velocity = moving ? new Vector3(2, 0, 0) : Vector3.zero,
                startLifetime = 5,
                startSize = emittedSize
            }, 1);
        return new Rig
        {
            Name = name,
            Root = root,
            Renderer = renderer,
            ObservedSystem = cullingMode == ParticleSystemCullingMode.AlwaysSimulate ? null : ps,
            SimulationMode = cullingMode,
            Animator = animator,
            Legacy = animation == null ? null : animation["Loop"],
            Marker = marker,
            World = world
        };
    }

    void Update()
    {
        tick++;
        if (tick == 1)
        {
            rigs.Add(Create("control", false, true, 0, false, false));
            rigs.Add(Create("minimum", false, true, .2f, false, false));
            rigs.Add(Create("legacy", false, true, .2f, false, false, legacy: true));
            rigs.Add(Create("trails", false, true, 0, true, false));
            rigs.Add(Create("stretch", false, true, 0, false, true));
            rigs.Add(Create("empty", false, false, .2f, false, false));
            rigs.Add(Create("emptySmall", false, false, .2f, false, false, size: .1f));
            rigs.Add(Create("largeEmit", false, true, .2f, false, false, size: .1f, emittedSize: 4f));
            rigs.Add(Create("largeStartSize", false, true, .2f, false, false, size: 4f, emittedSize: .1f));
            rigs.Add(Create("movingTrail", false, true, 0, true, false, size: .5f, moving: true));
            rigs.Add(Create("movingStretch", false, true, 0, false, true, size: .5f, moving: true));
            rigs.Add(Create("worldFar", true, true, .2f, false, false));
            rigs.Add(Create("pauseTrails", false, true, 0, true, false, cullingMode: ParticleSystemCullingMode.Pause));
            rigs.Add(Create("pauseEmpty", false, false, 0, true, false, cullingMode: ParticleSystemCullingMode.Pause));
            rigs.Add(Create("catchUpTrails", false, true, 0, true, false, cullingMode: ParticleSystemCullingMode.PauseAndCatchup));
            rigs.Add(Create("automaticMinimum", false, false, .2f, false, false, cullingMode: ParticleSystemCullingMode.Automatic, emitOverTime: true));
            rigs.Add(Create("automaticTrails", false, true, 0, true, false, cullingMode: ParticleSystemCullingMode.Automatic));
        }
        if (tick == 6)
            foreach (var rig in rigs)
                if (!rig.World) rig.Root.transform.position = new Vector3(1000, 0, 0);
        if (tick == 11) camera.transform.position = new Vector3(1000, 0, -10);
        if (tick == 16) camera.cullingMask = 0;
        if (tick == 21) camera.cullingMask = 1 << Layer;
        if (tick == 26)
            foreach (var rig in rigs) rig.Renderer.enabled = false;
        if (tick == 31)
            foreach (var rig in rigs) rig.Renderer.enabled = true;
        if (tick == 36) camera.transform.position = new Vector3(1005.7f, 0, -10);
        if (tick == 41) camera.transform.position = new Vector3(1006.5f, 0, -10);
        if (tick == 46) camera.enabled = false;
        if (tick == 51)
        {
            camera.transform.position = new Vector3(1004.9f, 0, -10);
            camera.enabled = true;
        }
        if (tick < 57) return;
        if (tick == 57)
        {
            rows.Add("{\"k\":\"complete\",\"v\":true}");
            File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), rows);
        }
        if (tick != 59) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    void LateUpdate()
    {
        if (tick == 0 || tick > 56) return;
        foreach (var rig in rigs)
        {
            // Simulation controls use the relocation and layer-mask portions of the sequence.
            if (rig.ObservedSystem != null && tick > 25) continue;
            float normalizedTime = rig.Legacy != null ? rig.Legacy.normalizedTime : rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            var observation = $"{rig.Renderer.isVisible}/{Mathf.RoundToInt(normalizedTime * 10000)}/{Mathf.RoundToInt(rig.Marker.localPosition.x * 10000)}";
            if (rig.ObservedSystem != null)
            {
                float time = rig.ObservedSystem.time;
                if (rig.SimulationMode == ParticleSystemCullingMode.PauseAndCatchup)
                {
                    int advance = Mathf.RoundToInt((time - rig.PreviousSimulationTime) * 10000);
                    observation += advance == 0 ? "/held" : advance <= 200 ? "/step" : "/catchup";
                    rig.PreviousSimulationTime = time;
                }
                else
                    observation += "/" + Mathf.RoundToInt(time * 10000);
                if (rig.SimulationMode == ParticleSystemCullingMode.Automatic)
                    observation += "/" + rig.ObservedSystem.proceduralSimulationSupported;
            }
            rows.Add(JsonUtility.ToJson(new Row { k = rig.Name + "." + tick, v = observation }));
        }
    }
}

sealed class ParticleProbeEvents : MonoBehaviour
{
    public void OnClipEvent() { }
}
