using System;
using System.Collections;
using System.IO;
using UnityEngine;

public class DependencyProbe : MonoBehaviour
{
    public static Action Finished;
    string output;
    static string B(bool value) => value ? "true" : "false";
    void Record(string key, bool value) => File.AppendAllText(output, key + "=" + B(value) + "\n");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("FLARE_DEPENDENCY_OUT") != null)
            new GameObject("DependencyProbe").AddComponent<DependencyProbe>();
    }
    IEnumerator Start()
    {
        output = Path.Combine(Environment.GetEnvironmentVariable("FLARE_DEPENDENCY_OUT"), "values.txt");
        File.WriteAllText(output, "");
        int warnings = 0;
        Application.LogCallback log = (message, stack, kind) => { if (kind == LogType.Warning || kind == LogType.Error) warnings++; };
        Application.logMessageReceived += log;
        var orphan = Instantiate(Resources.Load<GameObject>("Orphan"));
        Record("loaded.layer", orphan.GetComponent<FlareLayer>() != null);
        Record("loaded.camera", orphan.GetComponent<Camera>() != null);

        var sceneOrphan = GameObject.Find("SceneOrphan");
        Record("scene.exists", sceneOrphan != null);
        Record("scene.camera", sceneOrphan != null && sceneOrphan.GetComponent<Camera>() != null);
        warnings = 0;
        var added = new GameObject("Added");
        added.AddComponent<FlareLayer>();
        Record("add.camera", added.GetComponent<Camera>() != null);
        Record("add.warnings", warnings != 0);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -5);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        var target = new RenderTexture(64, 64, 24);
        target.Create();
        camera.targetTexture = target;
        var flare = new GameObject("Flare").AddComponent<LensFlare>();
        flare.flare = Resources.Load<Flare>("S20");
        flare.brightness = 1;
        flare.fadeSpeed = 1000;
        Time.captureDeltaTime = 1f / 30;
        for (int i = 0; i < 5; i++) yield return new WaitForEndOfFrame();
        Record("orphan.draws", Lit(target));
        camera.enabled = false;
        camera.gameObject.AddComponent<FlareLayer>();
        Record("disabled.staysDisabled", !camera.enabled);
        for (int i = 0; i < 5; i++) yield return new WaitForEndOfFrame();
        Record("disabled.automaticDraws", Lit(target));
        camera.Render();
        Record("disabled.manualDraws", Lit(target));
        for (int i = 0; i < 5; i++) { camera.Render(); yield return new WaitForEndOfFrame(); }
        File.AppendAllText(Path.Combine(Path.GetDirectoryName(output), "init-trace.txt"), "afterFiveExplicitRenders=" + B(Lit(target)) + "\n");
        for (int i = 0; i < 55; i++) { camera.Render(); yield return new WaitForEndOfFrame(); }
        Record("disabled.repeatedManualDraws", Lit(target));
        camera.enabled = true;
        for (int i = 0; i < 5; i++) yield return new WaitForEndOfFrame();
        Record("enabled.draws", Lit(target));
        camera.enabled = false;
        camera.Render();
        Record("disabled.warmManualDraws", Lit(target));
        camera.enabled = true;
        camera.GetComponent<FlareLayer>().enabled = false;
        for (int i = 0; i < 5; i++) yield return new WaitForEndOfFrame();
        Record("layerDisabled.draws", Lit(target));
        var coldCamera = new GameObject("ColdEnabledCamera").AddComponent<Camera>();
        coldCamera.CopyFrom(camera);
        coldCamera.transform.position = camera.transform.position;
        coldCamera.gameObject.AddComponent<FlareLayer>();
        camera.enabled = false;
        coldCamera.Render();
        Record("coldEnabled.manualFirstDraws", Lit(target));
        yield return new WaitForEndOfFrame();
        Record("coldEnabled.firstDraws", Lit(target));
        yield return new WaitForEndOfFrame();
        Record("coldEnabled.secondDraws", Lit(target));
        flare.enabled = false;
        var newFlare = new GameObject("NewFlare").AddComponent<LensFlare>();
        newFlare.flare = Resources.Load<Flare>("S20");
        newFlare.brightness = 1;
        newFlare.fadeSpeed = 1000;
        coldCamera.Render();
        Record("warmCamera.newSourceManualFirstDraws", Lit(target));
        yield return new WaitForEndOfFrame();
        Record("warmCamera.newSourceFirstDraws", Lit(target));
        var flagCamera = new GameObject("FlagCamera").AddComponent<Camera>();
        flagCamera.CopyFrom(coldCamera);
        flagCamera.transform.position = coldCamera.transform.position;
        flagCamera.gameObject.AddComponent<FlareLayer>();
        flagCamera.enabled = false;
        flagCamera.Render();
        Record("enabledThenDisabled.manualFirstDraws", Lit(target));
        coldCamera.enabled = false;
        newFlare.enabled = false;
        var disabledNewFlare = new GameObject("DisabledNewFlare").AddComponent<LensFlare>();
        disabledNewFlare.flare = Resources.Load<Flare>("S20");
        disabledNewFlare.brightness = 1;
        disabledNewFlare.fadeSpeed = 1000;
        coldCamera.Render();
        Record("warmDisabledCamera.newSourceManualFirstDraws", Lit(target));
        Application.logMessageReceived -= log;
        Record("complete", true);
        if (Finished != null) Finished(); else Application.Quit();
    }
    static bool Lit(RenderTexture target)
    {
        var saved = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
        image.Apply();
        bool lit = false;
        foreach (var pixel in image.GetPixels()) if (pixel.r > 0.02f || pixel.g > 0.02f || pixel.b > 0.02f) lit = true;
        DestroyImmediate(image);
        RenderTexture.active = saved;
        return lit;
    }
}
