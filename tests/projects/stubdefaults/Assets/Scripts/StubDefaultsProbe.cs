using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Records what the Editor answers in Play mode for members whose type's default (0, null,
/// false, the zero matrix, an enum's 0) could differ from Unity's answer: one JSON line per value,
/// read a few frames after the scene loads.</summary>
public class StubDefaultsProbe : MonoBehaviour
{
    static readonly StringBuilder records = new StringBuilder();
    static string output;

    static void Record(string key, string value) =>
        records.Append("{\"key\":\"").Append(key).Append("\",\"value\":\"").Append(value).Append("\"}\n");

    // G9 and G17 round-trip and print the same digits on Mono and CoreCLR (R does not).
    static string F(float v) => v.ToString("G9", CultureInfo.InvariantCulture);
    static string F(double v) => v.ToString("G17", CultureInfo.InvariantCulture);
    static void Record(string key, float v) => Record(key, F(v));
    static void Record(string key, int v) => Record(key, v.ToString(CultureInfo.InvariantCulture));
    static void Record(string key, long v) => Record(key, v.ToString(CultureInfo.InvariantCulture));
    static void Record(string key, ulong v) => Record(key, v.ToString(CultureInfo.InvariantCulture));
    static void Record(string key, bool v) => Record(key, v ? "true" : "false");
    static void Record(string key, Color c) => Record(key, F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a));
    static void Record(string key, Vector2 v) => Record(key, F(v.x) + "," + F(v.y));
    static void Record<T>(string key, T e) where T : Enum => Record(key, e + "=" + Convert.ToInt64(e, CultureInfo.InvariantCulture));

    static void Record(string key, Matrix4x4 m)
    {
        var parts = new string[16];
        for (int i = 0; i < 16; i++) parts[i] = F(m[i % 4, i / 4]);
        Record(key, string.Join(",", parts));
    }

    static void Try(string key, Action read)
    {
        try { read(); }
        catch (Exception e) { Record(key, "throws " + e.GetType().Name); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        output = Environment.GetEnvironmentVariable("STUB_DEFAULTS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        new GameObject("StubDefaultsProbe").AddComponent<StubDefaultsProbe>();
    }

    IEnumerator Run()
    {
        for (int i = 0; i < 5; i++) yield return null;
        Read();
    }

    void Awake() => StartCoroutine(Run());

    static void Read()
    {
        Try("Gizmos.color", () => Record("Gizmos.color", Gizmos.color));
        Try("Gizmos.matrix", () => Record("Gizmos.matrix", Gizmos.matrix));
        Try("Gizmos.probeSize", () => Record("Gizmos.probeSize", Gizmos.probeSize));
        Try("Gizmos.exposure", () => Record("Gizmos.exposure", Gizmos.exposure == null ? "null" : Gizmos.exposure.name));

        Try("DynamicGI.updateThreshold", () => Record("DynamicGI.updateThreshold", DynamicGI.updateThreshold));
        Try("DynamicGI.materialUpdateTimeSlice", () => Record("DynamicGI.materialUpdateTimeSlice", DynamicGI.materialUpdateTimeSlice));
        Try("DynamicGI.synchronousMode", () => Record("DynamicGI.synchronousMode", DynamicGI.synchronousMode));
        Try("DynamicGI.isConverged", () => Record("DynamicGI.isConverged", DynamicGI.isConverged));

        Try("GarbageCollector.GCMode", () => Record("GarbageCollector.GCMode", UnityEngine.Scripting.GarbageCollector.GCMode));
        Try("GarbageCollector.incrementalTimeSliceNanoseconds", () => Record("GarbageCollector.incrementalTimeSliceNanoseconds", UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds));

        Try("ReflectionProbe.minBakedCubemapResolution", () => Record("ReflectionProbe.minBakedCubemapResolution", ReflectionProbe.minBakedCubemapResolution));
        Try("ReflectionProbe.maxBakedCubemapResolution", () => Record("ReflectionProbe.maxBakedCubemapResolution", ReflectionProbe.maxBakedCubemapResolution));
        Try("SplashScreen.isFinished", () => Record("SplashScreen.isFinished", SplashScreen.isFinished));
        Try("MuscleHandle.muscleHandleCount", () => Record("MuscleHandle.muscleHandleCount", UnityEngine.Animations.MuscleHandle.muscleHandleCount));
        Try("Caching.compressionEnabled", () => Record("Caching.compressionEnabled", Caching.compressionEnabled));
        Try("QualitySettings.maxQueuedFrames", () => Record("QualitySettings.maxQueuedFrames", QualitySettings.maxQueuedFrames));
        Try("Graphics.activeTier", () => Record("Graphics.activeTier", Graphics.activeTier));
        Try("Graphics.activeColorGamut", () => Record("Graphics.activeColorGamut", Graphics.activeColorGamut));
        Try("PixelPerfectRendering.pixelSnapSpacing", () => Record("PixelPerfectRendering.pixelSnapSpacing", UnityEngine.U2D.PixelPerfectRendering.pixelSnapSpacing));
        Try("RenderTexture.GetTexelOffset", () =>
        {
            var rt = new RenderTexture(4, 4, 0);
#pragma warning disable 618
            Record("RenderTexture.GetTexelOffset", rt.GetTexelOffset());
#pragma warning restore 618
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        });
        Try("Accessibility.AccessibilitySettings.fontScale", () => Record("Accessibility.AccessibilitySettings.fontScale", UnityEngine.Accessibility.AccessibilitySettings.fontScale));

        // Obsolete members whose message states a constant answer.
#pragma warning disable 618
        Try("SystemInfo.supportsRenderTextures", () => Record("SystemInfo.supportsRenderTextures", SystemInfo.supportsRenderTextures));
        Try("SystemInfo.supportsStencil", () => Record("SystemInfo.supportsStencil", SystemInfo.supportsStencil));
        Try("SystemInfo.supportsVertexPrograms", () => Record("SystemInfo.supportsVertexPrograms", SystemInfo.supportsVertexPrograms));
        Try("SystemInfo.graphicsPixelFillrate", () => Record("SystemInfo.graphicsPixelFillrate", SystemInfo.graphicsPixelFillrate));
        Try("RenderTexture.enabled", () => Record("RenderTexture.enabled", RenderTexture.enabled));
        Try("Application.isPlayer", () => Record("Application.isPlayer", Application.isPlayer));
        Try("AvatarMask.humanoidBodyPartCount", () =>
        {
            var mask = new AvatarMask();
            Record("AvatarMask.humanoidBodyPartCount", mask.humanoidBodyPartCount);
            UnityEngine.Object.Destroy(mask);
        });
#pragma warning restore 618

        // Members answered by hand-written code before this probe, kept here as regression guards.
        Try("ScalableBufferManager.widthScaleFactor", () => Record("ScalableBufferManager.widthScaleFactor", ScalableBufferManager.widthScaleFactor));
        Try("ScalableBufferManager.heightScaleFactor", () => Record("ScalableBufferManager.heightScaleFactor", ScalableBufferManager.heightScaleFactor));
        // The device list is the machine's; only whether it is an array is a contract.
        Try("Microphone.devices", () => Record("Microphone.devices", Microphone.devices == null ? "null" : "array"));
        Try("Camera.CalculateObliqueMatrix", () =>
        {
            var go = new GameObject("ObliqueCamera");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.aspect = 16f / 9f;
            Record("Camera.CalculateObliqueMatrix", camera.CalculateObliqueMatrix(new Vector4(0f, 0.25f, -1f, 2f)));
            UnityEngine.Object.Destroy(go);
        });

        Record("complete", "1");
        File.WriteAllText(output, records.ToString());
        Debug.Log("stub defaults complete: " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
