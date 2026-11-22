using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The capture (`bake_buffers.py native|engine`, <c>BAKED_BUFFERS_OUT</c> set): opens each scene
/// of <c>Assets/Scenes</c> in Edit mode and bakes it.
/// <para>In the Editor (<c>-executeMethod BuffersRecord.Record</c>) the bake is the Editor's own
/// in two steps, through its internal <c>UnityEditor.LightBaking</c> API (by reflection): the
/// scene's bake input and lightmap requests (<c>InputExtraction.ExtractFromScene</c>, which is
/// what <see cref="Lightmapping.Bake"/> starts from), written as the Editor serializes them, and
/// the Progressive CPU lightmapper's bake of them (<c>LightBaker.Bake</c>), which writes every
/// buffer of each lightmap unprocessed into <c>&lt;out&gt;/&lt;scene&gt;/lightmapRequest0</c>.
/// The instances' names go to <c>instances.txt</c> in the bake input's order.</para>
/// <para>With <c>BAKED_BUFFERS_AUTO</c> set (Unbound's <c>unbound test</c>) it starts itself once
/// Edit mode has run a few updates and bakes with <see cref="Lightmapping.Bake"/>; Unbound writes
/// its bake's buffers where <c>UNBOUND_BAKED_GI_BUFFERS</c> says. What is logged meanwhile goes
/// to <c>messages.txt</c> in both.</para>
/// </summary>
[InitializeOnLoad]
public static class BuffersRecord
{
    static BuffersRecord()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BAKED_BUFFERS_OUT"))) return;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BAKED_BUFFERS_AUTO"))) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int updates = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++updates < 30) return;
            EditorApplication.update -= wait;
            Run(scene =>
            {
                var folder = Path.Combine(Environment.GetEnvironmentVariable("BAKED_BUFFERS_OUT"), scene);
                Directory.CreateDirectory(folder);
                var said = new List<string>();
                Application.LogCallback listen = (message, stack, type) => said.Add(type + ": " + message);
                Application.logMessageReceived += listen;
                bool baked;
                try { baked = Lightmapping.Bake(); }
                finally { Application.logMessageReceived -= listen; }
                File.WriteAllLines(Path.Combine(folder, "messages.txt"), said);
                return "bake " + baked;
            });
        };
        EditorApplication.update += wait;
    }

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    static void Run(Func<string, string> bake)
    {
        var output = Environment.GetEnvironmentVariable("BAKED_BUFFERS_OUT");
        var log = new List<string>();
        try
        {
            EditorSettings.asyncShaderCompilation = false;
            var scenes = new List<string>(Directory.GetFiles("Assets/Scenes", "*.unity"));
            scenes.Sort(StringComparer.Ordinal);
            foreach (var path in scenes)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                EditorSceneManager.OpenScene(path.Replace('\\', '/'));
                log.Add(name + ": " + bake(name));
            }
            log.Add("complete");
            File.WriteAllLines(Path.Combine(output, "log.txt.tmp"), log);
            File.Move(Path.Combine(output, "log.txt.tmp"), Path.Combine(output, "log.txt"));
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            log.Add("exception " + e);
            File.WriteAllLines(Path.Combine(output, "failed.txt"), log);
            EditorApplication.Exit(1);
        }
    }

    public static void Record() => Run(EditorBake);

    /// <summary>The Editor's bake of the open scene into <c>&lt;out&gt;/&lt;scene&gt;</c>.</summary>
    static string EditorBake(string scene)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("BAKED_BUFFERS_OUT"), scene);
        Directory.CreateDirectory(folder);
        var assembly = typeof(Editor).Assembly;
        var baker = assembly.GetType("UnityEditor.LightBaking.LightBaker", true);
        var extraction = assembly.GetType("UnityEditor.LightBaking.InputExtraction", true);
        Type requestsType = baker.GetNestedType("LightmapRequests"), mapType = extraction.GetNestedType("SourceMap");
        object input = Activator.CreateInstance(baker.GetNestedType("BakeInput"));
        object requests = Activator.CreateInstance(requestsType);
        object probes = Activator.CreateInstance(baker.GetNestedType("LightProbeRequests"));
        object device = Activator.CreateInstance(baker.GetNestedType("DeviceSettings"));
        object map = Activator.CreateInstance(mapType);
        object Call(Type type, string method, object target, params object[] arguments) =>
            type.GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).Invoke(target, arguments);

        // What the Editor says while it makes the bake input (of meshes it leaves out, ...).
        var said = new List<string>();
        Application.LogCallback listen = (message, stack, type) => said.Add(type + ": " + message);
        Application.logMessageReceived += listen;
        bool extracted;
        try { extracted = (bool)Call(extraction, "ExtractFromScene", null, folder, input, requests, probes, map); }
        finally { Application.logMessageReceived -= listen; }
        File.WriteAllLines(Path.Combine(folder, "messages.txt"), said);
        if (!extracted) return "the scene's bake input could not be extracted";
        Call(baker, "Serialize", null, Path.Combine(folder, "bakeInput.bin"), input);
        Call(baker, "SerializeLightmapRequests", null, Path.Combine(folder, "lightmapRequests.bin"), requests);
        var names = new List<string>();
        uint count = (uint)input.GetType().GetProperty("instanceCount").GetValue(input);
        for (int i = 0; i < count; i++)
        {
            var source = EditorUtility.InstanceIDToObject((int)Call(mapType, "GetInstanceInstanceID", map, i));
            var transform = source is Component component ? component.transform : source is GameObject go ? go.transform : null;
            names.Add(transform == null ? "?" : PathOf(transform));
        }
        File.WriteAllLines(Path.Combine(folder, "instances.txt"), names);
        if (!(bool)Call(device.GetType(), "Initialize", device, Enum.Parse(baker.GetNestedType("Backend"), "CPU")))
            return "the CPU lightmapper could not be initialized";
        return "bake " + Call(baker, "Bake", null, input, requests, probes, device);
    }
}
