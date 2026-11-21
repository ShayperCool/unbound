using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, what SceneView.CameraMode and SceneView.SceneViewState answer: defaults,
/// derived switches, copies, equality, text and the fields JsonUtility writes.
/// </summary>
static class SceneViewTypesProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("SCENE_VIEW_TYPES_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);

        Check("state.default", () => Describe(new SceneView.SceneViewState()));
        Check("state.allOff", () =>
        {
            var state = new SceneView.SceneViewState();
            state.SetAllEnabled(false);
            return Describe(state);
        });
        Check("state.allOn", () =>
        {
            var state = new SceneView.SceneViewState();
            state.SetAllEnabled(true);
            return Describe(state);
        });
        Check("state.fxOff", () =>
        {
            var state = new SceneView.SceneViewState { fxEnabled = false };
            state.SetAllEnabled(true);
            return Describe(state);
        });
        Check("state.mixed", () =>
        {
            var state = new SceneView.SceneViewState { showFog = false, alwaysRefresh = true, showClouds = false, showVisualEffectGraphs = false };
            return Describe(state);
        });
        Check("state.copy", () =>
        {
            var state = new SceneView.SceneViewState { showFog = false, alwaysRefresh = true, showClouds = false, fxEnabled = false, showVisualEffectGraphs = false };
            return Describe(new SceneView.SceneViewState(state));
        });
        Check("state.vfxOnlyOff", () =>
        {
            var state = new SceneView.SceneViewState();
            state.SetAllEnabled(true);
            state.showVisualEffectGraphs = false;
            return "allEnabled=" + state.allEnabled;
        });
#pragma warning disable CS0618
        Check("state.obsolete", () =>
        {
            var state = new SceneView.SceneViewState();
            state.Toggle(true);
            bool on = state.IsAllOn();
            state.showMaterialUpdate = false;
            return "isAllOn=" + on + "|alwaysRefresh=" + state.alwaysRefresh + "|materialUpdateEnabled=" + state.materialUpdateEnabled;
        });
#pragma warning restore CS0618
        Check("state.json", () =>
        {
            var state = new SceneView.SceneViewState();
            state.SetAllEnabled(false);
            state.fxEnabled = false;
            return JsonUtility.ToJson(state);
        });

        var ctor = typeof(SceneView.CameraMode).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(DrawCameraMode), typeof(string), typeof(string), typeof(bool) }, null);
        SceneView.CameraMode Mode(DrawCameraMode draw, string name, string section, bool show) =>
            (SceneView.CameraMode)ctor.Invoke(new object[] { draw, name, section, show });
        Check("mode.default", () => Describe(default(SceneView.CameraMode)));
        Check("mode.made", () => Describe(Mode(DrawCameraMode.Wireframe, "Wire", "Shading", false)));
        Check("mode.equality", () =>
        {
            var a = Mode(DrawCameraMode.Textured, "Shaded", "Shading Mode", true);
            var hidden = Mode(DrawCameraMode.Textured, "Shaded", "Shading Mode", false);
            var renamed = Mode(DrawCameraMode.Textured, "Other", "Shading Mode", true);
            var redrawn = Mode(DrawCameraMode.Wireframe, "Shaded", "Shading Mode", true);
            var resectioned = Mode(DrawCameraMode.Textured, "Shaded", "Other", true);
            var nulls = Mode(DrawCameraMode.Textured, null, null, true);
            return string.Join("|", new[]
            {
                "hidden==" + (a == hidden), "renamed==" + (a == renamed), "redrawn==" + (a == redrawn),
                "resectioned==" + (a == resectioned), "renamed!=" + (a != renamed),
                "nulls==" + (nulls == Mode(DrawCameraMode.Textured, null, null, false)),
                "nullVsEmpty==" + (nulls == Mode(DrawCameraMode.Textured, "", "", true)),
                "equalsBoxed=" + a.Equals((object)hidden), "equalsNull=" + a.Equals(null), "equalsString=" + a.Equals("x"),
                "hashSame=" + (a.GetHashCode() == hidden.GetHashCode()),
                "hashNullVsEmpty=" + (nulls.GetHashCode() == Mode(DrawCameraMode.Textured, "", "", true).GetHashCode()),
            });
        });
        Check("mode.json", () => JsonUtility.ToJson(Mode(DrawCameraMode.Overdraw, "Over", "Misc", false)));

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
        EditorApplication.Exit(0);
    }

    static string Describe(SceneView.SceneViewState s) => string.Join("|", new[]
    {
        "fog=" + s.showFog, "skybox=" + s.showSkybox, "flares=" + s.showFlares, "imageEffects=" + s.showImageEffects,
        "particles=" + s.showParticleSystems, "vfx=" + s.showVisualEffectGraphs, "alwaysRefresh=" + s.alwaysRefresh,
        "clouds=" + s.showClouds, "fx=" + s.fxEnabled,
        "fogEnabled=" + s.fogEnabled, "alwaysRefreshEnabled=" + s.alwaysRefreshEnabled, "skyboxEnabled=" + s.skyboxEnabled,
        "cloudsEnabled=" + s.cloudsEnabled, "flaresEnabled=" + s.flaresEnabled, "imageEffectsEnabled=" + s.imageEffectsEnabled,
        "particleSystemsEnabled=" + s.particleSystemsEnabled, "visualEffectGraphsEnabled=" + s.visualEffectGraphsEnabled,
        "allEnabled=" + s.allEnabled,
    });

    static string Describe(SceneView.CameraMode m) =>
        "text=" + (m.ToString() ?? "<null>") + "|drawMode=" + m.drawMode + "|name=" + (m.name ?? "<null>") + "|section=" + (m.section ?? "<null>");

    static void Check(string key, Func<string> read)
    {
        string value;
        try { value = read(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ":" + e.Message; }
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
