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
/// buffer of each lightmap unprocessed into <c>&lt;out&gt;/&lt;scene&gt;/lightmapRequest0</c>; then
/// its whole bake (<see cref="Lightmapping.Bake"/>) makes the lightmaps of them, whose files
/// (<c>Lightmap-N_comp_light.exr</c>) go beside them.
/// The instances' and the lights' names go to <c>instances.txt</c> and <c>lights.txt</c> in the
/// bake input's order.</para>
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
        // The light probes: their points (in the probe requests' file) and, besides their
        // light, the part of their rays that met a back face.
        var probeRequests = (Array)Call(probes.GetType(), "GetProbeRequests", probes);
        var probeRequestType = baker.GetNestedType("ProbeRequest");
        for (int i = 0; i < probeRequests.Length; i++)
        {
            object request = probeRequests.GetValue(i);
            var mask = probeRequestType.GetField("outputTypeMask");
            mask.SetValue(request, Enum.ToObject(mask.FieldType, Convert.ToUInt32(mask.GetValue(request)) | 4u));
            probeRequests.SetValue(request, i);
        }
        Call(probes.GetType(), "SetLightProbeRequests", probes, probeRequests);
        Call(baker, "SerializeLightProbeRequests", null, Path.Combine(folder, "probeRequests.bin"), probes);
        var names = new List<string>();
        uint count = (uint)input.GetType().GetProperty("instanceCount").GetValue(input);
        for (int i = 0; i < count; i++)
        {
            var source = EditorUtility.InstanceIDToObject((int)Call(mapType, "GetInstanceInstanceID", map, i));
            var transform = source is Component component ? component.transform : source is GameObject go ? go.transform : null;
            names.Add(transform == null ? "?" : PathOf(transform));
        }
        File.WriteAllLines(Path.Combine(folder, "instances.txt"), names);
        var lights = new List<string>();
        uint lightCount = (uint)Call(input.GetType(), "GetLightCount", input);
        for (int i = 0; i < lightCount; i++)
        {
            var source = EditorUtility.InstanceIDToObject((int)Call(mapType, "GetLightInstanceID", map, i));
            var transform = source is Component component ? component.transform : source is GameObject go ? go.transform : null;
            lights.Add(transform == null ? "?" : PathOf(transform));
        }
        File.WriteAllLines(Path.Combine(folder, "lights.txt"), lights);
        if (!(bool)Call(device.GetType(), "Initialize", device, Enum.Parse(baker.GetNestedType("Backend"), "CPU")))
            return "the CPU lightmapper could not be initialized";
        var baked = Call(baker, "Bake", null, input, requests, probes, device);
        // The lightmaps the Editor's whole bake (Lightmapping.Bake) makes of the scene's buffers,
        // as it writes them beside the scene (none where the bake keeps no light). Its
        // lightmapper's process now and then fails (the bake answers false, or true without its
        // files): the bake is run again, up to three times; its last answer goes to wholeBake.txt.
        var written = Path.Combine("Assets/Scenes", scene);
        bool whole = false;
        // (Under load the bake now and then answers true without having written all its files:
        // run again as for a false answer. Every scene here writes some, and one with baked
        // global illumination its lightmaps.)
        bool lightmaps = Lightmapping.lightingSettings != null && Lightmapping.lightingSettings.bakedGI;
        for (int attempt = 0; attempt < 3 && !whole; attempt++)
            whole = Lightmapping.Bake() && Directory.Exists(written)
                && (!lightmaps || Directory.GetFiles(written, "Lightmap-*_comp_light.exr").Length > 0);
        File.WriteAllText(Path.Combine(folder, "wholeBake.txt"), whole ? "True" : "False");
        if (Directory.Exists(written))
        {
            foreach (var lightmap in Directory.GetFiles(written, "Lightmap-*_comp_light.exr"))
                File.Copy(lightmap, Path.Combine(folder, Path.GetFileName(lightmap)), true);
            // Every file the bake wrote there, and the folder's .meta, as written.
            var kept = Path.Combine(folder, "written");
            Directory.CreateDirectory(kept);
            if (File.Exists(written + ".meta")) File.Copy(written + ".meta", Path.Combine(kept, "folder.meta"), true);
            foreach (var file in Directory.GetFiles(written))
                File.Copy(file, Path.Combine(kept, Path.GetFileName(file)), true);
        }
        return "bake " + baked;
    }
}
