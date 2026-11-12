using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class HiddenRootsControl : MonoBehaviour
{
    static readonly List<string> Values = new List<string>();
    static readonly List<Camera> ReflectionCameras = new List<Camera>();
    static GameObject hidden;
    static string output;
    ReflectionProbe probe;
    ParticleSystem particles;
    int renderId, frames;
    public static bool Finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        output = Environment.GetEnvironmentVariable("HIDDEN_ROOTS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        new GameObject("Root control").AddComponent<HiddenRootsControl>();
    }
    static void Put(string key, object value) => Values.Add("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}");
    static void Roots(string phase)
    {
        var scene = SceneManager.GetActiveScene();
        Put(phase + ".rootCount", scene.rootCount);
        var marker = new GameObject(phase + " marker");
        Put(phase + ".nextSibling", marker.transform.GetSiblingIndex());
        Put(phase + ".rootsAfterMarker", scene.GetRootGameObjects().Length);
        UnityEngine.Object.DestroyImmediate(marker);
    }
    void Start()
    {
        QualitySettings.realtimeReflectionProbes = true;
        Roots("plain");
        var camera = new GameObject("View").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        probe = new GameObject("Reflection").AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 16;
        probe.cullingMask = 0;
        renderId = probe.RenderProbe();
    }
    void Update()
    {
        frames++;
        if (frames == 20)
        {
            Put("reflection.finished", probe.IsFinishedRendering(renderId));
            Roots("reflection");
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
                if (camera.cameraType == CameraType.Reflection) ReflectionCameras.Add(camera);
            var template = new GameObject("Light template").AddComponent<Light>();
            template.enabled = false;
            particles = new GameObject("Particles").AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.startLifetime = 1000;
            main.startSpeed = 0;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = particles.emission;
            emission.enabled = false;
            var lights = particles.lights;
            lights.enabled = true;
            lights.light = template;
            lights.ratio = 1;
            lights.maxLights = 4;
            particles.Emit(2);
            particles.Play();
        }
        if (frames != 40) return;
        Put("particles.live", particles.particleCount > 0);
        Roots("particles");
        // A user-created unsaved root is a separate lifetime control.
        hidden = new GameObject("Unsaved lifetime") { hideFlags = HideFlags.HideAndDontSave };

        Finished = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
#endif
    }
    public static void AfterPlay()
    {
        File.WriteAllLines(output + ".diagnostics.jsonl", new[] {
            "hidden.sceneValid=" + (hidden != null && hidden.scene.IsValid()),
            "hidden.aliveAfterPlay=" + (hidden != null)
        });
        Put("reflection.cameraCount", ReflectionCameras.Count);
        bool alive = ReflectionCameras.Count > 0;
        bool found = alive;
        var available = Resources.FindObjectsOfTypeAll<Camera>();
        foreach (var camera in ReflectionCameras)
        {
            alive &= camera != null;
            found &= Array.IndexOf(available, camera) >= 0;
        }
        Put("reflection.aliveAfterPlay", alive);
        Put("reflection.foundAfterPlay", found);
        if (hidden != null) UnityEngine.Object.DestroyImmediate(hidden);
        Values.Sort(StringComparer.Ordinal);
        Values.Add("{\"k\":\"complete\",\"v\":\"1\"}");
        File.WriteAllLines(output, Values);
    }
}
