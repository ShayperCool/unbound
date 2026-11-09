using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// light-order.jsonl: which lights URP's Forward path sees and gives an object when there are
/// more than it can use. For each case (named lights made for it, the scene's own lights off) a
/// camera renders one small quad on its own layer with Probe/LightOrderDump and records:
/// <list type="bullet">
/// <item><c>visible</c>: the culling's visible lights, in order (the local lights only when the
/// case has several directional ones), and <c>main</c>, URP's main light;</item>
/// <item><c>unity_LightData</c> and <c>object</c>: the quad's per-object lights, in order (named by
/// their additional light position or direction).</item>
/// </list>
/// The cases: per-object order by colour (luminance of the light's colour times its intensity),
/// by distance and range, by render mode (Important first, Not Important last), additional
/// directional lights (shadows weigh), more lights than eight with a main light, the light's
/// culling mask and intensity, spot lights that reach the object's bounds only with their cone,
/// the visible lights' order (spot lights before point lights, each
/// nearest the camera first) and the visible light limit (33 with URP's Forward on this device).
/// </summary>
public class LightOrderProbe : ScriptableRendererFeature
{
    /// <summary>The visible lights of the last render: names in order, the main light's index.</summary>
    static readonly List<Light> s_Visible = new List<Light>();
    static int s_Main = -1;
    Pass m_Pass;

    public override void Create() => m_Pass = new Pass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) => renderer.EnqueuePass(m_Pass);

    class Pass : ScriptableRenderPass
    {
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var lights = frameData.Get<UniversalLightData>();
            s_Visible.Clear();
            foreach (var v in lights.visibleLights) s_Visible.Add(v.light);
            s_Main = lights.mainLightIndex;
        }
    }

    const int DumpLayer = 30, OtherLayer = 5, UnseenLayer = 6, Pixels = 9;

    /// <summary>Records every case into <paramref name="path"/>.</summary>
    public static void Record(string path, ScriptableRendererData rendererData)
    {
        var feature = CreateInstance<LightOrderProbe>();
        rendererData.rendererFeatures.Add(feature);
        rendererData.SetDirty();
        var sceneLights = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled).ToArray();
        foreach (var l in sceneLights) l.enabled = false;
        var sun = RenderSettings.sun;
        var target = new RenderTexture(Pixels, 1, 0, RenderTextureFormat.ARGBFloat);
        var cameraObject = new GameObject("Light Order Camera");
        cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -6f), Quaternion.identity);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 60f;
        camera.farClipPlane = 100f;
        camera.cullingMask = 1 << DumpLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.targetTexture = target;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = "Light Order Quad";
        quad.layer = DumpLayer;
        quad.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        var material = new Material(Shader.Find("Probe/LightOrderDump"));
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        var readback = new Texture2D(Pixels, 1, TextureFormat.RGBAFloat, false);
        var lines = new StringBuilder();
        var made = new List<Light>();
        var center = new Vector3(0f, 1f, 0f);
        // Unit offsets around the object whose distances to the camera all differ (equal
        // distances leave the visible lights' order to the light manager).
        Vector3 Around(int i)
        {
            float z = -0.9f + 0.2f * i, r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(i * 2.4f), r * Mathf.Sin(i * 2.4f), z);
        }

        Light Add(string name, LightType type, Vector3 position, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            made.Add(l);
            return l;
        }
        Light Directional(string name, Vector3 euler, float intensity, LightShadows shadows)
        {
            var l = Add(name, LightType.Directional, Vector3.zero, Color.white, intensity, 10f);
            l.transform.rotation = Quaternion.Euler(euler);
            l.shadows = shadows;
            return l;
        }
        string Name(Vector4 p)
        {
            foreach (var l in made)
            {
                var expected = l.type == LightType.Directional ? (Vector4)(-l.transform.forward) : (Vector4)l.transform.position;
                bool directional = l.type == LightType.Directional;
                if ((directional ? p.w < 0.5f : p.w > 0.5f) && ((Vector3)(p - expected)).magnitude < 1e-3f) return l.name;
            }
            return "?";
        }
        void Case(string name, Vector3 objectPosition, Action setup)
        {
            made.Clear();
            setup();
            quad.transform.position = objectPosition;
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, Pixels, 1), 0, 0);
            readback.Apply();
            RenderTexture.active = null;
            var data = (Vector4)readback.GetPixel(0, 0);
            var perObject = new List<string>();
            for (int i = 1; i < Pixels; i++)
            {
                var p = (Vector4)readback.GetPixel(i, 0);
                if (p.w >= 0f) perObject.Add(Name(p));
            }
            // The directional lights' order among themselves is the light manager's own (not
            // sorted): only the local lights are recorded when a case has several.
            bool directionals = made.Count(l => l.type == LightType.Directional) > 1;
            var visible = s_Visible.Where(l => !directionals || l == null || l.type != LightType.Directional)
                .Select(l => l != null ? l.name : "(none)");
            string main = s_Main >= 0 && s_Main < s_Visible.Count && s_Visible[s_Main] != null ? s_Visible[s_Main].name : "(none)";
            void Line(string key, string value) =>
                lines.Append("{\"key\":\"").Append(name).Append(' ').Append(key).Append("\",\"value\":").Append(value).Append("}\n");
            Line("visible", "\"" + string.Join(",", visible) + "\"");
            Line("main", "\"" + main + "\"");
            Line("unity_LightData", "[" + string.Join(",", new[] { data.x, data.y, data.z, data.w }.Select(f => f.ToString("R", CultureInfo.InvariantCulture))) + "]");
            Line("object", "\"" + string.Join(",", perObject) + "\"");
            foreach (var l in made) DestroyImmediate(l.gameObject);
            made.Clear();
            RenderSettings.sun = sun;
            camera.cullingMask = 1 << DumpLayer;
        }

        // Equal distance and intensity: the colour's luminance (0.3 r + 0.59 g + 0.11 b) ranks
        // them, not its sum; ten lights, eight per object.
        Case("luminance", center, () =>
        {
            var colors = new (string, Color)[]
            {
                ("Red", Color.red), ("Green", Color.green), ("Blue", Color.blue), ("Yellow", new Color(1f, 1f, 0f)),
                ("Cyan", new Color(0f, 1f, 1f)), ("Magenta", new Color(1f, 0f, 1f)), ("White", Color.white),
                ("Gray", new Color(0.5f, 0.5f, 0.5f)), ("Violet", new Color(0.5f, 0f, 1f)), ("DarkGreen", new Color(0f, 0.3f, 0f)),
            };
            for (int i = 0; i < colors.Length; i++) Add(colors[i].Item1, LightType.Point, center + Around(i), colors[i].Item2, 1f, 5f);
        });
        // White lights: the distance from the object's centre against the range,
        // intensity / (1 + 25 d² / range²); one reaches the quad's bounds but not its centre,
        // one does not reach it at all.
        Case("attenuation", center, () =>
        {
            Add("D1R2", LightType.Point, center + new Vector3(1f, 0f, 0f), Color.white, 1f, 2f);
            Add("D2R8", LightType.Point, center + new Vector3(0f, 2f, 0f), Color.white, 1f, 8f);
            Add("D05R15", LightType.Point, center + new Vector3(0f, 0f, -0.5f), Color.white, 1f, 1.5f);
            Add("D3R20", LightType.Point, center + new Vector3(-3f, 0f, 0f), Color.white, 1f, 20f);
            Add("D2R4I2", LightType.Point, center + new Vector3(0f, -2f, 0f), Color.white, 2f, 4f);
            Add("Edge", LightType.Point, center + new Vector3(0f, 1.15f, 0f), Color.white, 4f, 1.07f);
            Add("Out", LightType.Point, center + new Vector3(-1.5f, 0f, 0f), Color.white, 4f, 1.3f);
        });
        // Important lights before every Auto one, Not Important ones after them, whatever the
        // brightness.
        Case("render_mode", center, () =>
        {
            Add("Important02", LightType.Point, center + Around(0), Color.white, 0.2f, 5f).renderMode = LightRenderMode.ForcePixel;
            Add("Auto1", LightType.Point, center + Around(1), Color.white, 1f, 5f);
            Add("NotImportant5", LightType.Point, center + Around(2), Color.white, 5f, 5f).renderMode = LightRenderMode.ForceVertex;
            Add("Auto05", LightType.Point, center + Around(3), Color.white, 0.5f, 5f);
            Add("Important01", LightType.Point, center + Around(4), Color.white, 0.1f, 5f).renderMode = LightRenderMode.ForcePixel;
        });
        // A main light (the sun) and other directional lights: a directional light with shadows
        // weighs sixteen times its brightness, one without them its brightness, against point
        // lights at the object's centre.
        Case("directional", center, () =>
        {
            RenderSettings.sun = Directional("Sun", new Vector3(50f, 30f, 0f), 2f, LightShadows.None);
            Directional("Soft01", new Vector3(40f, -60f, 0f), 0.1f, LightShadows.Soft);
            Directional("None01", new Vector3(60f, 120f, 0f), 0.1f, LightShadows.None);
            Directional("Hard006", new Vector3(30f, 200f, 0f), 0.06f, LightShadows.Hard);
            Add("Point1", LightType.Point, center, Color.white, 1f, 5f);
            Add("Point05", LightType.Point, center + new Vector3(0f, 0f, 0.01f), Color.white, 0.5f, 5f);
        });
        // Ten point lights and a main light that would come first: eight per-object lights left
        // after the main light is taken out.
        Case("limit_main", center, () =>
        {
            Directional("Main", new Vector3(50f, 30f, 0f), 1f, LightShadows.None);
            for (int i = 0; i < 10; i++)
                Add("P" + i, LightType.Point, center + new Vector3(0.3f + 0.27f * i, 0f, 0f), Color.white, 1f, 10f);
        });
        // A main light whose culling mask leaves out the object's layer (the camera sees its other
        // layer): unity_LightData.z, whether the main light lights the object.
        Case("main_mask", center, () =>
        {
            camera.cullingMask = (1 << DumpLayer) | (1 << OtherLayer);
            Directional("Main", new Vector3(50f, 30f, 0f), 1f, LightShadows.None).cullingMask = 1 << OtherLayer;
            Add("Point", LightType.Point, center + Around(0), Color.white, 1f, 5f);
        });
        // The light's culling mask: one that leaves out the object's layer lights the camera's
        // other layer (visible, not the object's), one outside the camera's mask is not
        // visible; neither is a light below 0.001 intensity.
        Case("culling_mask", center, () =>
        {
            camera.cullingMask = (1 << DumpLayer) | (1 << OtherLayer);
            Add("OtherLayer", LightType.Point, center + Around(0), Color.white, 1f, 5f).cullingMask = 1 << OtherLayer;
            Add("Unseen", LightType.Point, center + Around(1), Color.white, 1f, 5f).cullingMask = 1 << UnseenLayer;
            Add("Zero", LightType.Point, center + Around(2), Color.white, 0f, 5f);
            Add("Dim00005", LightType.Point, center + Around(3), Color.white, 0.0005f, 5f);
            Add("Dim0002", LightType.Point, center + Around(4), Color.white, 0.002f, 5f);
            Add("Default", LightType.Point, center + Around(5), Color.white, 0.5f, 5f);
        });
        // The visible lights' order: spot lights, then point lights, each the nearest to the
        // camera first (made in another order, the brighter farther).
        Case("visible_order", center, () =>
        {
            Add("PointMid", LightType.Point, new Vector3(0.5f, 1f, 0f), Color.white, 1f, 20f);
            Add("SpotFar", LightType.Spot, new Vector3(-1f, 2f, 4f), Color.white, 3f, 20f).transform.LookAt(center);
            Add("PointNear", LightType.Point, new Vector3(-0.5f, 1.2f, -3f), Color.white, 0.5f, 20f);
            Add("PointFar", LightType.Point, new Vector3(1f, 0.8f, 3f), Color.white, 4f, 20f);
            Add("SpotNear", LightType.Spot, new Vector3(1f, 2f, -2f), Color.white, 1f, 20f).transform.LookAt(center);
            foreach (var l in made) if (l.type == LightType.Spot) l.spotAngle = 120f;
        });
        // Spot lights whose range reaches the object: one pointing at it, one pointing away, one
        // whose cone passes beside it; only the first lights it.
        Case("spot_reach", center, () =>
        {
            var toward = Add("Toward", LightType.Spot, center + new Vector3(1.5f, 0.3f, -0.4f), Color.white, 1f, 5f);
            toward.transform.LookAt(center);
            var away = Add("Away", LightType.Spot, center + new Vector3(-1.2f, 0.4f, -0.7f), Color.white, 2f, 5f);
            away.transform.rotation = Quaternion.LookRotation(away.transform.position - center);
            var beside = Add("Beside", LightType.Spot, center + new Vector3(0.2f, 1.6f, -0.9f), Color.white, 3f, 5f);
            beside.transform.LookAt(center + new Vector3(1.6f, 0f, 0f));
            foreach (var l in made) l.spotAngle = 40f;
        });
        // More lights than URP's Forward culling keeps (its additional light limit plus one): the
        // nearest to the camera stay, whatever their brightness.
        Case("visible_limit", center, () =>
        {
            for (int i = 0; i < 36; i++)
            {
                int x = i % 6, z = i / 6;
                Add("G" + i, LightType.Point, new Vector3(-1.5f + 0.6f * x + 0.01f * z, 0.5f + 0.1f * x, -2f + 0.9f * z), Color.white, 0.2f + 0.1f * (i % 7), 0.4f);
            }
        });

        File.WriteAllText(path, lines.ToString());
        RenderSettings.sun = sun;
        foreach (var l in sceneLights) l.enabled = true;
        camera.targetTexture = null;
        target.Release();
        DestroyImmediate(cameraObject);
        DestroyImmediate(quad);
        DestroyImmediate(material);
        DestroyImmediate(readback);
        rendererData.rendererFeatures.Remove(feature);
        rendererData.SetDirty();
        DestroyImmediate(feature);
    }
}
