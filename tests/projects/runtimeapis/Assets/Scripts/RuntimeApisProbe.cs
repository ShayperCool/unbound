using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Writes values.jsonl in $RUNTIMEAPIS_OUT (tests/value_probe.py rows): what runtime engine members
/// that render pipelines and packages call answer and do: Camera stereo matrices on a camera
/// without stereo, the scene view filter mode, ColorGamutUtility, GraphicsSettings.GetShaderMode,
/// Transform rotation orders and euler hints, Screen.SetMSAASamples, CommandBuffer fast memory
/// calls, ScriptableRenderContext geometry emission, attributes and exceptions.
/// </summary>
public class RuntimeApisProbe : MonoBehaviour
{
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static string Describe(object value)
    {
        if (value is Matrix4x4 m) return m.ToString("F5");
        if (value is Vector3 v) return v.ToString("F5");
        if (value is Array a) return value.GetType().Name + "[" + a.Length + "]";
        return value.ToString();
    }

    static void Try(string key, Func<object> action)
    {
        logs.Clear();
        string result;
        try
        {
            var value = action();
            result = ReferenceEquals(value, null) ? "returns null" : "returns " + Describe(value);
        }
        catch (Exception e)
        {
            if (e is TargetInvocationException t && t.InnerException != null) e = t.InnerException;
            result = "throws " + e.GetType().FullName + ": " + e.Message;
        }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    static void Do(string key, Action action) => Try(key, () => { action(); return "done"; });

    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    static object Call(Type type, object target, string name, params object[] args)
    {
        var method = type.GetMethods(All).First(m => m.Name == name && m.GetParameters().Length == args.Length);
        return method.Invoke(target, args);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUNTIMEAPIS_OUT"))) return;
        new GameObject("RuntimeApisProbe").AddComponent<RuntimeApisProbe>();
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("RUNTIMEAPIS_OUT");
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        yield return null;
        try
        {
            Cameras();
            Gamuts();
            ShaderModes();
            Rotations();
            Rendering();
            Attributes();
        }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        yield return Scriptable();
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static readonly Matrix4x4 P = Matrix4x4.Perspective(50f, 1.5f, 0.2f, 300f);
    static readonly Matrix4x4 P2 = Matrix4x4.Perspective(70f, 2f, 0.5f, 100f);
    static readonly Matrix4x4 V = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f), Vector3.one);

    static void Cameras()
    {
        var cam = new GameObject("Cam").AddComponent<Camera>();
        var left = Camera.StereoscopicEye.Left;
        var right = Camera.StereoscopicEye.Right;
        Try("camera.stereo_enabled", () => cam.stereoEnabled);
        Try("camera.projection", () => cam.projectionMatrix);
        Try("camera.nonjittered", () => cam.nonJitteredProjectionMatrix);
        Try("camera.stereo_projection_left", () => cam.GetStereoProjectionMatrix(left));
        Try("camera.stereo_nonjittered_left", () => cam.GetStereoNonJitteredProjectionMatrix(left));
        Try("camera.stereo_nonjittered_right", () => cam.GetStereoNonJitteredProjectionMatrix(right));
        Try("camera.stereo_view_left", () => cam.GetStereoViewMatrix(left));
        Do("camera.set_stereo_projection_left", () => cam.SetStereoProjectionMatrix(left, P));
        Try("camera.after_set.stereo_projection_left", () => cam.GetStereoProjectionMatrix(left));
        Try("camera.after_set.stereo_projection_right", () => cam.GetStereoProjectionMatrix(right));
        Try("camera.after_set.stereo_nonjittered_left", () => cam.GetStereoNonJitteredProjectionMatrix(left));
        Try("camera.after_set.projection", () => cam.projectionMatrix);
        Do("camera.copy_device_to_nonjittered_left", () => cam.CopyStereoDeviceProjectionMatrixToNonJittered(left));
        Try("camera.after_copy.stereo_nonjittered_left", () => cam.GetStereoNonJitteredProjectionMatrix(left));
        Try("camera.after_copy.stereo_projection_left", () => cam.GetStereoProjectionMatrix(left));
        Do("camera.set_stereo_projection_left_other", () => cam.SetStereoProjectionMatrix(left, P2));
        Try("camera.after_other.stereo_projection_left", () => cam.GetStereoProjectionMatrix(left));
        Try("camera.after_other.stereo_nonjittered_left", () => cam.GetStereoNonJitteredProjectionMatrix(left));
        Do("camera.set_stereo_projection_right", () => cam.SetStereoProjectionMatrix(right, P2));
        Try("camera.after_right.stereo_nonjittered_right", () => cam.GetStereoNonJitteredProjectionMatrix(right));
        Do("camera.copy_device_to_nonjittered_right", () => cam.CopyStereoDeviceProjectionMatrixToNonJittered(right));
        Do("camera.set_stereo_projection_right_again", () => cam.SetStereoProjectionMatrix(right, P));
        Try("camera.after_right_again.stereo_nonjittered_right", () => cam.GetStereoNonJitteredProjectionMatrix(right));
        Do("camera.reset_stereo_projection", () => cam.ResetStereoProjectionMatrices());
        Try("camera.after_reset.stereo_projection_left", () => cam.GetStereoProjectionMatrix(left));
        Try("camera.after_reset.stereo_nonjittered_left", () => cam.GetStereoNonJitteredProjectionMatrix(left));
        Do("camera.set_stereo_view_left", () => cam.SetStereoViewMatrix(left, V));
        Try("camera.after_set.stereo_view_left", () => cam.GetStereoViewMatrix(left));
        Try("camera.after_set.stereo_view_right", () => cam.GetStereoViewMatrix(right));
        Try("camera.after_set.world_to_camera", () => cam.worldToCameraMatrix);
        Do("camera.reset_stereo_view", () => cam.ResetStereoViewMatrices());
        Try("camera.after_reset.stereo_view_left", () => cam.GetStereoViewMatrix(left));
        Try("camera.scene_view_filter_mode", () => Call(typeof(Camera), cam, "get_sceneViewFilterMode"));
        Destroy(cam.gameObject);
    }

    static void Gamuts()
    {
        var utility = typeof(Camera).Assembly.GetType("UnityEngine.ColorGamutUtility");
        foreach (ColorGamut gamut in Enum.GetValues(typeof(ColorGamut)))
        {
            Try("gamut." + gamut + ".primaries", () => Call(utility, null, "GetColorPrimaries", gamut));
            Try("gamut." + gamut + ".white_point", () => Call(utility, null, "GetWhitePoint", gamut));
            Try("gamut." + gamut + ".transfer", () => Call(utility, null, "GetTransferFunction", gamut));
        }
        foreach (int value in new[] { -1, 7 })
        {
            var gamut = Enum.ToObject(typeof(ColorGamut), value);
            Try("gamut.value" + value + ".primaries", () => Call(utility, null, "GetColorPrimaries", gamut));
            Try("gamut.value" + value + ".white_point", () => Call(utility, null, "GetWhitePoint", gamut));
            Try("gamut.value" + value + ".transfer", () => Call(utility, null, "GetTransferFunction", gamut));
        }
    }

    static void ShaderModes()
    {
        foreach (BuiltinShaderType type in Enum.GetValues(typeof(BuiltinShaderType)))
        {
            Try("shader_mode." + type, () => GraphicsSettings.GetShaderMode(type));
        }
        foreach (int value in new[] { -1, 8 })
        {
            Try("shader_mode.value" + value, () => GraphicsSettings.GetShaderMode((BuiltinShaderType)value));
        }
    }

    static void Rotations()
    {
        var go = new GameObject("Rotated");
        var t = go.transform;
        t.localRotation = Quaternion.Euler(30f, 50f, 70f);
        Try("transform.rotation_order", () => Call(typeof(Transform), t, "get_rotationOrder"));
        foreach (int order in new[] { 0, 1, 2, 3, 4, 5 })
        {
            var rotationOrder = Enum.ToObject(typeof(Transform).Assembly.GetType("UnityEngine.RotationOrder"), order);
            Try("transform.euler_order" + rotationOrder, () => Call(typeof(Transform), t, "GetLocalEulerAngles", rotationOrder));
        }
        Try("transform.euler", () => t.localEulerAngles);
        Do("transform.set_euler_hint", () => Call(typeof(Transform), t, "SetLocalEulerHint", new Vector3(390f, 50f, 70f)));
        Try("transform.after_hint.euler", () => t.localEulerAngles);
        Try("transform.after_hint.rotation", () => t.localRotation.eulerAngles);
        Try("transform.after_hint.euler_orderZXY", () => Euler(t, 4));
        Try("transform.after_hint.euler_orderXYZ", () => Euler(t, 0));
        Do("transform.set_euler_hint_other", () => Call(typeof(Transform), t, "SetLocalEulerHint", new Vector3(10f, 10f, 10f)));
        Try("transform.after_other_hint.euler", () => t.localEulerAngles);
        Try("transform.after_other_hint.euler_orderZXY", () => Euler(t, 4));
        var orderType = typeof(Transform).Assembly.GetType("UnityEngine.RotationOrder");
        Do("transform.set_rotation_order", () => typeof(Transform).GetProperty("rotationOrder", All).SetValue(t, Enum.ToObject(orderType, 3)));
        Try("transform.after_order.rotation_order", () => Call(typeof(Transform), t, "get_rotationOrder"));
        Try("transform.after_order.euler", () => t.localEulerAngles);
        Try("transform.after_order.rotation", () => t.localRotation.eulerAngles);
        Try("transform.after_order.euler_orderYXZ", () => Euler(t, 3));
        Try("transform.after_order.euler_orderZXY", () => Euler(t, 4));
        t.localRotation = Quaternion.Euler(12.34567f, 0f, 0f);
        Try("transform.fraction.euler_orderZXY", () => Euler(t, 4));
        Try("transform.fraction.euler", () => t.localEulerAngles);
        t.localRotation = Quaternion.Euler(-20f, 200f, 300f);
        Try("transform.wide.euler_orderZXY", () => Euler(t, 4));
        Try("transform.wide.euler_orderXYZ", () => Euler(t, 0));
        t.localEulerAngles = new Vector3(400f, 0f, 0f);
        Try("transform.set_euler_400.euler_orderZXY", () => Euler(t, 4));
        Try("transform.set_euler_400.euler", () => t.localEulerAngles);
        // Gimbal lock: the middle angle at a quarter turn, without a hint and with one.
        Do("transform.clear_hint", () => Call(typeof(Transform), t, "SetLocalEulerHint", Vector3.zero));
        t.localRotation = Quaternion.Euler(90f, 30f, 0f);
        Try("transform.lock90.euler_orderZXY", () => Euler(t, 4));
        Try("transform.lock90.euler_orderXYZ", () => Euler(t, 0));
        Try("transform.lock90.euler", () => t.localEulerAngles);
        t.localRotation = Quaternion.Euler(90f, 30f, 20f);
        Try("transform.lock90z.euler_orderZXY", () => Euler(t, 4));
        Try("transform.lock90z.euler", () => t.localEulerAngles);
        t.localRotation = Quaternion.Euler(-90f, 30f, 0f);
        Try("transform.lock_neg90.euler_orderZXY", () => Euler(t, 4));
        Try("transform.lock_neg90.euler", () => t.localEulerAngles);
        t.localRotation = Quaternion.Euler(89.9f, 30f, 0f);
        Try("transform.near89.euler_orderZXY", () => Euler(t, 4));
        Try("transform.near89.euler", () => t.localEulerAngles);
        t.localRotation = Quaternion.Euler(89.9999f, 30f, 0f);
        Try("transform.near_lock.euler_orderZXY", () => Euler(t, 4));
        Try("transform.near_lock.euler", () => t.localEulerAngles);
        t.localRotation = Quaternion.AngleAxis(90f, Vector3.up) * Quaternion.AngleAxis(30f, Vector3.right);
        Try("transform.lock_y90.euler_orderXYZ", () => Euler(t, 0));
        Do("transform.lock_hint", () => Call(typeof(Transform), t, "SetLocalEulerHint", new Vector3(90f, 10f, 20f)));
        t.localRotation = Quaternion.Euler(90f, 30f, 0f);
        Try("transform.lock90_hint.euler_orderZXY", () => Euler(t, 4));
        Try("transform.lock90_hint.euler_orderXYZ", () => Euler(t, 0));
        Do("transform.lock_hint_y", () => Call(typeof(Transform), t, "SetLocalEulerHint", new Vector3(10f, 90f, 5f)));
        t.localRotation = Quaternion.AngleAxis(90f, Vector3.up) * Quaternion.AngleAxis(30f, Vector3.right);
        Try("transform.lock_y90_hint.euler_orderXYZ", () => Euler(t, 0));
        Destroy(go);
    }

    static object Euler(Transform t, int order) =>
        Call(typeof(Transform), t, "GetLocalEulerAngles", Enum.ToObject(typeof(Transform).Assembly.GetType("UnityEngine.RotationOrder"), order));

    static void Rendering()
    {
        Try("quality.anti_aliasing", () => QualitySettings.antiAliasing);
        Try("screen.msaa_samples", () => typeof(Screen).GetProperty("msaaSamples", All)?.GetValue(null) ?? "absent");
        Do("screen.set_msaa_4", () => Call(typeof(Screen), null, "SetMSAASamples", 4));
        Try("screen.after_set.msaa_samples", () => typeof(Screen).GetProperty("msaaSamples", All)?.GetValue(null) ?? "absent");
        Try("screen.after_set.quality_anti_aliasing", () => QualitySettings.antiAliasing);
        Do("screen.set_msaa_1", () => Call(typeof(Screen), null, "SetMSAASamples", 1));
        int quality = QualitySettings.antiAliasing;
        foreach (int samples in new[] { 0, 4, 8 })
        {
            QualitySettings.antiAliasing = samples;
            Try("screen.quality_aa" + samples + ".msaa_samples", () => typeof(Screen).GetProperty("msaaSamples", All)?.GetValue(null) ?? "absent");
        }
        QualitySettings.antiAliasing = quality;
        Do("texture.streaming_debug_properties", () => Call(typeof(Texture), null, "SetStreamingTextureMaterialDebugProperties", 3));
        var cb = new CommandBuffer { name = "fast" };
        Try("command_buffer.size", () => cb.sizeInBytes);
        Do("command_buffer.switch_into_fast_memory", () => cb.SwitchIntoFastMemory(BuiltinRenderTextureType.CameraTarget, FastMemoryFlags.SpillTop, 0.5f, true));
        Try("command_buffer.after_into.size", () => cb.sizeInBytes);
        Do("command_buffer.switch_out_of_fast_memory", () => cb.SwitchOutOfFastMemory(BuiltinRenderTextureType.CameraTarget, true));
        Try("command_buffer.after_out.size", () => cb.sizeInBytes);
        cb.Release();
        var cam = new GameObject("Emit").AddComponent<Camera>();
        Do("context.emit_geometry_for_camera", () => ScriptableRenderContext.EmitGeometryForCamera(cam));
        Do("context.emit_world_geometry_for_scene_view", () => ScriptableRenderContext.EmitWorldGeometryForSceneView(cam));
        Do("context.emit_geometry_for_null", () => ScriptableRenderContext.EmitGeometryForCamera(null));
        Destroy(cam.gameObject);
        Try("scripting_runtime.user_assemblies", () =>
        {
            var names = (string[])Call(typeof(Camera).Assembly.GetType("UnityEngine.ScriptingRuntime"), null, "GetAllUserAssemblies");
            return string.Join(",", names.OrderBy(n => n, StringComparer.Ordinal));
        });
        var layers = typeof(SortingLayer).GetField("onLayerChanged", All);
        Try("sorting_layer.on_layer_changed", () => layers.GetValue(null) == null ? "null" : "set");
    }

    sealed class ProbePipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline() => new ProbePipeline();
    }

    sealed class ProbePipeline : RenderPipeline
    {
        protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }
    }

    static object ScreenSamples() => typeof(Screen).GetProperty("msaaSamples", All).GetValue(null);

    /// <summary>Screen MSAA with a scriptable render pipeline (a pipeline that renders nothing).</summary>
    static IEnumerator Scriptable()
    {
        int quality = QualitySettings.antiAliasing;
        var asset = ScriptableObject.CreateInstance<ProbePipelineAsset>();
        Do("srp.assign", () => GraphicsSettings.defaultRenderPipeline = asset);
        Try("srp.msaa_samples", ScreenSamples);
        yield return null;
        Try("srp.frame.msaa_samples", ScreenSamples);
        Do("srp.set_msaa_4", () => Call(typeof(Screen), null, "SetMSAASamples", 4));
        Try("srp.after_set_4.msaa_samples", ScreenSamples);
        Try("srp.after_set_4.quality_anti_aliasing", () => QualitySettings.antiAliasing);
        yield return null;
        Try("srp.after_set_4.frame.msaa_samples", ScreenSamples);
        Do("srp.set_msaa_1", () => Call(typeof(Screen), null, "SetMSAASamples", 1));
        Try("srp.after_set_1.msaa_samples", ScreenSamples);
        Try("srp.after_set_1.quality_anti_aliasing", () => QualitySettings.antiAliasing);
        yield return null;
        Try("srp.after_set_1.frame.msaa_samples", ScreenSamples);
        Do("srp.set_msaa_3", () => Call(typeof(Screen), null, "SetMSAASamples", 3));
        Try("srp.after_set_3.msaa_samples", ScreenSamples);
        QualitySettings.antiAliasing = 8;
        Try("srp.quality_aa8.msaa_samples", ScreenSamples);
        yield return null;
        Try("srp.quality_aa8.frame.msaa_samples", ScreenSamples);
        QualitySettings.antiAliasing = quality;
        Do("srp.unassign", () => GraphicsSettings.defaultRenderPipeline = null);
        Try("srp.unassigned.msaa_samples", ScreenSamples);
        yield return null;
        Try("srp.unassigned.frame.msaa_samples", ScreenSamples);
    }

    static void Attributes()
    {
        Try("attribute.recreate_pipeline", () => Activator.CreateInstance(typeof(Camera).Assembly.GetType("UnityEngine.Rendering.RecreatePipelineOnChangeAttribute"), true).GetType().FullName);
        Try("attribute.prefer_binary", () => new PreferBinarySerialization().GetType().FullName);
        Try("attribute.assembly_is_editor", () => Activator.CreateInstance(typeof(Camera).Assembly.GetType("UnityEngine.AssemblyIsEditorAssembly"), true).GetType().FullName);
        Try("exception.invalid_import", () =>
        {
            var e = (Exception)Activator.CreateInstance(typeof(Camera).Assembly.GetType("UnityEngine.Rendering.InvalidImportException"), All, null, new object[] { "bad import" }, null);
            return e.GetType().BaseType.FullName + ": " + e.Message;
        });
    }
}
