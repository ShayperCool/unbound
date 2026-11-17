using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Records, under the Universal Render Pipeline, the sky and the lights it is drawn with while
/// scenes holding directional lights are added, made active and unloaded: for each state the
/// active scene, <c>RenderSettings.sun</c>, the skybox material, the ambient probe's constant
/// term, three pixels of the drawn sky, the <c>_LightColor0</c>/<c>_WorldSpaceLightPos0</c> a
/// skybox is given and the <c>_MainLightColor</c>/<c>_MainLightPosition</c> an opaque pass is
/// given. Writes values.jsonl into URP_SUN_SCENES_OUT and exits.
/// </summary>
public sealed class UrpSunScenesProbe : MonoBehaviour
{
    const int QuadLayer = 30;
    readonly StringBuilder m_Out = new StringBuilder();
    Camera m_Camera;
    RenderTexture m_Sky, m_Pair;
    Texture2D m_ReadSky, m_ReadPair;
    Material m_SunSky, m_MainLight, m_BrightSky;
    GameObject m_Quad;
    Scene m_Base;
    int m_Turn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("URP_SUN_SCENES_OUT"))) return;
        var go = new GameObject("UrpSunScenesProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<UrpSunScenesProbe>();
    }

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
    void RecFloats(string key, params float[] values) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":[").Append(string.Join(",", values.Select(F))).Append("]}\n");

    /// <summary>A directional light of its own direction, in <paramref name="scene"/>.</summary>
    Light Directional(string name, float intensity, Color color, Scene scene)
    {
        var go = new GameObject(name);
        go.transform.rotation = Quaternion.Euler(25f + 9f * m_Turn, -50f + 37f * m_Turn, 0f);
        m_Turn++;
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        if (scene.IsValid() && go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
        return light;
    }

    void Prepare()
    {
        m_Base = SceneManager.GetActiveScene();
        var cameraObject = new GameObject("ProbeCamera");
        DontDestroyOnLoad(cameraObject);
        m_Camera = cameraObject.AddComponent<Camera>();
        m_Camera.enabled = false;
        m_Camera.fieldOfView = 90f;
        m_Camera.nearClipPlane = 0.1f;
        m_Camera.farClipPlane = 100f;
        m_Sky = new RenderTexture(4, 4, 24, RenderTextureFormat.ARGBFloat);
        m_Pair = new RenderTexture(2, 1, 24, RenderTextureFormat.ARGBFloat);
        m_ReadSky = new Texture2D(4, 4, TextureFormat.RGBAFloat, false);
        m_ReadPair = new Texture2D(2, 1, TextureFormat.RGBAFloat, false);
        m_SunSky = new Material(Resources.Load<Shader>("SunProbeSky"));
        m_MainLight = new Material(Resources.Load<Shader>("UrpSunMainLight"));
        // A procedural sky brighter than the default one, for a scene with its own skybox.
        m_BrightSky = new Material(Shader.Find("Skybox/Procedural")) { name = "BrightSky" };
        m_BrightSky.SetFloat("_Exposure", 2.5f);
        m_BrightSky.SetFloat("_AtmosphereThickness", 0.6f);
        m_Quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DontDestroyOnLoad(m_Quad);
        m_Quad.name = "MainLightQuad";
        m_Quad.layer = QuadLayer;
        m_Quad.GetComponent<Renderer>().sharedMaterial = m_MainLight;
        m_Quad.transform.SetPositionAndRotation(new Vector3(0f, -100f, 1f), Quaternion.identity);
        m_Quad.transform.localScale = new Vector3(4f, 4f, 1f);
        m_Quad.SetActive(false);
    }

    Color[] Render(RenderTexture target, Texture2D read)
    {
        m_Camera.targetTexture = target;
        m_Camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        m_Camera.targetTexture = null;
        return read.GetPixels();
    }

    void Record(string state)
    {
        Rec(state + ".active", SceneManager.GetActiveScene().name);
        Rec(state + ".sun", RenderSettings.sun == null ? "null" : RenderSettings.sun.name);
        Rec(state + ".skybox", RenderSettings.skybox == null ? "null" : RenderSettings.skybox.name);
        Rec(state + ".ambientMode", RenderSettings.ambientMode.ToString());
        var probe = RenderSettings.ambientProbe;
        RecFloats(state + ".ambientL0", probe[0, 0], probe[1, 0], probe[2, 0]);

        // The sky as drawn: looking north 20 degrees up, three pixels from the horizon upwards.
        m_Camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(-20f, 0f, 0f));
        m_Camera.clearFlags = CameraClearFlags.Skybox;
        m_Camera.cullingMask = 0;
        var sky = Render(m_Sky, m_ReadSky);
        foreach (var (name, index) in new[] { ("low", 1), ("mid", 6), ("high", 14) })
            RecFloats(state + ".sky." + name, sky[index].r, sky[index].g, sky[index].b);

        // The light a skybox is drawn with.
        var skybox = RenderSettings.skybox;
        RenderSettings.skybox = m_SunSky;
        var pair = Render(m_Pair, m_ReadPair);
        RenderSettings.skybox = skybox;
        RecFloats(state + ".skyLightColor", pair[0].r, pair[0].g, pair[0].b);
        RecFloats(state + ".skyLightPos", pair[1].r, pair[1].g, pair[1].b, pair[1].a);

        // The main light an opaque pass is given.
        m_Camera.transform.SetPositionAndRotation(new Vector3(0f, -100f, 0f), Quaternion.identity);
        m_Camera.clearFlags = CameraClearFlags.SolidColor;
        m_Camera.backgroundColor = Color.clear;
        m_Camera.cullingMask = 1 << QuadLayer;
        m_Quad.SetActive(true);
        pair = Render(m_Pair, m_ReadPair);
        m_Quad.SetActive(false);
        RecFloats(state + ".mainLightColor", pair[0].r, pair[0].g, pair[0].b);
        RecFloats(state + ".mainLightPos", pair[1].r, pair[1].g, pair[1].b, pair[1].a);
    }

    IEnumerator Unload(Scene scene)
    {
        var op = SceneManager.UnloadSceneAsync(scene);
        while (!op.isDone) yield return null;
        yield return null;
    }

    IEnumerator Start()
    {
        yield return null;
        Prepare();
        Record("base");

        // A scene with a directional light added beside the base scene, made active, given it as
        // the named sun, deactivated and activated again, then unloaded.
        var match = SceneManager.CreateScene("Match");
        var matchSun = Directional("MatchSun", 1.5f, new Color(1f, 0.9f, 0.75f), match);
        Record("matchLoaded");
        SceneManager.SetActiveScene(match);
        Record("matchActive");
        RenderSettings.sun = matchSun;
        Record("matchNamedSun");
        SceneManager.SetActiveScene(m_Base);
        Record("baseActiveAgain");
        SceneManager.SetActiveScene(match);
        Record("matchActiveAgain");
        yield return Unload(match);
        Record("matchUnloaded");

        // A dim light of the base scene beside a brighter one of an active added scene.
        var baseDim = Directional("BaseDim", 0.4f, Color.white, m_Base);
        Record("baseDim");
        var second = SceneManager.CreateScene("Second");
        Directional("SecondSun", 1.2f, new Color(0.8f, 0.9f, 1f), second);
        SceneManager.SetActiveScene(second);
        Record("secondActive");
        yield return Unload(second);
        Record("secondUnloaded");
        Destroy(baseDim.gameObject);
        yield return null;
        Record("baseDimDestroyed");

        // A light that outlives its scene (DontDestroyOnLoad) while the base scene is active.
        var kept = Directional("KeptSun", 0.8f, new Color(1f, 0.8f, 0.8f), m_Base);
        DontDestroyOnLoad(kept.gameObject);
        Record("keptSun");
        Destroy(kept.gameObject);
        yield return null;

        // A dim named sun beside a brighter light: the sky and the main light may part ways. Then
        // the named sun disabled.
        Directional("BrightOther", 1.5f, Color.white, m_Base);
        var namedDim = Directional("NamedDim", 0.3f, new Color(0.6f, 1f, 0.6f), m_Base);
        RenderSettings.sun = namedDim;
        Record("namedDim");
        namedDim.enabled = false;
        Record("namedDisabled");

        // An added scene with its own sky material, made active, left and unloaded.
        var third = SceneManager.CreateScene("Third");
        SceneManager.SetActiveScene(third);
        Record("thirdActive");
        RenderSettings.skybox = m_BrightSky;
        Record("thirdBrightSky");
        SceneManager.SetActiveScene(m_Base);
        Record("thirdLeft");
        SceneManager.SetActiveScene(third);
        Record("thirdActiveAgain");
        yield return Unload(third);
        Record("thirdUnloaded");

        var dir = Environment.GetEnvironmentVariable("URP_SUN_SCENES_OUT");
        Directory.CreateDirectory(dir);
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.Exit(0);
    }
}
