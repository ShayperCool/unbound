using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Playables;
using UnityEngine.Playables;

/// <summary>
/// Writes values.jsonl in $TEXPLAYABLES_OUT (tests/value_probe.py rows): the experimental texture
/// playables (UnityEngine.Experimental.Playables): CameraPlayable, MaterialEffectPlayable,
/// TextureMixerPlayable, TexturePlayableOutput and TexturePlayableBinding: what each makes in a
/// graph, its getters after its setters, the casts between them, and a texture output's source.
/// </summary>
public class TexturePlayablesProbe : MonoBehaviour
{
    const string Variable = "TEXPLAYABLES_OUT";
    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value)
    {
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value ?? "null") + "\"}\n");
        output.Flush();
    }

    static void Try(string key, Func<string> action)
    {
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
    }

    static string Name(UnityEngine.Object o) => o == null ? "null" : o.name;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("TexturePlayablesProbe").AddComponent<TexturePlayablesProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        PlayableGraph graph = default;
        try { Run(out graph); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        if (graph.IsValid()) graph.Destroy();
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    static void Run(out PlayableGraph graph)
    {
        graph = PlayableGraph.Create("TexturePlayablesProbe");
        var g = graph;
        var camera = new GameObject("ProbeCamera").AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.red;
        var other = new GameObject("OtherCamera").AddComponent<Camera>();
        other.enabled = false;
        var material = new Material(Shader.Find("Unlit/Color")) { name = "ProbeMaterial" };

        var cam = CameraPlayable.Create(g, camera);
        Row("camera.valid", cam.IsValid() + "," + ((Playable)cam).IsValid());
        Row("camera.type", ((Playable)cam).GetPlayableType()?.FullName);
        Row("camera.camera", Name(cam.GetCamera()));
        Row("camera.counts", ((Playable)cam).GetInputCount() + "," + ((Playable)cam).GetOutputCount());
        Row("camera.state", ((Playable)cam).GetPlayState() + "," + ((Playable)cam).GetDuration().ToString("R", CultureInfo.InvariantCulture) + "," + ((Playable)cam).GetSpeed().ToString("R", CultureInfo.InvariantCulture));
        cam.SetCamera(other);
        Row("camera.set", Name(cam.GetCamera()));
        cam.SetCamera(null);
        Row("camera.setNull", Name(cam.GetCamera()));
        Try("camera.createNull", () => { var p = CameraPlayable.Create(g, null); return p.IsValid() + "," + Name(p.GetCamera()); });
        Row("camera.equals", cam.Equals(cam) + "," + cam.Equals(CameraPlayable.Create(g, camera)));

        var effect = MaterialEffectPlayable.Create(g, material);
        Row("effect.type", ((Playable)effect).GetPlayableType()?.FullName);
        Row("effect.material", Name(effect.GetMaterial()) + "," + effect.GetPass());
        Row("effect.counts", ((Playable)effect).GetInputCount() + "," + ((Playable)effect).GetOutputCount());
        Row("effect.pass2", MaterialEffectPlayable.Create(g, material, 2).GetPass().ToString());
        effect.SetPass(5);
        effect.SetMaterial(null);
        Row("effect.set", Name(effect.GetMaterial()) + "," + effect.GetPass());
        Try("effect.createNull", () => { var p = MaterialEffectPlayable.Create(g, null, 7); return p.IsValid() + "," + Name(p.GetMaterial()) + "," + p.GetPass(); });

        var mixer = TextureMixerPlayable.Create(g);
        Row("mixer.type", ((Playable)mixer).GetPlayableType()?.FullName);
        Row("mixer.counts", ((Playable)mixer).GetInputCount() + "," + ((Playable)mixer).GetOutputCount());
        Row("mixer.equals", mixer.Equals(mixer).ToString());

        Try("camera.invalidGraph", () => CameraPlayable.Create(default(PlayableGraph), camera).IsValid().ToString());
        Try("effect.invalidGraph", () => MaterialEffectPlayable.Create(default(PlayableGraph), material).IsValid().ToString());
        Try("mixer.invalidGraph", () => TextureMixerPlayable.Create(default(PlayableGraph)).IsValid().ToString());
        Try("cast.cameraFromMixer", () => { var c = (CameraPlayable)(Playable)mixer; return c.IsValid().ToString(); });
        Try("cast.effectFromCamera", () => { var e = (MaterialEffectPlayable)(Playable)cam; return e.IsValid().ToString(); });
        Try("cast.mixerFromNull", () => { var m = (TextureMixerPlayable)Playable.Null; return m.IsValid().ToString(); });

        var rt = new RenderTexture(8, 8, 16) { name = "ProbeTarget" };
        var texOut = TexturePlayableOutput.Create(g, "ProbeOutput", rt);
        Row("output.valid", texOut.IsOutputValid() + "," + ((PlayableOutput)texOut).IsOutputValid());
        Row("output.type", ((PlayableOutput)texOut).GetPlayableOutputType()?.FullName);
        Row("output.target", Name(texOut.GetTarget()));
        Row("output.graphCounts", g.GetOutputCount() + "," + g.GetOutputCountByType<TexturePlayableOutput>() + "," + g.GetPlayableCount());
        Row("output.name", g.GetOutput(0).GetPlayableOutputType()?.Name);
        Row("output.null", TexturePlayableOutput.Null.IsOutputValid().ToString());
        Try("output.invalidGraph", () => TexturePlayableOutput.Create(default(PlayableGraph), "x", rt).IsOutputValid().ToString());
        texOut.SetTarget(null);
        Row("output.setNull", Name(texOut.GetTarget()));
        texOut.SetTarget(rt);
        Try("cast.outputFromNull", () => ((TexturePlayableOutput)PlayableOutput.Null).IsOutputValid().ToString());

        var binding = TexturePlayableBinding.Create("ProbeStream", camera);
        Row("binding", binding.streamName + "," + Name(binding.sourceObject) + "," + (binding.outputTargetType?.FullName ?? "null"));
        var bindingNull = TexturePlayableBinding.Create(null, null);
        Row("binding.null", (bindingNull.streamName ?? "null") + "," + Name(bindingNull.sourceObject) + "," + (bindingNull.outputTargetType?.FullName ?? "null"));

        // The output's source: a camera playable (what evaluating it draws is not recorded: the
        // target's pixels are the graphics device's).
        var drawCam = CameraPlayable.Create(g, camera);
        ((PlayableOutput)texOut).SetSourcePlayable(drawCam);
        Row("output.source", ((PlayableOutput)texOut).GetSourcePlayable().GetPlayableType()?.Name + "," + g.GetPlayableCount());
    }
}
